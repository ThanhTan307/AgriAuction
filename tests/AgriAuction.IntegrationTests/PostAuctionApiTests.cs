using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriAuction.Api.Modules.Transactions;
using AgriAuction.Application.Realtime;
using AgriAuction.Application.Transactions;
using AgriAuction.Application.Notifications;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Realtime;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class PostAuctionApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private async Task<HttpClient> Login(string role = Roles.Administrator, Account? account = null, HttpClient? client = null)
    {
        client ??= fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = (account ?? fixture.Accounts[role]).Email, password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()); return client;
    }
    private async Task<(Guid RoomId, Guid AuctionId, Account Other)> Historical(bool winner = true, bool queued = true)
    {
        // Historical fixtures only in the newly allocated integration schema.
        var (auctionId, other) = await new RoomApiTests(fixture).Seed(endsIn: -5);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auction = await db.Auctions.SingleAsync(a => a.Id == auctionId);
        var room = SimulationRoom.Configure(auctionId, fixture.Accounts[Roles.Administrator].Id, RoomApiTests.Rules(), auction.Terms.StartsAt, auction.Terms.EndsAt, auction.Terms.StartsAt.AddMinutes(-1));
        db.SimulationRooms.Add(room); await db.SaveChangesAsync(); room.Start(fixture.Accounts[Roles.Administrator].Id, auction.Terms.StartsAt);
        if (winner) db.SimulationBids.Add(room.Accept(fixture.Accounts[Roles.Enterprise].Id, 123.4567m, auction.Terms.EndsAt.AddSeconds(-1)));
        var result = room.CloseIfDue(auction.Terms.EndsAt.AddSeconds(1))!; db.SimulationResults.Add(result);
        if (queued) db.PostAuctionEvents.Add(new() { RoomId = room.Id, CreatedAt = result.ClosedAt, NextAttemptAt = result.ClosedAt });
        await db.SaveChangesAsync(); return (room.Id, auctionId, other);
    }
    private async Task Process(bool recover = false)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var p = scope.ServiceProvider.GetRequiredService<IPostAuctionProcessor>();
        if (recover) await p.RecoverAsync(default); await p.ProcessAsync(default);
    }
    private async Task<TransactionDetail> Detail(Guid room)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.PostAuctionTransactions.Where(t => t.RoomId == room).Select(t => t.Id).SingleAsync();
        return await scope.ServiceProvider.GetRequiredService<ITransactionService>().GetAsync(id, fixture.Accounts[Roles.Administrator].Id, Roles.Administrator, default);
    }
    [PostgresFact] public async Task WorkerCloseQueuesCommittedResultAndCreatesOneTransaction()
    {
        using var admin = await Login(); using var enterprise = await Login(Roles.Enterprise);
        var (id, _) = await new RoomApiTests(fixture).Seed(endsIn: 10);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room", new ConfigureRoomRequest(2, RoomApiTests.Rules()))).StatusCode);
        var room = (await (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room/start", new StartRoomRequest(1))).Content.ReadFromJsonAsync<RoomView>())!;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auctions/{id}/room/bids") { Content = JsonContent.Create(new { amount = "100" }) }; request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.Created, (await enterprise.SendAsync(request)).StatusCode);
        var delay = room.EndsAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(150); if (delay > TimeSpan.Zero) await Task.Delay(delay);
        async Task Close() { await using var s = fixture.Host.Services.CreateAsyncScope(); await s.ServiceProvider.GetRequiredService<IRoomService>().CloseDueAsync(id, default); }
        await Task.WhenAll(Close(), Close(), Close()); await Task.WhenAll(Process(), Process(), Process());
        var trade = await Detail(room.RoomId); Assert.Equal(100, trade.Transaction.WinningPrice); Assert.Equal("AwaitingPolicy", trade.Transaction.State);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.SimulationResults.CountAsync(r => r.RoomId == room.RoomId)); Assert.Equal(1, await db.PostAuctionEvents.CountAsync(r => r.RoomId == room.RoomId && r.ProcessedAt != null));
        Assert.Equal(1, await db.PostAuctionTransactions.CountAsync(r => r.RoomId == room.RoomId)); Assert.Equal(4, await db.Notifications.CountAsync(n => n.EventId == room.RoomId));
    }
    [PostgresFact] public async Task ConcurrentProjectionIsIdempotentAndSnapshotsRemainImmutable()
    {
        var (room, _, _) = await Historical(); await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Process()));
        var first = await Detail(room); await Process(); var replay = await Detail(room);
        Assert.Equal(first.Transaction, replay.Transaction); Assert.Equal(123.4567m, first.Transaction.WinningPrice);
        Assert.Equal("PolicyLocked", first.Transaction.PaymentState); Assert.Equal("PolicyLocked", first.Transaction.DeliveryState); Assert.Single(first.History);
        Assert.Equal(first.Transaction.Title, first.Snapshot.ApprovedLot.Title); Assert.Equal("whole lot simulation", first.Transaction.PriceBasis);
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == first.Transaction.Id && a.Action == "SimulationTransactionCreated"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE categories SET name = 'Renamed after snapshot' WHERE id = {first.Snapshot.ApprovedLot.CategoryId}");
        Assert.Equal(first.Snapshot.CategoryName, (await Detail(room)).Snapshot.CategoryName);
    }
    [PostgresFact] public async Task NoWinnerNotifiesFarmerWithoutInventingTransactionOrWinner()
    {
        var (room, _, _) = await Historical(winner: false); await Process(); await Process();
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PostAuctionTransactions.AnyAsync(t => t.RoomId == room)); var note = await db.Notifications.SingleAsync(n => n.EventId == room);
        Assert.Equal(fixture.Accounts[Roles.Farmer].Id, note.RecipientId); Assert.Equal("AuctionClosedNoWinner", note.Kind); Assert.Null(note.TransactionId);
    }
    [PostgresFact] public async Task FreshHostRecoversHistoricalResultsAndDoesNotDuplicateAfterRestart()
    {
        var (room, _, _) = await Historical(queued: false);
        using var restarted = fixture.Host.WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var worker = new PostAuctionWorker(restarted.Services.GetRequiredService<IServiceScopeFactory>(), restarted.Services.GetRequiredService<IHostEnvironment>(), NullLogger<PostAuctionWorker>.Instance);
        await worker.RunOnceAsync(default); await worker.RunOnceAsync(default); await Process(recover: true);
        var t = await Detail(room); Assert.Single(t.History);
        await using var s = restarted.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PostAuctionTransactions.CountAsync(t => t.RoomId == room)); Assert.Equal(4, await db.Notifications.CountAsync(n => n.EventId == room));
    }
    [PostgresFact] public async Task AuditDatabaseFaultRollsBackAllProjectionWritesThenRetrySucceeds()
    {
        var (room, _, _) = await Historical(); await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "postfault_" + Guid.NewGuid().ToString("N");
        // Identifiers below are generated ASCII UUIDs, never request input.
        var faultSql = $"CREATE TABLE {token}(enabled boolean NOT NULL); INSERT INTO {token} VALUES(true); CREATE FUNCTION {token}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action = 'SimulationTransactionCreated' AND NEW.after::jsonb->>'RoomId' = '{room}' AND (SELECT enabled FROM {token}) THEN RAISE EXCEPTION 'Injected projection audit fault'; END IF; RETURN NEW; END $$; CREATE TRIGGER {token} BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION {token}();"; await db.Database.ExecuteSqlRawAsync(faultSql);
        await Process(); Assert.False(await db.PostAuctionTransactions.AnyAsync(t => t.RoomId == room)); Assert.False(await db.Notifications.AnyAsync(n => n.EventId == room));
        var source = await db.PostAuctionEvents.AsNoTracking().SingleAsync(e => e.RoomId == room); Assert.Null(source.ProcessedAt); Assert.True(source.Attempts > 0); Assert.NotNull(source.LastErrorType);
        var resetFaultSql = $"UPDATE {token} SET enabled = false"; await db.Database.ExecuteSqlRawAsync(resetFaultSql); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE post_auction_events SET next_attempt_at = clock_timestamp() WHERE room_id = {room}");
        await Process(); Assert.NotNull((await db.PostAuctionEvents.AsNoTracking().SingleAsync(e => e.RoomId == room)).ProcessedAt); Assert.Single((await Detail(room)).History);
    }
    [PostgresFact] public async Task UncommittedSourceCannotPublishNotifications()
    {
        var (room, _, _) = await Historical(queued: false); await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.PostAuctionEvents.Add(new() { RoomId = room, CreatedAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
            await Process(); Assert.False(await db.Notifications.AnyAsync(n => n.EventId == room)); await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear(); await Process(recover: true); Assert.NotNull(await Detail(room));
    }
    [PostgresFact] public async Task TransactionAndInboxOwnershipAreIndependentAndDoNotLeakToOtherAccounts()
    {
        var (room, _, other) = await Historical(); await Process(); var trade = (await Detail(room)).Transaction;
        using var owner = await Login(Roles.Enterprise); using var farmer = await Login(Roles.Farmer); using var outsider = await Login(account: other); using var admin = await Login();
        foreach (var c in new[] { owner, farmer, admin }) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/transactions/" + trade.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/api/v1/transactions/" + trade.Id)).StatusCode);
        var others = await outsider.GetFromJsonAsync<TransactionPage>("/api/v1/transactions"); Assert.DoesNotContain(others!.Items, t => t.Id == trade.Id);
        var notes = (await owner.GetFromJsonAsync<NotificationPage>("/api/v1/me/notifications?pageSize=100"))!; var own = notes.Items.First(n => n.TransactionId == trade.Id);
        foreach (var c in new[] { outsider, farmer, admin }) Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync($"/api/v1/me/notifications/{own.Id}/read", new { })).StatusCode);
        Assert.DoesNotContain((await outsider.GetFromJsonAsync<NotificationPage>("/api/v1/me/notifications"))!.Items, n => n.Id == own.Id);
    }
    [PostgresFact] public async Task MarkReadConcurrentReplayKeepsTimestampAndUnreadCountMatchesDatabase()
    {
        var (room, _, _) = await Historical(); await Process(); using var enterprise = await Login(Roles.Enterprise);
        var before = (await enterprise.GetFromJsonAsync<NotificationPage>("/api/v1/me/notifications?pageSize=100"))!; var auctionId = (await Detail(room)).Transaction.AuctionId; var note = before.Items.First(n => n.AuctionId == auctionId);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => enterprise.PostAsJsonAsync($"/api/v1/me/notifications/{note.Id}/read", new { })));
        var receipts = new List<NotificationView>(); foreach (var response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); receipts.Add((await response.Content.ReadFromJsonAsync<NotificationView>())!); }
        Assert.Single(receipts.Select(n => n.ReadAt).Distinct()); Assert.NotNull(receipts[0].ReadAt);
        var after = (await enterprise.GetFromJsonAsync<NotificationPage>("/api/v1/me/notifications?pageSize=100"))!; Assert.Equal(before.UnreadCount - 1, after.UnreadCount);
        var count = await enterprise.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications/unread-count"); Assert.Equal(after.UnreadCount, count.GetProperty("unreadCount").GetInt32());
    }
    [PostgresFact] public async Task ApiRejectsAnonymousValidationAndClientChosenRecipient()
    {
        using var anonymous = fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var route in new[] { "/api/v1/transactions", "/api/v1/me/notifications", "/api/v1/me/notifications/unread-count" }) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        using var admin = await Login(); foreach (var route in new[] { "/api/v1/transactions?page=0", "/api/v1/me/notifications?pageSize=101" }) Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/me/notifications/{Guid.NewGuid()}/read", new { recipientId = fixture.Accounts[Roles.Enterprise].Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/transactions/{Guid.NewGuid()}/payment-simulation", new { })).StatusCode);
    }
    [PostgresFact] public async Task ProductionBlocksSimulationApiAndWorkerEvenWhenConfiguredEnabled()
    {
        var (room, _, _) = await Historical();
        using var prod = fixture.Host.WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Simulation:PostAuctionWorkerEnabled"] = "true" })); }); using var client = await Login(client: prod.CreateClient());
        foreach (var route in new[] { "/api/v1/transactions", "/api/v1/me/notifications" }) Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/me/notifications/{Guid.NewGuid()}/read", new { })).StatusCode);
        Assert.DoesNotContain(prod.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), x => x is PostAuctionWorker);
        using var worker = new PostAuctionWorker(prod.Services.GetRequiredService<IServiceScopeFactory>(), prod.Services.GetRequiredService<IHostEnvironment>(), NullLogger<PostAuctionWorker>.Instance); await worker.RunOnceAsync(default);
        await using var s = fixture.Host.Services.CreateAsyncScope(); Assert.False(await s.ServiceProvider.GetRequiredService<AppDbContext>().PostAuctionTransactions.AnyAsync(t => t.RoomId == room));
    }
    [PostgresFact] public async Task DatabaseConstraintsBlockDuplicateTradeNotificationAndFalsePaymentState()
    {
        var (room, _, _) = await Historical(); await Process(); var trade = (await Detail(room)).Transaction;
        await using var s = fixture.Host.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE post_auction_transactions SET payment_state = 'Confirmed' WHERE id = {trade.Id}")); Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notifications (id,recipient_id,event_id,auction_id,transaction_id,kind,title,message,mode,created_at,read_at) SELECT {Guid.NewGuid()},recipient_id,event_id,auction_id,transaction_id,kind,title,message,mode,created_at,read_at FROM notifications WHERE event_id = {room} LIMIT 1")); Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var duplicateTrade = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO post_auction_transactions SELECT (jsonb_populate_record(NULL::post_auction_transactions, to_jsonb(t) || jsonb_build_object('id', {Guid.NewGuid()}))).* FROM post_auction_transactions t WHERE t.id = {trade.Id}")); Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateTrade.SqlState);
    }
    [PostgresFact] public async Task CookieNotificationReadRequiresCsrfAndDatabaseErrorsCannotReportSuccess()
    {
        var (room, _, _) = await Historical(); await Process();
        using var client = fixture.Host.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=true", new { email = fixture.Accounts[Roles.Enterprise].Email, password = fixture.Password })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var inbox = (await client.GetFromJsonAsync<NotificationPage>("/api/v1/me/notifications?pageSize=100"))!;
        var auction = (await Detail(room)).Transaction.AuctionId; var id = inbox.Items.First(n => n.AuctionId == auction).Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/me/notifications/{id}/read", new { })).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/me/notifications/{id}/read", new { })).StatusCode);
        fixture.Fault.FailNextRead(); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/transactions")).StatusCode);
    }
    [PostgresFact] public async Task OutboxInsertFailureRollsBackCloseAndResultAtomically()
    {
        using var admin = await Login();
        var (id, _) = await new RoomApiTests(fixture).Seed(endsIn: 10);
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room", new ConfigureRoomRequest(2, RoomApiTests.Rules())); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var room = (await response.Content.ReadFromJsonAsync<RoomView>())!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/admin/auctions/{id}/room/start", new StartRoomRequest(1))).StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = "closefault_" + Guid.NewGuid().ToString("N");
        var sql = $"CREATE TABLE {name}(enabled boolean NOT NULL); INSERT INTO {name} VALUES(true); CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.room_id = '{room.RoomId}'::uuid AND (SELECT enabled FROM {name}) THEN RAISE EXCEPTION 'Injected close outbox failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {name} BEFORE INSERT ON post_auction_events FOR EACH ROW EXECUTE FUNCTION {name}();"; await db.Database.ExecuteSqlRawAsync(sql);
        var delay = room.EndsAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100); if (delay > TimeSpan.Zero) await Task.Delay(delay);
        async Task Close() { await using var s = fixture.Host.Services.CreateAsyncScope(); await s.ServiceProvider.GetRequiredService<IRoomService>().CloseDueAsync(id, default); }
        await Assert.ThrowsAsync<DbUpdateException>(Close);
        Assert.Equal("Open", (await db.SimulationRooms.AsNoTracking().SingleAsync(r => r.Id == room.RoomId)).State);
        Assert.False(await db.SimulationResults.AnyAsync(r => r.RoomId == room.RoomId)); Assert.False(await db.PostAuctionEvents.AnyAsync(e => e.RoomId == room.RoomId));
        Assert.False(await db.SimulationRoomEvents.AnyAsync(e => e.RoomId == room.RoomId && e.Kind == "RoomClosed"));
        var resume = $"UPDATE {name} SET enabled = false"; await db.Database.ExecuteSqlRawAsync(resume); await Close(); await Process();
        Assert.Equal(1, await db.SimulationResults.CountAsync(r => r.RoomId == room.RoomId)); Assert.Equal(1, await db.Notifications.CountAsync(n => n.EventId == room.RoomId));
    }
}
