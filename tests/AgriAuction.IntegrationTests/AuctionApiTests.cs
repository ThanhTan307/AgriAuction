using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriAuction.Application.Auctions;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class AuctionApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<HttpClient> Login(string role = Roles.Administrator, Account? account = null, HttpClient? client = null)
    {
        client ??= fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = (account ?? fixture.Accounts[role]).Email, password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return client;
    }
    private static SimulationTerms Terms() => new() { RegistrationOpensAt = DateTimeOffset.UtcNow.AddMinutes(-1), RegistrationClosesAt = DateTimeOffset.UtcNow.AddHours(1), StartsAt = DateTimeOffset.UtcNow.AddHours(2), EndsAt = DateTimeOffset.UtcNow.AddHours(3), DepositAmount = 123456.7891m, Currency = "VND" };
    private async Task<Lot> Seed(bool approved = true)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = Category.Create("A-" + Guid.NewGuid().ToString("N")[..20], "Phase 3 test", true); db.Categories.Add(category);
        var lot = Lot.Create(fixture.Accounts[Roles.Farmer].Id, new() { CategoryId = category.Id, Title = "Test auction " + Guid.NewGuid(), Description = "Test", Location = "Test location", Quantity = 2, QuantityUnit = "box", Weight = 3, WeightUnit = "kg", ProposedStartingPrice = 500, ProposedIncrement = 1, Currency = "VND", PriceBasis = "whole lot proposal", ProposedStartsAt = DateTimeOffset.UtcNow.AddDays(1), ProposedEndsAt = DateTimeOffset.UtcNow.AddDays(2) });
        db.Lots.Add(lot); await db.SaveChangesAsync();
        if (approved)
        {
            var image = new LotImage { LotId = lot.Id, UploadedBy = lot.FarmerId, Data = LotApiTests.Photo(), Width = 24, Height = 16, Sha256 = "TEST" };
            var revision = lot.Submit(1); lot.Decide(revision.Id, "Approve", "Synthetic fixture approval");
            db.LotImages.Add(image); db.LotRevisions.Add(revision); db.RevisionImages.Add(new() { RevisionId = revision.Id, ImageId = image.Id });
            db.LotReviews.Add(new() { LotId = lot.Id, RevisionId = revision.Id, AdminId = fixture.Accounts[Roles.Administrator].Id, Outcome = "Approve", Reason = "Synthetic fixture approval" });
            await db.SaveChangesAsync();
        }
        return lot;
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, $"Expected {status}, actual {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private async Task<AuctionView> Prepare(HttpClient admin, Lot? lot = null, SimulationTerms? terms = null, bool publish = true)
    {
        lot ??= await Seed();
        var a = await Read<AuctionView>(await admin.PostAsJsonAsync("/api/v1/admin/auctions", new PrepareAuctionRequest(lot.Id, lot.Version, terms ?? Terms())), HttpStatusCode.Created);
        return publish ? await Read<AuctionView>(await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{a.Id}/publish-simulation", new PublishSimulationRequest(a.Version))) : a;
    }
    private static async Task<RegistrationView> Register(HttpClient enterprise, AuctionView auction)
        => await Read<RegistrationView>(await enterprise.PostAsJsonAsync($"/api/v1/auctions/{auction.Id}/registrations", new { }), HttpStatusCode.Created);
    private static Task<HttpResponseMessage> Confirm(HttpClient client, RegistrationView r, Guid key, string reason = "Test simulation confirmation", long? version = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/registrations/{r.Id}/confirm-simulation") { Content = JsonContent.Create(new ConfirmSimulationRequest(version ?? r.Version, reason)) };
        message.Headers.Add("Idempotency-Key", key.ToString()); return client.SendAsync(message);
    }
    [PostgresFact] public async Task GuardsReturn401And403ForAllThreeRoles()
    {
        using var anonymous = fixture.Host.CreateClient();
        foreach (var route in new[] { "/api/v1/auctions", "/api/v1/admin/auctions", "/api/v1/me/registrations", "/api/v1/admin/registrations" }) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        foreach (var role in Roles.All)
        {
            using var client = await Login(role);
            Assert.Equal(role == Roles.Farmer ? HttpStatusCode.Forbidden : HttpStatusCode.OK, (await client.GetAsync("/api/v1/auctions")).StatusCode);
            Assert.Equal(role == Roles.Administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/auctions")).StatusCode);
            Assert.Equal(role == Roles.Enterprise ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me/registrations")).StatusCode);
            Assert.Equal(role == Roles.Administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/registrations")).StatusCode);
            Assert.Equal(role == Roles.Administrator ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, (await Confirm(client, new(Guid.NewGuid(), Guid.NewGuid(), "", Guid.NewGuid(), "", "", 1, DateTimeOffset.UtcNow, null, null!, []), Guid.NewGuid())).StatusCode);
        }
    }
    [PostgresFact] public async Task PreparationRequiresApprovedCurrentLotAndValidExplicitTerms()
    {
        using var admin = await Login(); var draft = await Seed(false);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/auctions", new PrepareAuctionRequest(draft.Id, draft.Version, Terms()))).StatusCode);
        var lot = await Seed();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/auctions", new PrepareAuctionRequest(lot.Id, 1, Terms()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/auctions", new PrepareAuctionRequest(lot.Id, lot.Version, Terms() with { DepositAmount = 0 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/auctions", new PrepareAuctionRequest(lot.Id, lot.Version, Terms() with { RegistrationClosesAt = DateTimeOffset.UtcNow.AddDays(7) }))).StatusCode);
        var a = await Prepare(admin, lot, publish: false); Assert.Equal("Draft", a.State); Assert.True(a.IsSimulation); Assert.Equal(123456.7891m, a.Terms.DepositAmount);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(lot.CurrentRevisionId, (await db.Auctions.SingleAsync(x => x.Id == a.Id)).RevisionId);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityId == a.Id && x.Action == "AuctionSimulationPrepared"));
    }
    [PostgresFact] public async Task ConcurrentPreparationForSameLotCreatesOneAuction()
    {
        using var admin = await Login(); var lot = await Seed(); var body = new PrepareAuctionRequest(lot.Id, lot.Version, Terms());
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.PostAsJsonAsync("/api/v1/admin/auctions", body)));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Equal(3, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
    }
    [PostgresFact] public async Task DraftIsPrivateAndPublishIsExplicitVersionChecked()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin, publish: false);
        Assert.Equal(HttpStatusCode.NotFound, (await enterprise.GetAsync($"/api/v1/auctions/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await enterprise.GetAsync(a.Images[0].Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{a.Id}/publish-simulation", new PublishSimulationRequest(99))).StatusCode);
        a = await Read<AuctionView>(await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{a.Id}/publish-simulation", new PublishSimulationRequest(1)));
        Assert.Equal("Scheduled", a.State); Assert.True(a.RegistrationOpen);
        Assert.Equal(HttpStatusCode.OK, (await enterprise.GetAsync(a.Images[0].Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await enterprise.GetAsync($"/api/v1/auctions/{a.Id}/images/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{a.Id}/publish-simulation", new PublishSimulationRequest(2))).StatusCode);
    }
    [PostgresFact] public async Task RegistrationCreatesExactDepositHistoryAndDuplicateIs409()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin); var r = await Register(enterprise, a);
        Assert.Equal(fixture.Accounts[Roles.Enterprise].Id, r.EnterpriseId); Assert.Equal("PendingDeposit", r.State); Assert.Equal("Simulation", r.Deposit.Mode); Assert.Equal(123456.7891m, r.Deposit.Amount); Assert.Single(r.History);
        var duplicate = await enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { }); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); Assert.Contains("DUPLICATE_REGISTRATION", await duplicate.Content.ReadAsStringAsync());
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AuctionRegistrations.CountAsync(x => x.AuctionId == a.Id)); Assert.Equal(1, await db.SimulationDeposits.CountAsync(x => x.RegistrationId == r.Id));
    }
    [PostgresFact] public async Task ConcurrentDuplicateRegistrationIsConsistentAcrossRequests()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Equal(7, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.AuctionRegistrations.Where(x => x.AuctionId == a.Id).Select(x => x.Id).SingleAsync();
        Assert.Equal(1, await db.RegistrationEvents.CountAsync(x => x.RegistrationId == id)); Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityId == id));
    }
    [PostgresFact] public async Task ClientCannotChooseEnterpriseOrMutateTermsAndOtherOwnerCannotRead()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { enterpriseId = Guid.NewGuid() })).StatusCode);
        var r = await Register(enterprise, a);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var other = new Account { Email = Guid.NewGuid() + "@test.invalid", DisplayName = "Other Enterprise", Role = Roles.Enterprise }; other.NormalizedEmail = other.Email.ToUpperInvariant();
        other.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(other, fixture.Password); db.Users.Add(other); await db.SaveChangesAsync();
        using var otherClient = await Login(account: other);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/v1/me/registrations/{r.Id}")).StatusCode);
        Assert.DoesNotContain((await otherClient.GetFromJsonAsync<RegistrationPage>("/api/v1/me/registrations"))!.Items, x => x.Id == r.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(enterprise, r, Guid.NewGuid())).StatusCode);
    }
    [PostgresFact] public async Task RegistrationBeforeWindowOrAfterDeadlineIsBlocked()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var terms = Terms();
        var a = await Prepare(admin, terms: terms with { RegistrationOpensAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        Assert.Equal(HttpStatusCode.Conflict, (await enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { })).StatusCode);
        var open = await Prepare(admin); var r = await Register(enterprise, open);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE auctions SET registration_opens_at = clock_timestamp() - interval '2 hours', registration_closes_at = clock_timestamp() - interval '1 hour' WHERE id = {open.Id}");
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(admin, r, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await enterprise.PostAsJsonAsync($"/api/v1/auctions/{open.Id}/registrations", new { })).StatusCode);
    }
    [PostgresFact] public async Task ConfirmationPersistsLedgerActorAuditAndExactReplay()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin); var r = await Register(enterprise, a); var key = Guid.NewGuid();
        var result = await Read<SimulationConfirmation>(await Confirm(admin, r, key)); var replay = await Read<SimulationConfirmation>(await Confirm(admin, r, key)); Assert.Equal(result, replay);
        var seen = (await enterprise.GetFromJsonAsync<RegistrationView>($"/api/v1/me/registrations/{r.Id}"))!;
        Assert.Equal("SimulationEligible", seen.State); Assert.Equal("SimulatedHeld", seen.Deposit.State); Assert.Equal(fixture.Accounts[Roles.Administrator].Id, seen.Deposit.ConfirmedBy); Assert.Equal(2, seen.History.Count);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.SimulationLedger.CountAsync(x => x.DepositId == r.Deposit.Id)); Assert.Equal(1, await db.DepositCommands.CountAsync(x => x.RequestId == key));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityId == r.Id && x.Action == "DepositSimulationConfirmed"));
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityId == r.Id && x.Action == "DepositSimulationConfirmed");
        Assert.Contains("AwaitingSimulation", audit.Before); Assert.Contains(key.ToString(), audit.After); Assert.Contains("Test simulation confirmation", audit.After);
        var entry = await db.SimulationLedger.SingleAsync(x => x.DepositId == r.Deposit.Id); Assert.Equal(123456.7891m, entry.Amount); Assert.Equal("Simulation", entry.Provider);
    }
    [PostgresFact] public async Task ConcurrentSameKeyReplaysOneCommittedResult()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var r = await Register(enterprise, await Prepare(admin)); var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Confirm(admin, r, key)));
        var results = new List<SimulationConfirmation>(); foreach (var response in responses) results.Add(await Read<SimulationConfirmation>(response)); Assert.Single(results.Distinct());
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.SimulationLedger.CountAsync(x => x.DepositId == r.Deposit.Id)); Assert.Equal(2, await db.RegistrationEvents.CountAsync(x => x.RegistrationId == r.Id));
    }
    [PostgresFact] public async Task DifferentConcurrentKeysCannotConfirmTwice()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var r = await Register(enterprise, await Prepare(admin));
        var results = await Task.WhenAll(Confirm(admin, r, Guid.NewGuid()), Confirm(admin, r, Guid.NewGuid())); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    }
    [PostgresFact] public async Task KeyReuseWithDifferentPayloadOrTargetIsConflict()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var r = await Register(enterprise, await Prepare(admin)); var other = await Register(enterprise, await Prepare(admin)); var key = Guid.NewGuid();
        await Read<SimulationConfirmation>(await Confirm(admin, r, key));
        foreach (var response in new[] { await Confirm(admin, r, key, "Changed reason"), await Confirm(admin, other, key), await Confirm(admin, r, key, version: 2) })
        { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("IDEMPOTENCY_CONFLICT", await response.Content.ReadAsStringAsync()); }
        Assert.Equal("PendingDeposit", (await enterprise.GetFromJsonAsync<RegistrationView>($"/api/v1/me/registrations/{other.Id}"))!.State);
    }
    [PostgresFact] public async Task InvalidKeyReasonAndVersionDoNotCommitConfirmation()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var r = await Register(enterprise, await Prepare(admin));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/admin/registrations/{r.Id}/confirm-simulation", new ConfirmSimulationRequest(1, "No key"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(admin, r, Guid.Empty)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(admin, r, Guid.NewGuid(), " ")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(admin, r, Guid.NewGuid(), version: 2)).StatusCode);
        Assert.Equal("PendingDeposit", (await enterprise.GetFromJsonAsync<RegistrationView>($"/api/v1/me/registrations/{r.Id}"))!.State);
    }
    [PostgresFact] public async Task DatabaseAuditFailureRollsBackAllRowsAndSameKeyCanRetry()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var r = await Register(enterprise, await Prepare(admin)); var key = Guid.NewGuid();
        var token = "rollback_" + Guid.NewGuid().ToString("N");
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Add a uniquely named TEST-only trigger; no existing objects are altered/dropped.
        var sql = $"CREATE FUNCTION {token}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action = 'DepositSimulationConfirmed' AND NEW.after LIKE '%{token}%' THEN RAISE EXCEPTION 'Injected database audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {token} BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION {token}();";
        await db.Database.ExecuteSqlRawAsync(sql);
        var failure = await Confirm(admin, r, key, token); Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        var seen = (await enterprise.GetFromJsonAsync<RegistrationView>($"/api/v1/me/registrations/{r.Id}"))!; Assert.Equal("PendingDeposit", seen.State); Assert.Equal("AwaitingSimulation", seen.Deposit.State); Assert.Single(seen.History);
        Assert.False(await db.SimulationLedger.AnyAsync(x => x.DepositId == r.Deposit.Id)); Assert.False(await db.DepositCommands.AnyAsync(x => x.RequestId == key));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.EntityId == r.Id && x.Action == "DepositSimulationConfirmed"));
        await Read<SimulationConfirmation>(await Confirm(admin, r, key));
    }
    [PostgresFact] public async Task DatabaseConstraintsProtectUniqueRegistrationAndSimulationOnly()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin); var r = await Register(enterprise, a);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auction_registrations (id,auction_id,enterprise_id,state,version,registered_at) VALUES ({Guid.NewGuid()},{a.Id},{r.EnterpriseId},'PendingDeposit',1,clock_timestamp())")); Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var real = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE simulation_deposits SET mode = 'Real' WHERE id = {r.Deposit.Id}")); Assert.Equal(PostgresErrorCodes.CheckViolation, real.SqlState);
        var live = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE auctions SET state = 'Open' WHERE id = {a.Id}")); Assert.Equal(PostgresErrorCodes.CheckViolation, live.SqlState);
    }
    [PostgresFact] public async Task DisabledOrWrongRoleAccountsAndInactiveCategoryBlockCommands()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin); var r = await Register(enterprise, a);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Users.SingleAsync(x => x.Id == r.EnterpriseId); account.IsEnabled = false; await db.SaveChangesAsync();
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await enterprise.GetAsync("/api/v1/auctions")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(admin, r, Guid.NewGuid())).StatusCode); }
        finally { account.IsEnabled = true; await db.SaveChangesAsync(); }
        var category = await db.Categories.SingleAsync(x => x.Id == a.Content.CategoryId); category.Update(category.Code, category.Name, false); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(admin, r, Guid.NewGuid())).StatusCode);
        Assert.False((await enterprise.GetFromJsonAsync<AuctionView>($"/api/v1/auctions/{a.Id}"))!.RegistrationOpen);
    }
    [PostgresFact] public async Task DemoWritesBlockedOutsideDevelopmentAndTesting()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin); var r = await Register(enterprise, a);
        using var host = fixture.Host.WithWebHostBuilder(builder => builder.UseEnvironment("Staging"));
        using var stagedEnterprise = await Login(Roles.Enterprise, client: host.CreateClient()); using var stagedAdmin = await Login(client: host.CreateClient());
        Assert.Equal(HttpStatusCode.Conflict, (await stagedEnterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { })).StatusCode);
        var failure = await Confirm(stagedAdmin, r, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode); Assert.Contains("SIMULATION_ENVIRONMENT_REQUIRED", await failure.Content.ReadAsStringAsync());
    }
    [PostgresFact] public async Task RegistrationRechecksDatabaseTimeAfterWaitingForAuctionLock()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE auctions SET registration_closes_at = clock_timestamp() + interval '1 second' WHERE id = {a.Id}");
        var waiting = enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { });
        await Task.Delay(1500); await transaction.CommitAsync();
        var result = await waiting; Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.Contains("REGISTRATION_CLOSED", await result.Content.ReadAsStringAsync());
        Assert.False(await db.AuctionRegistrations.AnyAsync(x => x.AuctionId == a.Id));
    }
    [PostgresFact] public async Task RegistrationAuditFailureRollsBackRegistrationDepositAndHistory()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var a = await Prepare(admin);
        var token = "regfail_" + Guid.NewGuid().ToString("N");
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Identifiers are generated UUID tokens; auction ID is a typed Guid.
        var triggerSql = $"CREATE FUNCTION {token}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action = 'AuctionRegisteredSimulation' AND NEW.after LIKE '%{a.Id}%' THEN RAISE EXCEPTION 'Injected registration database failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {token} BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION {token}();";
        await db.Database.ExecuteSqlRawAsync(triggerSql);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await enterprise.PostAsJsonAsync($"/api/v1/auctions/{a.Id}/registrations", new { })).StatusCode);
        Assert.False(await db.AuctionRegistrations.AnyAsync(x => x.AuctionId == a.Id));
        Assert.False(await db.SimulationDeposits.AnyAsync(d => db.AuctionRegistrations.Any(r => r.Id == d.RegistrationId && r.AuctionId == a.Id)));
        Assert.False(await db.RegistrationEvents.AnyAsync(e => db.AuctionRegistrations.Any(r => r.Id == e.RegistrationId && r.AuctionId == a.Id)));
    }
    [PostgresFact] public async Task ConcurrentKeyReuseAcrossAuctionsCommitsOnlyOneTarget()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise);
        var first = await Register(enterprise, await Prepare(admin)); var second = await Register(enterprise, await Prepare(admin)); var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Confirm(admin, first, key), Confirm(admin, second, key));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.SimulationLedger.CountAsync(x => x.DepositId == first.Deposit.Id || x.DepositId == second.Deposit.Id));
        Assert.Equal(1, await db.DepositCommands.CountAsync(x => x.RequestId == key));
    }
    [PostgresFact] public async Task ConcurrentReadsNeverMixRegistrationDepositAndHistorySnapshots()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var r = await Register(enterprise, await Prepare(admin));
        var confirming = Confirm(admin, r, Guid.NewGuid());
        for (var n = 0; n < 8; n++)
        {
            var seen = (await enterprise.GetFromJsonAsync<RegistrationView>($"/api/v1/me/registrations/{r.Id}"))!;
            Assert.Equal(seen.State == "PendingDeposit" ? "AwaitingSimulation" : "SimulatedHeld", seen.Deposit.State);
            Assert.Equal(seen.State == "PendingDeposit" ? 1 : 2, seen.History.Count);
        }
        Assert.Equal(HttpStatusCode.OK, (await confirming).StatusCode);
    }
}
