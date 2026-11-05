using System.Text.Json;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Notifications;
using AgriAuction.Domain.Transactions;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Transactions;

public sealed class PostAuctionProcessor(AppDbContext db) : IPostAuctionProcessor
{
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    // Recover Phase 4 results predating this module as well as an interrupted batch.
    // Never edit a result, bid, auction, registration, deposit or existing trade.
    public async Task<int> RecoverAsync(CancellationToken ct)
    {
        var ids = await db.SimulationResults.AsNoTracking().Where(r => !db.PostAuctionEvents.Any(e => e.RoomId == r.RoomId))
            .OrderBy(r => r.ClosedAt).Take(50).Select(r => r.RoomId).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT room_id FROM simulation_results WHERE room_id = {id} FOR UPDATE", ct);
            if (await db.PostAuctionEvents.AnyAsync(e => e.RoomId == id, ct)) continue;
            var now = await Now(ct);
            db.PostAuctionEvents.Add(new() { RoomId = id, CreatedAt = now, NextAttemptAt = now });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); count++;
        }
        db.ChangeTracker.Clear(); return count;
    }

    private void Notify(PostAuctionEvent source, Guid recipient, Guid auction, Guid? trade, string kind, string title, string message, DateTimeOffset now)
        => db.Notifications.Add(new Notification { RecipientId = recipient, EventId = source.RoomId, AuctionId = auction,
            TransactionId = trade, Kind = kind, Title = title, Message = message, CreatedAt = now });

    private async Task Project(PostAuctionEvent source, CancellationToken ct)
    {
        var room = await db.SimulationRooms.AsNoTracking().SingleAsync(r => r.Id == source.RoomId, ct);
        var result = await db.SimulationResults.AsNoTracking().SingleAsync(r => r.RoomId == source.RoomId, ct);
        var auction = await db.Auctions.AsNoTracking().SingleAsync(a => a.Id == room.AuctionId, ct);
        var revision = await db.LotRevisions.AsNoTracking().SingleAsync(r => r.Id == auction.RevisionId && r.LotId == auction.LotId, ct);
        if (!auction.IsSimulation || auction.State != "Scheduled" || auction.PublishedAt is null
            || room.Mode != "Simulation" || result.Mode != "Simulation" || result.RoomVersion != room.Version
            || result.ClosedAt != room.ClosedAt || result.ClosedAt < room.EndsAt
            || !await db.LotReviews.AnyAsync(r => r.RevisionId == revision.Id && r.Outcome == "Approve", ct)
            || !await db.RevisionImages.AnyAsync(i => i.RevisionId == revision.Id, ct))
            throw new WorkflowConflict("INVALID_CLOSED_RESULT", "Kết quả hoặc hồ sơ duyệt không hợp lệ.");
        var now = await Now(ct);
        if (room.State == "ClosedNoWinner" && result.WinnerId is null && result.WinningBidId is null && result.Amount is null && room.BidCount == 0)
        {
            Notify(source, auction.FarmerId, auction.Id, null, "AuctionClosedNoWinner", "Phiên Simulation không có người thắng",
                $"Lô {revision.Content.Title}: phiên đã kết thúc, không phát sinh hồ sơ giao dịch thắng giá.", now);
        }
        else
        {
            var bid = await db.SimulationBids.AsNoTracking().SingleOrDefaultAsync(b => b.Id == result.WinningBidId, ct)
                ?? throw new WorkflowConflict("INVALID_CLOSED_RESULT", "Thiếu giá thắng đã ghi nhận.");
            var names = await db.Users.AsNoTracking().Where(u => u.Id == auction.FarmerId || u.Id == bid.EnterpriseId).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
            var category = await db.Categories.Where(c => c.Id == revision.Content.CategoryId).Select(c => c.Name).SingleAsync(ct);
            var snapshot = new TransactionSnapshot(revision.Content, category, room.Rules, room.OpenedAt!.Value, room.EndsAt, bid.AcceptedAt, names[auction.FarmerId], names[bid.EnterpriseId]);
            var trade = PostAuctionTransaction.FromResult(room, result, bid, auction.LotId, revision.Id, auction.FarmerId,
                revision.Content.Title, JsonSerializer.Serialize(snapshot), now);
            db.PostAuctionTransactions.Add(trade);
            if (auction.PolicyProfile is not null) trade.ActivateCompletion(auction.PolicyProfile);
            db.TransactionHistory.Add(new() { TransactionId = trade.Id, State = trade.State, OccurredAt = now });
            db.AuditLogs.Add(new() { EntityId = trade.Id, ActorId = Guid.Empty, Action = "SimulationTransactionCreated",
                After = JsonSerializer.Serialize(new { trade.Id, trade.RoomId, trade.ResultVersion, trade.State, trade.Mode, source = "PostAuctionWorker" }), OccurredAt = now });
            Notify(source, trade.EnterpriseId, auction.Id, trade.Id, "WinnerRecorded", "Bạn thắng phiên Simulation",
                $"Kết quả đã chốt cho lô {trade.Title}. Đây là kết quả thử nghiệm, chưa ghi nhận thanh toán hoặc giao hàng.", now);
            Notify(source, trade.FarmerId, auction.Id, trade.Id, "LotSoldSimulation", "Lô của bạn có người thắng Simulation",
                $"Phiên của lô {trade.Title} đã chốt có người thắng; chưa phải giao dịch hoàn tất.", now);
            foreach (var recipient in new[] { trade.FarmerId, trade.EnterpriseId })
                Notify(source, recipient, auction.Id, trade.Id, "TransactionCreated", "Hồ sơ giao dịch Simulation được tạo",
                    trade.PolicyProfile is null ? $"Lô {trade.Title}: chờ xác nhận chính sách. Thanh toán, giao nhận và hoàn cọc đang khóa." : $"Lô {trade.Title}: giá toàn bộ lô, VND; thanh toán và giao nhận mô phỏng theo profile D35.", now);
        }
        db.AuditLogs.Add(new() { EntityId = source.RoomId, ActorId = Guid.Empty, Action = "PostAuctionResultProcessed",
            After = JsonSerializer.Serialize(new { source.RoomId, room.State, mode = "Simulation", source = "PostAuctionWorker" }), OccurredAt = now });
        source.ProcessedAt = now; source.Attempts++; source.LastErrorType = null;
    }

    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        var processed = 0;
        for (var n = 0; n < 50; n++)
        {
            db.ChangeTracker.Clear(); Guid? failedId = null; Exception? failure = null;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                var source = (await db.PostAuctionEvents.FromSqlRaw("SELECT * FROM post_auction_events WHERE processed_at IS NULL AND next_attempt_at <= clock_timestamp() ORDER BY created_at, room_id LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct)).SingleOrDefault();
                if (source is null) break;
                try
                {
                    await Project(source, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); processed++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    await tx.RollbackAsync(ct); failedId = source.RoomId; failure = error;
                }
            }
            if (failedId.HasValue)
            {
                // Failed transaction has been disposed. Persist only retry metadata;
                // a competing successful worker must never have its marker reverted.
                db.ChangeTracker.Clear(); await using var retry = await db.Database.BeginTransactionAsync(ct);
                var source = (await db.PostAuctionEvents.FromSqlInterpolated($"SELECT * FROM post_auction_events WHERE room_id = {failedId.Value} FOR UPDATE").ToListAsync(ct)).Single();
                if (source.ProcessedAt is null)
                {
                    source.Attempts++; source.LastErrorType = failure!.GetType().Name;
                    source.NextAttemptAt = (await Now(ct)).AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(source.Attempts, 6))));
                    await db.SaveChangesAsync(ct);
                }
                await retry.CommitAsync(ct);
            }
        }
        db.ChangeTracker.Clear(); return processed;
    }
}
