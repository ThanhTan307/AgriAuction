using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AgriAuction.Api.Modules.Disputes;
using AgriAuction.Application.Disputes;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Realtime;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SkiaSharp;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class ComplaintApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static byte[] Image() { using var bitmap = new SKBitmap(8, 8); bitmap.Erase(SKColors.Green); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray(); }
    private async Task<HttpClient> Login(string role = Roles.Administrator, Account? account = null, HttpClient? client = null)
    {
        client ??= fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = (account ?? fixture.Accounts[role]).Email, password = fixture.Password }); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()); return client;
    }
    private async Task<Guid> Trade()
    {
        var (auctionId, _) = await new RoomApiTests(fixture).Seed(endsIn: -5);
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await db.Auctions.SingleAsync(a => a.Id == auctionId);
        var room = SimulationRoom.Configure(a.Id, fixture.Accounts[Roles.Administrator].Id, RoomApiTests.Rules(), a.Terms.StartsAt, a.Terms.EndsAt, a.Terms.StartsAt.AddMinutes(-1));
        db.SimulationRooms.Add(room); await db.SaveChangesAsync(); room.Start(fixture.Accounts[Roles.Administrator].Id, a.Terms.StartsAt);
        db.SimulationBids.Add(room.Accept(fixture.Accounts[Roles.Enterprise].Id, 100m, a.Terms.EndsAt.AddSeconds(-1))); var result = room.CloseIfDue(a.Terms.EndsAt.AddSeconds(1))!; db.SimulationResults.Add(result);
        db.PostAuctionEvents.Add(new() { RoomId = room.Id, CreatedAt = result.ClosedAt, NextAttemptAt = result.ClosedAt }); await db.SaveChangesAsync();
        await s.ServiceProvider.GetRequiredService<IPostAuctionProcessor>().ProcessAsync(default);
        return await db.PostAuctionTransactions.Where(t => t.RoomId == room.Id).Select(t => t.Id).SingleAsync();
    }
    private static HttpRequestMessage Multipart(string path, Guid key, Dictionary<string, string> fields, byte[]? bytes = null, string filename = "evidence.png", string mime = "image/png", int count = 1)
    {
        var form = new MultipartFormDataContent(); foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        for (var n = 0; n < count; n++) { var data = new ByteArrayContent(bytes ?? Image()); data.Headers.ContentType = new(mime); form.Add(data, "files", filename); }
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form }; request.Headers.Add("Idempotency-Key", key.ToString()); return request;
    }
    private static Dictionary<string, string> CreateFields(Guid trade, string scope = "TransactionRecord") => new() { ["transactionId"] = trade.ToString(), ["scope"] = scope, ["reason"] = "Kiểm tra chất lượng Simulation", ["description"] = "Cần hai bên giải trình; chưa xác nhận giao hàng hoặc thanh toán." };
    private static Dictionary<string, string> ReplyFields(long version) => new() { ["expectedVersion"] = version.ToString(), ["text"] = "Giải trình <script>window.bad=true</script> & bằng chứng" };
    private async Task<ComplaintReceipt> Create(HttpClient client, Guid trade, Guid? key = null, string scope = "TransactionRecord")
    {
        using var request = Multipart("/api/v1/complaints", key ?? Guid.NewGuid(), CreateFields(trade, scope)); var r = await client.SendAsync(request);
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<ComplaintReceipt>())!;
    }
    private static async Task<HttpResponseMessage> Command(HttpClient client, Guid id, string op, object body, Guid? key = null)
    { using var r = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/complaints/{id}/{op}") { Content = JsonContent.Create(body) }; r.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); return await client.SendAsync(r); }
    private async Task Dispatch()
    { await using var s = fixture.Host.Services.CreateAsyncScope(); await s.ServiceProvider.GetRequiredService<IComplaintOutbox>().DispatchAsync(default); }
    [PostgresFact] public async Task EndToEndBothPartiesAdminDecisionPrivateEvidenceAndInbox()
    {
        var trade = await Trade(); using var farmer = await Login(Roles.Farmer); using var enterprise = await Login(Roles.Enterprise); using var admin = await Login();
        var receipt = await Create(farmer, trade, scope: "AuctionRecord"); var id = receipt.ComplaintId;
        foreach (var client in new[] { enterprise, farmer })
        { using var reply = Multipart($"/api/v1/complaints/{id}/responses", Guid.NewGuid(), ReplyFields(receipt.Version)); var r = await client.SendAsync(reply); Assert.Equal(HttpStatusCode.OK, r.StatusCode); receipt = (await r.Content.ReadFromJsonAsync<ComplaintReceipt>())!; }
        foreach (var op in new[] { "review", "comments" })
        { var r = await Command(admin, id, op, new { expectedVersion = receipt.Version, text = "Đã đối chiếu bằng chứng hai bên, chỉ Simulation." }); Assert.Equal(HttpStatusCode.OK, r.StatusCode); receipt = (await r.Content.ReadFromJsonAsync<ComplaintReceipt>())!; }
        var decision = await Command(admin, id, "decision", new { expectedVersion = receipt.Version, outcome = "Accept", text = "Ghi nhận chấp nhận nội dung Simulation, không thực thi chế tài.", confirmedSimulation = true }); Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        var detail = (await enterprise.GetFromJsonAsync<ComplaintDetail>("/api/v1/complaints/" + id))!; Assert.Equal("DecisionRecorded", detail.Complaint.State); Assert.Equal(6, detail.Entries.TotalCount); Assert.Equal("AuctionRecord", detail.Complaint.Scope);
        Assert.Equal("AwaitingPolicy", detail.Transaction.Transaction.State); Assert.Equal("PolicyLocked", detail.Transaction.Transaction.PaymentState); Assert.Equal("PolicyLocked", detail.Transaction.Transaction.DeliveryState);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, detail.Entries.Items.Select(e => e.Version));
        var evidence = detail.Entries.Items[0].Evidence.Single(); var image = await enterprise.GetAsync(evidence.Url); Assert.Equal(HttpStatusCode.OK, image.StatusCode); Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType); Assert.Contains("no-store", image.Headers.CacheControl!.ToString()); Assert.Equal(evidence.Sha256, Convert.ToHexString(SHA256.HashData(await image.Content.ReadAsByteArrayAsync())));
        await Task.WhenAll(Dispatch(), Dispatch(), Dispatch()); await Dispatch();
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); var entries = await db.ComplaintEntries.Where(e => e.ComplaintId == id).ToListAsync();
        Assert.All(entries, e => Assert.NotNull(e.NotifiedAt)); Assert.Equal(entries.Sum(e => e.RecipientIds.Length), await db.Notifications.CountAsync(n => n.ComplaintId == id));
        Assert.Equal(6, await db.AuditLogs.CountAsync(a => a.EntityId == id)); Assert.Equal(3, await db.ComplaintEvidence.CountAsync(e => e.ComplaintId == id));
        var inbox = await enterprise.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications?pageSize=100"); Assert.Contains(inbox.GetProperty("items").EnumerateArray(), n => n.GetProperty("complaintId").ValueKind == JsonValueKind.String && n.GetProperty("complaintId").GetGuid() == id && n.GetProperty("kind").GetString() == "ComplaintDecisionRecorded");
    }
    [PostgresFact] public async Task ConcurrentCreateRetryReturnsOneReceiptAndPayloadCannotChange()
    {
        using var farmer = await Login(Roles.Farmer); var trade = await Trade(); var key = Guid.NewGuid();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Create(farmer, trade, key))); Assert.Single(receipts.Distinct());
        var fields = CreateFields(trade); fields["description"] = "Changed"; using var changed = Multipart("/api/v1/complaints", key, fields); Assert.Equal(HttpStatusCode.Conflict, (await farmer.SendAsync(changed)).StatusCode);
        using var other = await Login(Roles.Enterprise); using var duplicate = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade)); Assert.Equal(HttpStatusCode.Conflict, (await other.SendAsync(duplicate)).StatusCode);
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); var id = receipts[0].ComplaintId;
        Assert.Equal(1, await db.Complaints.CountAsync(c => c.TransactionId == trade)); Assert.Equal(1, await db.ComplaintCommands.CountAsync(c => c.ComplaintId == id)); Assert.Equal(1, await db.ComplaintEvidence.CountAsync(e => e.ComplaintId == id)); Assert.False(await db.Notifications.AnyAsync(n => n.ComplaintId == id));
    }
    [PostgresFact] public async Task RbacOwnershipUnknownFieldsAndNoPublicEvidence()
    {
        using var farmer = await Login(Roles.Farmer); using var outsider = await Login(account: fixture.OtherFarmer); using var admin = await Login(); using var anonymous = fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        var trade = await Trade(); var c = await Create(farmer, trade); var detail = (await farmer.GetFromJsonAsync<ComplaintDetail>("/api/v1/complaints/" + c.ComplaintId))!; var url = detail.Entries.Items[0].Evidence[0].Url;
        foreach (var path in new[] { "/api/v1/complaints", "/api/v1/complaints/" + c.ComplaintId, url }) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/v1/complaints/" + c.ComplaintId, $"/api/v1/complaints/{c.ComplaintId}/entries", url }) Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
        Assert.DoesNotContain((await outsider.GetFromJsonAsync<ComplaintPage>("/api/v1/complaints"))!.Items, x => x.Id == c.ComplaintId);
        using var createOther = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade)); Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(createOther)).StatusCode);
        using var adminCreate = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade)); Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(adminCreate)).StatusCode);
        using var otherReply = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", Guid.NewGuid(), ReplyFields(1)); Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(otherReply)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Command(farmer, c.ComplaintId, "decision", new { expectedVersion = 1, outcome = "Accept", text = "x", confirmedSimulation = true })).StatusCode);
        var fields = CreateFields(trade); fields["respondentId"] = fixture.OtherFarmer.Id.ToString(); using var mass = Multipart("/api/v1/complaints", Guid.NewGuid(), fields); Assert.Equal(HttpStatusCode.BadRequest, (await farmer.SendAsync(mass)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Command(admin, c.ComplaintId, "decision", new { expectedVersion = 1, outcome = "Accept", text = "x", confirmedSimulation = true, refund = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/assets/evidence/" + detail.Entries.Items[0].Evidence[0].ServerFileName)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await farmer.DeleteAsync("/api/v1/complaints/" + c.ComplaintId)).StatusCode);
    }
    [PostgresFact] public async Task UploadRejectsSpoofedCorruptUnsupportedOversizedMissingAndTooManyFiles()
    {
        using var farmer = await Login(Roles.Farmer); var trade = await Trade();
        var cases = new[] { (Image(), "a.svg", "image/png", 1), (Image(), "a.jpg", "image/jpeg", 1), (Image(), "a.png", "text/plain", 1), (new byte[] { 1, 2, 3 }, "a.png", "image/png", 1), (new byte[5242881], "a.png", "image/png", 1), (Image(), "a.png", "image/png", 0), (Image(), "a.png", "image/png", 4) };
        foreach (var (bytes, name, mime, count) in cases) { using var r = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade), bytes, name, mime, count); Assert.Equal(HttpStatusCode.BadRequest, (await farmer.SendAsync(r)).StatusCode); }
        await using var s = fixture.Host.Services.CreateAsyncScope(); Assert.False(await s.ServiceProvider.GetRequiredService<AppDbContext>().Complaints.AnyAsync(c => c.TransactionId == trade));
    }
    [PostgresFact] public async Task ConcurrentResponsesAndAdminUpdatesCannotOverwriteHistory()
    {
        using var farmer = await Login(Roles.Farmer); using var enterprise = await Login(Roles.Enterprise); using var admin = await Login(); var c = await Create(farmer, await Trade());
        async Task<HttpResponseMessage> Reply(HttpClient client) { using var r = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", Guid.NewGuid(), ReplyFields(1)); return await client.SendAsync(r); }
        var concurrent = await Task.WhenAll(Reply(farmer), Reply(enterprise), Command(admin, c.ComplaintId, "review", new { expectedVersion = 1, text = "Xem xét" }));
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK); Assert.Equal(2, concurrent.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var detail = (await admin.GetFromJsonAsync<ComplaintDetail>("/api/v1/complaints/" + c.ComplaintId))!; Assert.Equal(2, detail.Entries.TotalCount); Assert.Equal(2, detail.Complaint.Version);
        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Command(admin, c.ComplaintId, "comments", new { expectedVersion = 2, text = "Nhận xét" })));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK); Assert.Equal(3, replies.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }
    [PostgresFact] public async Task ExplicitDecisionReplayIsStableAfterClosureAndMoneyStaysLocked()
    {
        using var enterprise = await Login(Roles.Enterprise); using var admin = await Login(); var trade = await Trade(); var createKey = Guid.NewGuid(); var c = await Create(enterprise, trade, createKey);
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, c.ComplaintId, "decision", new { expectedVersion = 1, outcome = "Reject", text = "Xem xét", confirmedSimulation = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, c.ComplaintId, "review", new { expectedVersion = 1, text = "Bắt đầu xem xét" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Command(admin, c.ComplaintId, "decision", new { expectedVersion = 2, outcome = "Reject", text = "Không chấp nhận", confirmedSimulation = false })).StatusCode);
        var key = Guid.NewGuid(); var body = new { expectedVersion = 2, outcome = "Reject", text = "Không chấp nhận Simulation", confirmedSimulation = true };
        var decisions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Command(admin, c.ComplaintId, "decision", body, key))); Assert.All(decisions, d => Assert.Equal(HttpStatusCode.OK, d.StatusCode)); Assert.Single(await Task.WhenAll(decisions.Select(d => d.Content.ReadAsStringAsync())).ContinueWith(t => t.Result.Distinct()));
        Assert.Equal(c, await Create(enterprise, trade, createKey));
        using var reply = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", Guid.NewGuid(), ReplyFields(3)); Assert.Equal(HttpStatusCode.Conflict, (await enterprise.SendAsync(reply)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, c.ComplaintId, "comments", new { expectedVersion = 3, text = "Không sửa" })).StatusCode);
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Single(await db.ComplaintEntries.Where(e => e.ComplaintId == c.ComplaintId && e.Kind == "DecisionRecorded").ToListAsync()); Assert.Equal("PolicyLocked", (await db.PostAuctionTransactions.SingleAsync(t => t.Id == trade)).PaymentState);
    }
    [PostgresFact] public async Task MediationRemainsPendingAndDoesNotBlockPartyExplanation()
    {
        using var farmer = await Login(Roles.Farmer); using var admin = await Login(); var c = await Create(farmer, await Trade());
        await Command(admin, c.ComplaintId, "review", new { expectedVersion = 1, text = "Xem xét" });
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, c.ComplaintId, "decision", new { expectedVersion = 2, text = "Đề nghị hòa giải Simulation; quy trình chính thức chưa chốt", outcome = "Mediate", confirmedSimulation = true })).StatusCode);
        using var reply = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", Guid.NewGuid(), ReplyFields(3)); Assert.Equal(HttpStatusCode.OK, (await farmer.SendAsync(reply)).StatusCode);
        var detail = (await admin.GetFromJsonAsync<ComplaintDetail>("/api/v1/complaints/" + c.ComplaintId))!; Assert.Equal("MediationPending", detail.Complaint.State); Assert.Equal("Mediate", detail.Complaint.Outcome); Assert.Null(detail.Entries.Items.Last().Outcome);
    }
    [PostgresFact] public async Task AuditFaultRollsBackCaseEvidenceCommandAndOutboxThenSameKeyRetries()
    {
        using var farmer = await Login(Roles.Farmer); var trade = await Trade(); var key = Guid.NewGuid();
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); var token = "casefault_" + Guid.NewGuid().ToString("N");
        var sql = $"CREATE TABLE {token}(enabled boolean NOT NULL); INSERT INTO {token} VALUES(true); CREATE FUNCTION {token}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action = 'ComplaintCreated' AND (SELECT enabled FROM {token}) THEN RAISE EXCEPTION 'Injected complaint audit fault'; END IF; RETURN NEW; END $$; CREATE TRIGGER {token} BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION {token}();"; await db.Database.ExecuteSqlRawAsync(sql);
        using var r = Multipart("/api/v1/complaints", key, CreateFields(trade)); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await farmer.SendAsync(r)).StatusCode);
        Assert.False(await db.Complaints.AnyAsync(c => c.TransactionId == trade)); Assert.False(await db.ComplaintCommands.AnyAsync(c => c.RequestId == key));
        var reset = $"UPDATE {token} SET enabled = false"; await db.Database.ExecuteSqlRawAsync(reset); var receipt = await Create(farmer, trade, key);
        Assert.Equal(1, await db.ComplaintEntries.CountAsync(e => e.ComplaintId == receipt.ComplaintId)); Assert.Equal(1, await db.ComplaintEvidence.CountAsync(e => e.ComplaintId == receipt.ComplaintId));
    }
    [PostgresFact] public async Task NotificationFaultRollsBackBatchAndFreshWorkerRetriesWithoutDuplicates()
    {
        using var farmer = await Login(Roles.Farmer); var c = await Create(farmer, await Trade());
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); var token = "notefault_" + Guid.NewGuid().ToString("N");
        var sql = $"CREATE TABLE {token}(enabled boolean NOT NULL); INSERT INTO {token} VALUES(true); CREATE FUNCTION {token}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.complaint_id = '{c.ComplaintId}' AND (SELECT enabled FROM {token}) THEN RAISE EXCEPTION 'Injected complaint notification fault'; END IF; RETURN NEW; END $$; CREATE TRIGGER {token} BEFORE INSERT ON notifications FOR EACH ROW EXECUTE FUNCTION {token}();"; await db.Database.ExecuteSqlRawAsync(sql);
        await Dispatch(); Assert.False(await db.Notifications.AnyAsync(n => n.ComplaintId == c.ComplaintId)); var entry = await db.ComplaintEntries.AsNoTracking().SingleAsync(e => e.Id == c.EntryId); Assert.Null(entry.NotifiedAt); Assert.True(entry.Attempts > 0);
        var reset = $"UPDATE {token} SET enabled = false"; await db.Database.ExecuteSqlRawAsync(reset); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE complaint_entries SET next_attempt_at = clock_timestamp() WHERE id = {c.EntryId}");
        using var restarted = fixture.Host.WithWebHostBuilder(b => b.UseEnvironment("Testing")); using var worker = new ComplaintWorker(restarted.Services.GetRequiredService<IServiceScopeFactory>(), restarted.Services.GetRequiredService<IHostEnvironment>(), NullLogger<ComplaintWorker>.Instance); await worker.RunOnceAsync(default); await Task.WhenAll(Dispatch(), Dispatch());
        Assert.NotNull((await db.ComplaintEntries.AsNoTracking().SingleAsync(e => e.Id == c.EntryId)).NotifiedAt); Assert.Equal(entry.RecipientIds.Length, await db.Notifications.CountAsync(n => n.ComplaintId == c.ComplaintId));
    }
    [PostgresFact] public async Task FiltersPaginationAndDatabaseChecksRemainEnforced()
    {
        using var farmer = await Login(Roles.Farmer); using var admin = await Login(); var c = await Create(farmer, await Trade(), scope: "AuctionRecord");
        var list = (await admin.GetFromJsonAsync<ComplaintPage>("/api/v1/complaints?state=Open&scope=AuctionRecord&query=Room&pageSize=100"))!; Assert.All(list.Items, c => { Assert.Equal("Open", c.State); Assert.Equal("AuctionRecord", c.Scope); });
        foreach (var path in new[] { "/api/v1/complaints?page=0", "/api/v1/complaints?state=Paid", "/api/v1/complaints?scope=Private", $"/api/v1/complaints/{c.ComplaintId}/entries?pageSize=101" }) Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(path)).StatusCode);
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE complaints SET state = 'DecisionRecorded', outcome = NULL WHERE id = {c.ComplaintId}")); Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE complaint_evidence SET entry_id = {Guid.NewGuid()} WHERE complaint_id = {c.ComplaintId}")); Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
    }
    [PostgresFact] public async Task ProductionBlocksAllSimulationCommandsAndWorkerEvenIfFlagTrue()
    {
        var trade = await Trade(); using var farmer = await Login(Roles.Farmer); var c = await Create(farmer, trade);
        using var prod = fixture.Host.WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, conf) => conf.AddInMemoryCollection(new Dictionary<string, string?> { ["Simulation:ComplaintWorkerEnabled"] = "true" })); });
        using var owner = await Login(Roles.Farmer, client: prod.CreateClient()); using var admin = await Login(client: prod.CreateClient());
        foreach (var path in new[] { "/api/v1/complaints", "/api/v1/complaints/" + c.ComplaintId, $"/api/v1/complaints/{c.ComplaintId}/entries", "/api/v1/complaints/evidence/" + Guid.NewGuid() }) Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync(path)).StatusCode);
        using var create = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade)); Assert.Equal(HttpStatusCode.Conflict, (await owner.SendAsync(create)).StatusCode);
        using var reply = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", Guid.NewGuid(), ReplyFields(1)); Assert.Equal(HttpStatusCode.Conflict, (await owner.SendAsync(reply)).StatusCode);
        foreach (var op in new[] { "review", "comments" }) Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, c.ComplaintId, op, new { expectedVersion = 1, text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, c.ComplaintId, "decision", new { expectedVersion = 1, text = "x", outcome = "Accept", confirmedSimulation = true })).StatusCode);
        Assert.DoesNotContain(prod.Services.GetServices<IHostedService>(), s => s is ComplaintWorker);
        using var worker = new ComplaintWorker(prod.Services.GetRequiredService<IServiceScopeFactory>(), prod.Services.GetRequiredService<IHostEnvironment>(), NullLogger<ComplaintWorker>.Instance); await worker.RunOnceAsync(default);
        await using var s = fixture.Host.Services.CreateAsyncScope(); Assert.Null((await s.ServiceProvider.GetRequiredService<AppDbContext>().ComplaintEntries.SingleAsync(e => e.Id == c.EntryId)).NotifiedAt);
    }
    [PostgresFact] public async Task ResponseReplayKeepsEntryEvidenceAndRejectsChangedPayloadOrOperation()
    {
        using var farmer = await Login(Roles.Farmer); var c = await Create(farmer, await Trade()); var key = Guid.NewGuid();
        async Task<ComplaintReceipt> Reply()
        { using var r = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", key, ReplyFields(1)); var response = await farmer.SendAsync(r); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ComplaintReceipt>())!; }
        var first = await Reply(); Assert.Equal(first, await Reply());
        using var changed = Multipart($"/api/v1/complaints/{c.ComplaintId}/responses", key, ReplyFields(2)); Assert.Equal(HttpStatusCode.Conflict, (await farmer.SendAsync(changed)).StatusCode);
        using var cross = Multipart("/api/v1/complaints", key, CreateFields(await Trade())); Assert.Equal(HttpStatusCode.Conflict, (await farmer.SendAsync(cross)).StatusCode);
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(2, await db.ComplaintEvidence.CountAsync(e => e.ComplaintId == c.ComplaintId)); Assert.Equal(2, await db.ComplaintEntries.CountAsync(e => e.ComplaintId == c.ComplaintId));
    }
    [PostgresFact] public async Task TwoPartiesConcurrentCreateShareOneActiveCase()
    {
        using var farmer = await Login(Roles.Farmer); using var enterprise = await Login(Roles.Enterprise); var trade = await Trade();
        async Task<HttpResponseMessage> Send(HttpClient client) { using var r = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade)); return await client.SendAsync(r); }
        var responses = await Task.WhenAll(Send(farmer), Send(enterprise)); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var s = fixture.Host.Services.CreateAsyncScope(); Assert.Equal(1, await s.ServiceProvider.GetRequiredService<AppDbContext>().Complaints.CountAsync(c => c.TransactionId == trade));
    }
    [PostgresFact] public async Task UncommittedHistoryCannotPublishNotificationAndRollbackLeavesNoEntry()
    {
        using var farmer = await Login(Roles.Farmer); var c = await Create(farmer, await Trade()); await Dispatch();
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>(); var eventId = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var now = DateTimeOffset.UtcNow; db.ComplaintEntries.Add(new() { Id = eventId, ComplaintId = c.ComplaintId, ActorId = fixture.Accounts[Roles.Farmer].Id, ActorRole = Roles.Farmer, Kind = "ResponseAdded", Text = "Uncommitted synthetic entry", State = "Open", Version = 2, OccurredAt = now, NextAttemptAt = now, RecipientIds = [fixture.Accounts[Roles.Enterprise].Id] }); await db.SaveChangesAsync();
            await Dispatch(); Assert.False(await db.Notifications.AnyAsync(n => n.EventId == eventId)); await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear(); Assert.False(await db.ComplaintEntries.AnyAsync(e => e.Id == eventId)); Assert.Equal(1, await db.ComplaintEntries.CountAsync(e => e.ComplaintId == c.ComplaintId));
    }
    [PostgresFact] public async Task CookieCreateRequiresCsrfAndDisabledAccountCannotReadEvidence()
    {
        var trade = await Trade(); using var cookie = fixture.Host.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await cookie.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); cookie.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await cookie.PostAsJsonAsync("/api/v1/auth/login?useCookies=true", new { email = fixture.Accounts[Roles.Farmer].Email, password = fixture.Password })).StatusCode); cookie.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var bad = Multipart("/api/v1/complaints", Guid.NewGuid(), CreateFields(trade)); Assert.Equal(HttpStatusCode.BadRequest, (await cookie.SendAsync(bad)).StatusCode);
        csrf = await cookie.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); cookie.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString()); var c = await Create(cookie, trade);
        using var enterprise = await Login(Roles.Enterprise); var detail = (await enterprise.GetFromJsonAsync<ComplaintDetail>("/api/v1/complaints/" + c.ComplaintId))!; var actor = fixture.Accounts[Roles.Enterprise].Id;
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_enabled = false WHERE id = {actor}");
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await enterprise.GetAsync(detail.Entries.Items[0].Evidence[0].Url)).StatusCode); }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_enabled = true WHERE id = {actor}"); }
    }
}
