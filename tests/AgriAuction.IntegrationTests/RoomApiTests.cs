using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AgriAuction.Api.Modules.Realtime;
using AgriAuction.Application.Realtime;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;
using AgriAuction.Infrastructure.Persistence;
using AgriAuction.Infrastructure.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class RoomApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    public static SimulationBidRules Rules() => new() { StartingPrice = 100, MinimumIncrement = 10, Currency = "VND", PriceBasis = "whole lot simulation", FirstBidRule = "AtLeastStartingPrice", IncrementRule = "MinimumIncrease", TieRule = "FirstCommittedWins", ExtensionRule = "Disabled", ExtensionWindowSeconds = 0, ExtensionSeconds = 0, MaxExtensions = 0, CloseRule = "AtOrAfterDeadline" };
    private async Task<HttpClient> Login(string role = Roles.Administrator, Account? account = null, HttpClient? client = null)
    {
        client ??= fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = (account ?? fixture.Accounts[role]).Email, password = fixture.Password }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()); return client;
    }
    internal async Task<(Guid AuctionId, Account Other)> Seed(double endsIn = 60, bool eligible = true)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var other = new Account { Email = Guid.NewGuid() + "@test.invalid", Role = Roles.Enterprise, DisplayName = "Other Enterprise" }; other.NormalizedEmail = other.Email.ToUpperInvariant(); other.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(other, fixture.Password); db.Users.Add(other);
        var cat = Category.Create("R-" + Guid.NewGuid().ToString("N")[..20], "Room test", true); db.Categories.Add(cat);
        var now = DateTimeOffset.UtcNow;
        var lot = Lot.Create(fixture.Accounts[Roles.Farmer].Id, new() { CategoryId = cat.Id, Title = "Room test " + Guid.NewGuid(), Description = "Synthetic historical fixture", Location = "Test", Quantity = 1, QuantityUnit = "box", Weight = 1, WeightUnit = "kg", ProposedStartingPrice = 1, ProposedIncrement = 1, Currency = "VND", PriceBasis = "proposal", ProposedStartsAt = now.AddDays(1), ProposedEndsAt = now.AddDays(2) });
        db.Lots.Add(lot); await db.SaveChangesAsync();
        var image = new LotImage { LotId = lot.Id, UploadedBy = lot.FarmerId, Data = LotApiTests.Photo(), Width = 24, Height = 16, Sha256 = "TEST" };
        var revision = lot.Submit(1); lot.Decide(revision.Id, "Approve", "Synthetic fixture approval");
        db.LotImages.Add(image); db.LotRevisions.Add(revision); db.RevisionImages.Add(new() { RevisionId = revision.Id, ImageId = image.Id }); db.LotReviews.Add(new() { LotId = lot.Id, RevisionId = revision.Id, AdminId = fixture.Accounts[Roles.Administrator].Id, Outcome = "Approve", Reason = "Synthetic fixture approval" }); await db.SaveChangesAsync();
        // Historical fixture clock: registration/confirmation happened before
        // startsAt. No public API edits or shortens an existing Phase 3 schedule.
        now = DateTimeOffset.UtcNow;
        var terms = new SimulationTerms { RegistrationOpensAt = now.AddHours(-4), RegistrationClosesAt = now.AddHours(-2), StartsAt = now.AddHours(-1), EndsAt = now.AddSeconds(endsIn), DepositAmount = 100, Currency = "VND" };
        var a = Auction.Prepare(lot, fixture.Accounts[Roles.Administrator].Id, terms, now.AddHours(-3)); a.Publish(fixture.Accounts[Roles.Administrator].Id, now.AddHours(-3)); db.Auctions.Add(a);
        if (eligible) foreach (var actor in new[] { fixture.Accounts[Roles.Enterprise].Id, other.Id })
        {
            var registration = AuctionRegistration.Register(a, actor, now.AddHours(-3)); registration.ConfirmSimulation(1, now.AddHours(-3)); db.AuctionRegistrations.Add(registration);
            var deposit = new SimulationDeposit { RegistrationId = registration.Id, State = "SimulatedHeld", Amount = terms.DepositAmount, Currency = terms.Currency, CreatedAt = now.AddHours(-3), ConfirmedAt = now.AddHours(-3), ConfirmedBy = fixture.Accounts[Roles.Administrator].Id }; db.SimulationDeposits.Add(deposit);
            db.SimulationLedger.Add(new() { DepositId = deposit.Id, ActorId = fixture.Accounts[Roles.Administrator].Id, Amount = deposit.Amount, Currency = deposit.Currency, Reference = "SIM-" + deposit.Id.ToString("N"), OccurredAt = now.AddHours(-3) });
        }
        await db.SaveChangesAsync(); return (a.Id, other);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode code = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == code, $"Expected {code}, actual {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<HttpResponseMessage> Bid(HttpClient client, Guid id, decimal amount, Guid? key = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auctions/{id}/room/bids") { Content = JsonContent.Create(new PlaceBidRequest(amount)) }; message.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); return client.SendAsync(message);
    }
    private static async Task<RoomView> Configure(HttpClient admin, Guid id, SimulationBidRules? rules = null)
        => await Read<RoomView>(await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room", new ConfigureRoomRequest(2, rules ?? Rules())), HttpStatusCode.Created);
    private static async Task<RoomView> Start(HttpClient admin, Guid id)
        => await Read<RoomView>(await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room/start", new StartRoomRequest(1)));
    private async Task<bool> Close(Guid id)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IRoomService>().CloseDueAsync(id, default);
    }
    [PostgresFact] public async Task RoleOwnershipEligibilityAndEnvironmentAreChecked()
    {
        var (id, other) = await Seed(eligible: false); using var admin = await Login(); await Configure(admin, id);
        using var anonymous = fixture.Host.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/auctions/{id}/room")).StatusCode);
        using var farmer = await Login(Roles.Farmer); using var enterprise = await Login(Roles.Enterprise); using var stranger = await Login(account: fixture.OtherFarmer);
        Assert.Equal(HttpStatusCode.OK, (await farmer.GetAsync($"/api/v1/auctions/{id}/room")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/v1/auctions/{id}/room")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await enterprise.GetAsync($"/api/v1/auctions/{id}/room")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Bid(farmer, id, 100)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Bid(enterprise, id, 100)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await enterprise.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room/start", new StartRoomRequest(1))).StatusCode);
        using var host = fixture.Host.WithWebHostBuilder(builder => builder.UseEnvironment("Staging")); using var staged = await Login(client: host.CreateClient());
        Assert.Equal(HttpStatusCode.Conflict, (await staged.GetAsync($"/api/v1/auctions/{id}/room")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await staged.PostAsync("/hubs/simulation/negotiate?negotiateVersion=1", null)).StatusCode);
    }
    [PostgresFact] public async Task ConfigurationStartAndUnknownFieldsHaveVersionAndStateGuards()
    {
        var (id, _) = await Seed(); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room", new ConfigureRoomRequest(2, new()))).StatusCode);
        var prepared = await Configure(admin, id); Assert.Equal("Prepared", prepared.State);
        Assert.Equal(HttpStatusCode.Conflict, (await Bid(enterprise, id, 100)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room/start", new StartRoomRequest(99))).StatusCode);
        await Start(admin, id); Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room/start", new StartRoomRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room", new ConfigureRoomRequest(2, Rules()))).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auctions/{id}/room/bids") { Content = JsonContent.Create(new { amount = 100, enterpriseId = Guid.NewGuid() }) }; request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.BadRequest, (await enterprise.SendAsync(request)).StatusCode);
    }
    [PostgresFact] public async Task TwoEnterprisesConcurrentTieHasOneLeaderPerVersion()
    {
        var (id, other) = await Seed(); using var admin = await Login(); using var a = await Login(Roles.Enterprise); using var b = await Login(account: other); var room = await Configure(admin, id); await Start(admin, id);
        var responses = await Task.WhenAll(Bid(a, id, 100), Bid(b, id, 100)); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await Read<BidReceipt>(await Bid(responses[0].IsSuccessStatusCode ? b : a, id, 110), HttpStatusCode.Created);
        var snapshot = await a.GetFromJsonAsync<RoomView>($"/api/v1/auctions/{id}/room"); Assert.Equal(2, snapshot!.BidCount); Assert.Equal(4, snapshot.Version); Assert.Equal(110, snapshot.CurrentPrice);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var bids = await db.SimulationBids.Where(x => x.RoomId == room.RoomId).OrderBy(x => x.RoomVersion).ToListAsync(); Assert.Equal(new long[] { 3, 4 }, bids.Select(x => x.RoomVersion)); Assert.Equal(new decimal[] { 100, 110 }, bids.Select(x => x.Amount)); Assert.Equal(2, bids.Select(x => x.EnterpriseId).Distinct().Count());
    }
    [PostgresFact] public async Task ManyConcurrentRequestsAndSameKeyReplayCannotDuplicateBid()
    {
        var (id, _) = await Seed(); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var room = await Configure(admin, id); await Start(admin, id); var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Bid(enterprise, id, 100, key))); var receipts = new List<BidReceipt>(); foreach (var r in responses) receipts.Add(await Read<BidReceipt>(r, HttpStatusCode.Created)); Assert.Single(receipts.Distinct());
        Assert.Equal(HttpStatusCode.Conflict, (await Bid(enterprise, id, 110, key)).StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(1, await db.SimulationBids.CountAsync(x => x.RoomId == room.RoomId)); Assert.Equal(1, await db.SimulationBidCommands.CountAsync(x => x.RoomId == room.RoomId)); Assert.Equal(1, await db.SimulationRoomEvents.CountAsync(x => x.RoomId == room.RoomId && x.Kind == "BidAccepted"));
    }
    [PostgresFact] public async Task ExtensionOnlyOnValidBidWithinConfiguredWindowAndNeverByTimer()
    {
        var (id, _) = await Seed(endsIn: 30); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise);
        var prepared = await Configure(admin, id, Rules() with { ExtensionRule = "AddToDeadline", ExtensionWindowSeconds = 60, ExtensionSeconds = 10, MaxExtensions = 1 }); await Start(admin, id);
        Assert.Equal(HttpStatusCode.Conflict, (await Bid(enterprise, id, 1)).StatusCode);
        var before = (await enterprise.GetFromJsonAsync<RoomView>($"/api/v1/auctions/{id}/room"))!; Assert.Equal(prepared.EndsAt, before.EndsAt);
        var receipt = await Read<BidReceipt>(await Bid(enterprise, id, 100), HttpStatusCode.Created); Assert.Equal(prepared.EndsAt.AddSeconds(10), receipt.EndsAt);
        var second = await Read<BidReceipt>(await Bid(enterprise, id, 110), HttpStatusCode.Created); Assert.Equal(receipt.EndsAt, second.EndsAt); Assert.False(await Close(id));
    }
    [PostgresFact] public async Task DatabaseAuditFailureRollsBackBidLeaderCommandAndOutbox()
    {
        var (id, _) = await Seed(); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var room = await Configure(admin, id); await Start(admin, id);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var token = "bidfault_" + Guid.NewGuid().ToString("N");
        var sql = $"CREATE FUNCTION {token}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action = 'BidAccepted' AND NEW.entity_id = '{room.RoomId}'::uuid THEN RAISE EXCEPTION 'Injected bid audit database failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {token} BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION {token}();"; await db.Database.ExecuteSqlRawAsync(sql);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Bid(enterprise, id, 100)).StatusCode); var after = (await enterprise.GetFromJsonAsync<RoomView>($"/api/v1/auctions/{id}/room"))!;
        Assert.Equal(2, after.Version); Assert.Null(after.CurrentPrice); Assert.False(await db.SimulationBids.AnyAsync(x => x.RoomId == room.RoomId)); Assert.False(await db.SimulationBidCommands.AnyAsync(x => x.RoomId == room.RoomId)); Assert.False(await db.SimulationRoomEvents.AnyAsync(x => x.RoomId == room.RoomId && x.Kind == "BidAccepted"));
    }
    [PostgresFact] public async Task DeadlineRaceUsesSameLockAndRepeatedCloseHasOneResult()
    {
        var (id, _) = await Seed(endsIn: 3); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var prepared = await Configure(admin, id); await Start(admin, id); await Read<BidReceipt>(await Bid(enterprise, id, 100), HttpStatusCode.Created);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM auctions WHERE id = {id} FOR UPDATE");
        var late = Bid(enterprise, id, 110); var closing = Close(id);
        var delay = prepared.EndsAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(150); if (delay > TimeSpan.Zero) await Task.Delay(delay); await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await late).StatusCode); Assert.True(await closing); Assert.False(await Close(id));
        Assert.Equal(1, await db.SimulationResults.CountAsync(x => x.RoomId == prepared.RoomId)); var result = await db.SimulationResults.SingleAsync(x => x.RoomId == prepared.RoomId); Assert.Equal(100, result.Amount); Assert.Equal(1, await db.SimulationBids.CountAsync(x => x.RoomId == prepared.RoomId));
    }
    [PostgresFact] public async Task FreshHostWorkerRecoversDueRoomAndMultipleWorkersCannotDuplicateResult()
    {
        var (id, _) = await Seed(endsIn: 10); using var admin = await Login(); var room = await Configure(admin, id); await Start(admin, id);
        var delay = room.EndsAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100); if (delay > TimeSpan.Zero) await Task.Delay(delay);
        using var restarted = fixture.Host.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var worker = new SimulationAuctionWorker(restarted.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SimulationAuctionWorker>.Instance);
        await worker.RunOnceAsync(default); await Task.WhenAll(Close(id), Close(id)); await worker.RunOnceAsync(default);
        await using var scope = restarted.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var stored = await db.SimulationRooms.SingleAsync(x => x.Id == room.RoomId); Assert.Equal("ClosedNoWinner", stored.State); Assert.Equal(1, await db.SimulationResults.CountAsync(x => x.RoomId == room.RoomId));
    }
    private sealed class Notifier(PostgresFixture fixture, bool fail) : IRoomNotifier
    {
        public int Calls { get; private set; }
        public async Task NotifyAsync(RoomInvalidation change, CancellationToken ct)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.True(await db.SimulationRoomEvents.AnyAsync(x => x.Id == change.EventId, ct)); Calls++; if (fail) throw new IOException("Injected publication failure");
        }
    }
    [PostgresFact] public async Task OutboxOnlySeesCommittedEventsAndRetainsFailedPublication()
    {
        var (id, _) = await Seed(); using var admin = await Login(); var room = await Configure(admin, id); var notifier = new Notifier(fixture, true);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var outbox = new RoomOutbox(db, notifier); Assert.Equal(0, await outbox.DispatchAsync(default)); Assert.True(notifier.Calls > 0);
        var pending = await db.SimulationRoomEvents.AsNoTracking().SingleAsync(x => x.RoomId == room.RoomId); Assert.Null(pending.PublishedAt); Assert.True(pending.Attempts > 0);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE simulation_room_events SET next_attempt_at = clock_timestamp() WHERE room_id = {room.RoomId}");
        var good = new Notifier(fixture, false); Assert.True(await new RoomOutbox(db, good).DispatchAsync(default) > 0); Assert.NotNull((await db.SimulationRoomEvents.AsNoTracking().SingleAsync(x => x.RoomId == room.RoomId)).PublishedAt);
    }
    [PostgresFact] public async Task CheckAndUniqueConstraintsRejectFalseModeAndDuplicateBidVersion()
    {
        var (id, _) = await Seed(); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var room = await Configure(admin, id); await Start(admin, id); await Read<BidReceipt>(await Bid(enterprise, id, 100), HttpStatusCode.Created);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE simulation_rooms SET mode = 'Real' WHERE id = {room.RoomId}")); Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO simulation_bids (id,room_id,enterprise_id,amount,room_version,accepted_at,previous_ends_at,ends_at,extended) SELECT {Guid.NewGuid()},room_id,enterprise_id,amount,room_version,accepted_at,previous_ends_at,ends_at,extended FROM simulation_bids WHERE room_id = {room.RoomId}")); Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }
    private static async Task Send(WebSocket socket, object message, CancellationToken ct) => await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + "\u001e").AsMemory(), WebSocketMessageType.Text, true, ct);
    private static async Task<string> Receive(WebSocket socket, CancellationToken ct)
    {
        var data = new byte[8192]; var result = await socket.ReceiveAsync(data.AsMemory(), ct); return Encoding.UTF8.GetString(data, 0, result.Count);
    }
    [PostgresFact] public async Task SignalRUsesExistingSessionChecksRoomScopeAndBlocksCrossOrigin()
    {
        var (id, other) = await Seed(eligible: false); using var admin = await Login(); await Configure(admin, id);
        using var anonymous = fixture.Host.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/hubs/simulation/negotiate?negotiateVersion=1", null)).StatusCode);
        using var outsider = await Login(account: other); outsider.DefaultRequestHeaders.Add("Origin", "https://untrusted.invalid"); Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsync("/hubs/simulation/negotiate?negotiateVersion=1", null)).StatusCode); outsider.DefaultRequestHeaders.Remove("Origin");
        var client = fixture.Host.Server.CreateWebSocketClient(); client.ConfigureRequest = request => request.Headers.Authorization = outsider.DefaultRequestHeaders.Authorization!.ToString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); using var socket = await client.ConnectAsync(new Uri("ws://localhost/hubs/simulation"), timeout.Token);
        await Send(socket, new { protocol = "json", version = 1 }, timeout.Token); Assert.Contains("{}", await Receive(socket, timeout.Token));
        await Send(socket, new { type = 1, invocationId = "1", target = "JoinRoom", arguments = new[] { id } }, timeout.Token); Assert.Contains("ROOM_ACCESS_DENIED", await Receive(socket, timeout.Token));
    }
    [PostgresFact] public async Task CookieBidRequiresCsrfAndDisabledSessionCannotReadOrBid()
    {
        var (id, _) = await Seed(); using var admin = await Login(); await Configure(admin, id); await Start(admin, id);
        using var client = fixture.Host.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=true", new { email = fixture.Accounts[Roles.Enterprise].Email, password = fixture.Password })).StatusCode); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Bid(client, id, 100)).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString()); await Read<BidReceipt>(await Bid(client, id, 100), HttpStatusCode.Created);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var actor = await db.Users.SingleAsync(x => x.Id == fixture.Accounts[Roles.Enterprise].Id); actor.IsEnabled = false; await db.SaveChangesAsync();
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/auctions/{id}/room")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Bid(client, id, 110)).StatusCode); }
        finally { actor.IsEnabled = true; await db.SaveChangesAsync(); }
    }
    [PostgresFact] public async Task GridValidationAndOutsideWindowDoNotExtendOrWriteRejectedBid()
    {
        var (id, _) = await Seed(endsIn: 120); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise);
        var room = await Configure(admin, id, Rules() with { IncrementRule = "StartingPriceGrid", ExtensionRule = "FromBidTime", ExtensionWindowSeconds = 5, ExtensionSeconds = 10, MaxExtensions = 2 }); await Start(admin, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await Bid(enterprise, id, 105)).StatusCode); var accepted = await Read<BidReceipt>(await Bid(enterprise, id, 110), HttpStatusCode.Created); Assert.Equal(room.EndsAt, accepted.EndsAt);
        var view = (await enterprise.GetFromJsonAsync<RoomView>($"/api/v1/auctions/{id}/room"))!; Assert.Equal(1, view.BidCount); Assert.Equal(0, view.ExtensionCount);
    }
    [PostgresFact] public async Task ReplayAfterCloseReturnsOriginalReceiptWithoutNewResultOrBid()
    {
        var (id, _) = await Seed(endsIn: 10); using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise); var room = await Configure(admin, id); await Start(admin, id); var key = Guid.NewGuid();
        var accepted = await Read<BidReceipt>(await Bid(enterprise, id, 100, key), HttpStatusCode.Created);
        var delay = room.EndsAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100); if (delay > TimeSpan.Zero) await Task.Delay(delay); Assert.True(await Close(id));
        Assert.Equal(accepted, await Read<BidReceipt>(await Bid(enterprise, id, 100.0000m, key), HttpStatusCode.Created)); Assert.Equal(HttpStatusCode.Conflict, (await Bid(enterprise, id, 110)).StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(1, await db.SimulationBids.CountAsync(x => x.RoomId == room.RoomId)); Assert.Equal(1, await db.SimulationResults.CountAsync(x => x.RoomId == room.RoomId));
    }
}
