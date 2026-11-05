using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Identity;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkiaSharp;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class LotApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private HttpClient Client() => fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
    private async Task<HttpClient> Login(string role = Roles.Farmer, bool other = false)
    {
        var client = Client();
        var account = other ? fixture.OtherFarmer : fixture.Accounts[role];
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { account.Email, password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return client;
    }
    private async Task<Guid> Category(bool active = true)
    {
        using var client = await Login(Roles.Administrator);
        var response = await client.PostAsJsonAsync("/api/v1/admin/catalog/categories", new { code = "L-" + Guid.NewGuid().ToString("N")[..20], name = "Loại kiểm thử Phase 2", active });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryView>())!.Id;
    }
    private async Task<LotContent> Content() => new() { CategoryId = await Category(), Title = "Lô test " + Guid.NewGuid().ToString("N"), Description = "Khai báo kiểm thử, không phải kiểm định", Location = "Địa điểm test",
        Quantity = 20, QuantityUnit = "thùng", Weight = 300.125m, WeightUnit = "kg", ProposedStartingPrice = 123456.7891m, ProposedIncrement = 10.125m,
        Currency = "vnd", PriceBasis = "cả lô (đề xuất)", ProposedStartsAt = DateTimeOffset.UtcNow.AddDays(1), ProposedEndsAt = DateTimeOffset.UtcNow.AddDays(2) };
    private static async Task<LotView> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}, actual {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<LotView>())!;
    }
    private async Task<LotView> Create(HttpClient farmer) => await Read(await farmer.PostAsJsonAsync("/api/v1/lots", new CreateLotRequest(await Content())), HttpStatusCode.Created);
    public static byte[] Photo(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 24, int height = 16)
    {
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(SKColors.ForestGreen);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(format, 90); return encoded.ToArray();
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, LotView lot, byte[]? bytes = null, string name = "image.png", string mime = "image/png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Photo()); file.Headers.ContentType = new(mime);
        form.Add(file, "file", name); form.Add(new StringContent(lot.Version.ToString()), "expectedVersion");
        return await client.PostAsync($"/api/v1/lots/{lot.Id}/images", form);
    }
    private async Task<LotView> Pending(HttpClient farmer)
    {
        var lot = await Create(farmer); lot = await Read(await Upload(farmer, lot), HttpStatusCode.Created);
        return await Read(await farmer.PostAsJsonAsync($"/api/v1/lots/{lot.Id}/submit", new LotVersionRequest(lot.Version)), HttpStatusCode.OK);
    }
    private static Task<HttpResponseMessage> Decision(HttpClient admin, LotView lot, string outcome = "Approve", string reason = "Đã đối chiếu hồ sơ và ảnh khai báo")
        => admin.PostAsJsonAsync($"/api/v1/admin/lots/{lot.Id}/decision", new LotDecisionRequest(lot.Version, lot.CurrentRevisionId!.Value, outcome, reason));

    [PostgresFact]
    public async Task NewEndpointsEnforce401AndRoleGuards()
    {
        using var anonymous = Client();
        foreach (var route in new[] { "/api/v1/me/profile", "/api/v1/lots", "/api/v1/admin/lots/pending", "/api/v1/media/" + Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        foreach (var role in Roles.All)
        {
            using var client = await Login(role);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/profile")).StatusCode);
            Assert.Equal(role == Roles.Farmer ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/lots")).StatusCode);
            Assert.Equal(role == Roles.Administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/lots/pending")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task ProfileUpdatesOnlyDisplayNameWithVersionAndAudit()
    {
        foreach (var role in Roles.All)
        {
            using var client = await Login(role); var before = (await client.GetFromJsonAsync<ProfileView>("/api/v1/me/profile"))!;
            var response = await client.PatchAsJsonAsync("/api/v1/me/profile", new UpdateProfileRequest(" Tên test " + role, before.Version));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var after = (await response.Content.ReadFromJsonAsync<ProfileView>())!;
            Assert.Equal(before.Version + 1, after.Version); Assert.Equal(before.Email, after.Email); Assert.Equal(role, after.Role);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync("/api/v1/me/profile", new UpdateProfileRequest("Cũ", before.Version))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/v1/me/profile", new { displayName = "Leo quyền", expectedVersion = after.Version, role = "Administrator", isEnabled = false, email = "changed@invalid.test" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/v1/me/profile", new UpdateProfileRequest(" ", after.Version))).StatusCode);
            await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.AsNoTracking().SingleAsync(x => x.Id == before.Id); Assert.Equal(before.Email, row.Email); Assert.Equal(after.DisplayName, row.DisplayName);
            Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityId == row.Id && x.Action == "ProfileUpdated"));
            Assert.DoesNotContain("passwordHash", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }
    [PostgresFact]
    public async Task CreateEditReadPersistExactDecimalsAndRejectStaleUpdate()
    {
        using var farmer = await Login(); var lot = await Create(farmer);
        var changed = lot.Content with { Title = "Tên lô đã sửa", Weight = 400.125m };
        var after = await Read(await farmer.PatchAsJsonAsync($"/api/v1/lots/{lot.Id}", new UpdateLotRequest(changed, lot.Version)), HttpStatusCode.OK);
        Assert.Equal(2, after.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await farmer.PatchAsJsonAsync($"/api/v1/lots/{lot.Id}", new UpdateLotRequest(lot.Content, lot.Version))).StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Lots.AsNoTracking().SingleAsync(x => x.Id == lot.Id);
        Assert.Equal(400.125m, stored.Content.Weight); Assert.Equal(123456.7891m, stored.Content.ProposedStartingPrice); Assert.Equal(LotStates.Draft, stored.State);
        Assert.Equal(fixture.Accounts[Roles.Farmer].Id, stored.FarmerId);
    }
    [PostgresFact]
    public async Task InputValidationAndInactiveCategoriesDoNotCreateLots()
    {
        using var farmer = await Login(); var content = await Content();
        foreach (var bad in new[] { content with { Title = " " }, content with { Weight = 0 }, content with { Quantity = 0.0001m },
            content with { Currency = "invalid" }, content with { PriceBasis = "" }, content with { ProposedStartsAt = null }, content with { ProposedIncrement = 0 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await farmer.PostAsJsonAsync("/api/v1/lots", new CreateLotRequest(bad))).StatusCode);
        var inactive = content with { CategoryId = await Category(false) };
        Assert.Equal(HttpStatusCode.BadRequest, (await farmer.PostAsJsonAsync("/api/v1/lots", new CreateLotRequest(inactive))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await farmer.PostAsJsonAsync("/api/v1/lots", new { content, farmerId = fixture.OtherFarmer.Id, state = "Approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await farmer.GetAsync("/api/v1/lots?page=0")).StatusCode);
    }
    [PostgresFact]
    public async Task OtherOwnersCannotReadEditSubmitOrReadImagesAndFarmerCannotApprove()
    {
        using var farmer = await Login(); using var other = await Login(other: true); var lot = await Create(farmer);
        lot = await Read(await Upload(farmer, lot), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/lots/{lot.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"/api/v1/lots/{lot.Id}", new UpdateLotRequest(lot.Content, lot.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/lots/{lot.Id}/submit", new LotVersionRequest(lot.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(lot.Images[0].Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(other, lot)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmer.PostAsJsonAsync($"/api/v1/admin/lots/{lot.Id}/decision", new LotDecisionRequest(lot.Version, Guid.NewGuid(), "Approve", "Tự duyệt"))).StatusCode);
        var otherList = await other.GetFromJsonAsync<LotPage>("/api/v1/lots"); Assert.DoesNotContain(otherList!.Items, x => x.Id == lot.Id);
    }
    [PostgresFact]
    public async Task SubmissionWithoutImagesFailsAtomically()
    {
        using var farmer = await Login(); var lot = await Create(farmer);
        var response = await farmer.PostAsJsonAsync($"/api/v1/lots/{lot.Id}/submit", new LotVersionRequest(lot.Version));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("images", await response.Content.ReadAsStringAsync());
        var after = (await farmer.GetFromJsonAsync<LotView>($"/api/v1/lots/{lot.Id}"))!;
        Assert.Equal(LotStates.Draft, after.State); Assert.Equal(lot.Version, after.Version); Assert.Empty(after.Revisions);
    }
    [PostgresFact]
    public async Task ImagesAreDecodedReencodedAndPrivateWithSafeDownloadHeaders()
    {
        using var farmer = await Login(); using var enterprise = await Login(Roles.Enterprise); var lot = await Create(farmer);
        var polyglot = Photo().Concat(Encoding.UTF8.GetBytes("<script>payload()</script>")).ToArray();
        lot = await Read(await Upload(farmer, lot, polyglot, "../../untrusted.png"), HttpStatusCode.Created);
        lot = await Read(await Upload(farmer, lot, Photo(SKEncodedImageFormat.Jpeg), "photo.jpg", "image/jpeg"), HttpStatusCode.Created);
        Assert.Equal(2, lot.Images.Count);
        foreach (var image in lot.Images)
        {
            var response = await farmer.GetAsync(image.Url); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            Assert.DoesNotContain("untrusted", response.Content.Headers.ContentDisposition!.ToString());
            var bytes = await response.Content.ReadAsByteArrayAsync(); Assert.DoesNotContain("payload", Encoding.UTF8.GetString(bytes));
            using var decoded = SKBitmap.Decode(bytes); Assert.Equal(24, decoded.Width); Assert.Equal(16, decoded.Height);
            Assert.Equal(HttpStatusCode.Forbidden, (await enterprise.GetAsync(image.Url)).StatusCode);
        }
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.LotImages.CountAsync(x => x.LotId == lot.Id && x.Sha256.Length == 64));
    }
    [PostgresFact]
    public async Task SpoofedCorruptOversizedOrHugeDimensionImagesAreRejectedWithoutRows()
    {
        using var farmer = await Login(); var lot = await Create(farmer);
        var vectors = new[] { (Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), "image.png", "image/png"),
            (Photo(), "image.svg", "image/svg+xml"), (Photo(), "image.jpg", "image/jpeg"),
            (Photo()[..25], "broken.png", "image/png"), (new byte[ImageLimits.MaxBytes + 1], "big.png", "image/png"),
            (Photo(width: 6001, height: 1), "wide.png", "image/png") };
        foreach (var (bytes, name, mime) in vectors)
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(farmer, lot, bytes, name, mime)).StatusCode);
        var after = (await farmer.GetFromJsonAsync<LotView>($"/api/v1/lots/{lot.Id}"))!;
        Assert.Empty(after.Images); Assert.Equal(lot.Version, after.Version);
    }
    [PostgresFact]
    public async Task AdminApprovesSubmittedSnapshotAndAllApprovedEditsAreBlocked()
    {
        using var farmer = await Login(); using var admin = await Login(Roles.Administrator); var pending = await Pending(farmer);
        var queue = (await admin.GetFromJsonAsync<LotPage>("/api/v1/admin/lots/pending?query=" + Uri.EscapeDataString(pending.Content.Title)))!;
        Assert.Contains(queue.Items, x => x.Id == pending.Id);
        var approved = await Read(await Decision(admin, pending), HttpStatusCode.OK);
        Assert.Equal(LotStates.Approved, approved.State); Assert.Single(approved.Reviews); Assert.Equal(fixture.Accounts[Roles.Administrator].Id, approved.Reviews[0].AdminId);
        Assert.Equal(HttpStatusCode.Conflict, (await farmer.PatchAsJsonAsync($"/api/v1/lots/{approved.Id}", new UpdateLotRequest(approved.Content with { Title = "Không được sửa" }, approved.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(farmer, approved)).StatusCode);
        var detach = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/lots/{approved.Id}/images/{approved.Images[0].Id}") { Content = JsonContent.Create(new LotVersionRequest(approved.Version)) };
        Assert.Equal(HttpStatusCode.Conflict, (await farmer.SendAsync(detach)).StatusCode);
        var seen = (await farmer.GetFromJsonAsync<LotView>($"/api/v1/lots/{approved.Id}"))!;
        Assert.Equal(LotStates.Approved, seen.State); Assert.Equal(pending.Content, seen.Revisions[0].Content);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(seen.Images[0].Url)).StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(LotStates.Approved, (await db.Lots.SingleAsync(x => x.Id == seen.Id)).State);
        Assert.Single(await db.LotReviews.Where(x => x.LotId == seen.Id).ToListAsync());
    }
    [PostgresFact]
    public async Task RejectEditDetachResubmitKeepsPriorRevisionImagesAndReason()
    {
        using var farmer = await Login(); using var admin = await Login(Roles.Administrator); var first = await Pending(farmer);
        var rejected = await Read(await Decision(admin, first, "Reject", "Cần ảnh rõ hơn"), HttpStatusCode.OK);
        var edited = await Read(await farmer.PatchAsJsonAsync($"/api/v1/lots/{first.Id}", new UpdateLotRequest(first.Content with { Title = "Bản khai đã sửa" }, rejected.Version)), HttpStatusCode.OK);
        var detach = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/lots/{first.Id}/images/{first.Images[0].Id}") { Content = JsonContent.Create(new LotVersionRequest(edited.Version)) };
        edited = await Read(await farmer.SendAsync(detach), HttpStatusCode.OK);
        edited = await Read(await Upload(farmer, edited), HttpStatusCode.Created);
        var second = await Read(await farmer.PostAsJsonAsync($"/api/v1/lots/{first.Id}/submit", new LotVersionRequest(edited.Version)), HttpStatusCode.OK);
        Assert.Equal(2, second.Revisions.Count); Assert.Equal(first.Content, second.Revisions[0].Content);
        Assert.Equal(first.Images[0].Id, second.Revisions[0].Images[0].Id); Assert.NotEqual(first.Images[0].Id, second.Revisions[1].Images[0].Id);
        Assert.Equal("Cần ảnh rõ hơn", second.Reviews[0].Reason);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/lots/{first.Id}/decision", new LotDecisionRequest(second.Version, first.CurrentRevisionId!.Value, "Approve", "Nhầm bản cũ"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Decision(admin, second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(first.Images[0].Url)).StatusCode);
    }
    [PostgresFact]
    public async Task ConcurrentApprovalAndRejectionCommitExactlyOneDecision()
    {
        using var farmer = await Login(); using var admin1 = await Login(Roles.Administrator); using var admin2 = await Login(Roles.Administrator);
        var lot = await Pending(farmer);
        var results = await Task.WhenAll(Decision(admin1, lot), Decision(admin2, lot, "Reject", "Ý kiến khác"));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.LotReviews.CountAsync(x => x.LotId == lot.Id));
        Assert.Equal(lot.Version + 1, (await db.Lots.SingleAsync(x => x.Id == lot.Id)).Version);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityId == lot.Id && (x.Action == "LotApproved" || x.Action == "LotRejected")));
    }
    [PostgresFact]
    public async Task ConcurrentImageChangeAndSubmitCannotProduceMixedSnapshot()
    {
        using var farmer1 = await Login(); using var farmer2 = await Login(); var lot = await Create(farmer1);
        lot = await Read(await Upload(farmer1, lot), HttpStatusCode.Created);
        var results = await Task.WhenAll(Upload(farmer1, lot), farmer2.PostAsJsonAsync($"/api/v1/lots/{lot.Id}/submit", new LotVersionRequest(lot.Version)));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var seen = (await farmer1.GetFromJsonAsync<LotView>($"/api/v1/lots/{lot.Id}"))!;
        Assert.Equal(lot.Version + 1, seen.Version);
        if (seen.State == LotStates.PendingReview) { Assert.Single(seen.Images); Assert.Single(seen.Revisions[0].Images); }
        else { Assert.Equal(LotStates.Draft, seen.State); Assert.Equal(2, seen.Images.Count); Assert.Empty(seen.Revisions); }
    }
    [PostgresFact]
    public async Task InvalidReviewDoesNotWriteDecisionOrAudit()
    {
        using var farmer = await Login(); using var admin = await Login(Roles.Administrator); var lot = await Pending(farmer);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decision(admin, lot, reason: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decision(admin, lot, outcome: "Publish")).StatusCode);
        var after = (await farmer.GetFromJsonAsync<LotView>($"/api/v1/lots/{lot.Id}"))!;
        Assert.Equal(lot.Version, after.Version); Assert.Equal(LotStates.PendingReview, after.State); Assert.Empty(after.Reviews);
    }
    [PostgresFact]
    public async Task PostgresLotCheckConstraintRejectsInvalidQuantity()
    {
        using var farmer = await Login(); var lot = await Create(farmer);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE lots SET quantity = 0 WHERE id = {lot.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(lot.Content.Quantity, (await db.Lots.AsNoTracking().SingleAsync(x => x.Id == lot.Id)).Content.Quantity);
    }
    [PostgresFact]
    public async Task DatabaseFaultCannotProduceFalseLotSuccess()
    {
        using var farmer = await Login(); fixture.Fault.FailNextRead();
        var response = await farmer.GetAsync("/api/v1/lots");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Password=", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
    [PostgresFact]
    public async Task AdministratorCannotApproveALotTheyPreviouslyOwnedAsFarmer()
    {
        using var farmer = await Login(); var lot = await Pending(farmer);
        var ownerId = fixture.Accounts[Roles.Farmer].Id;
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Users.SingleAsync(x => x.Id == ownerId);
        owner.Role = Roles.Administrator; await db.SaveChangesAsync();
        try
        {
            using var promoted = await Login();
            Assert.Equal(HttpStatusCode.Forbidden, (await Decision(promoted, lot)).StatusCode);
            Assert.False(await db.LotReviews.AnyAsync(x => x.LotId == lot.Id));
        }
        finally { owner.Role = Roles.Farmer; await db.SaveChangesAsync(); }
    }
    [PostgresFact]
    public async Task ImageResourceLimitCannotBeExceeded()
    {
        using var farmer = await Login(); var lot = await Create(farmer);
        for (var n = 0; n < ImageLimits.MaxImagesPerLot; n++) lot = await Read(await Upload(farmer, lot), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(farmer, lot)).StatusCode);
        var seen = (await farmer.GetFromJsonAsync<LotView>($"/api/v1/lots/{lot.Id}"))!;
        Assert.Equal(ImageLimits.MaxImagesPerLot, seen.Images.Count); Assert.Equal(lot.Version, seen.Version);
    }
    [PostgresFact]
    public async Task CookieProfileAndUploadRequireCsrfTokens()
    {
        using var tokenClient = await Login(); var lot = await Create(tokenClient);
        using var cookie = fixture.Host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await cookie.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        cookie.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await cookie.PostAsJsonAsync("/api/v1/auth/login?useCookies=true", new { email = fixture.Accounts[Roles.Farmer].Email, password = fixture.Password })).StatusCode);
        cookie.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var profile = (await cookie.GetFromJsonAsync<ProfileView>("/api/v1/me/profile"))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await cookie.PatchAsJsonAsync("/api/v1/me/profile", new UpdateProfileRequest("Thiếu CSRF", profile.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(cookie, lot)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cookie.GetAsync("/pages/farmer/lots.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cookie.GetAsync("/pages/admin/lot-inspection.html")).StatusCode);
        csrf = await cookie.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); cookie.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Upload(cookie, lot)).StatusCode);
    }
    [PostgresFact]
    public async Task DemoAccountExceptionIsBlockedOutsideDevelopmentOrTesting()
    {
        // Staging host still uses the fixture's isolated TEST database/schema.
        using var host = fixture.Host.WithWebHostBuilder(builder => builder.UseEnvironment("Staging"));
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = fixture.Accounts[Roles.Farmer].Email, password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        var blocked = await client.PostAsJsonAsync("/api/v1/lots", new CreateLotRequest(await Content()));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode); Assert.Contains("ACCOUNT_POLICY_PENDING", await blocked.Content.ReadAsStringAsync());
    }
}
