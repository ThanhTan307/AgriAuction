using AgriAuction.Application.Realtime;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Realtime;

public sealed class RoomOutbox(AppDbContext db, IRoomNotifier notifier) : IRoomOutbox
{
    public async Task<int> DispatchAsync(CancellationToken ct)
    {
        var sent = 0;
        for (var n = 0; n < 50; n++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var batch = await db.SimulationRoomEvents.FromSqlRaw("SELECT * FROM simulation_room_events WHERE published_at IS NULL AND next_attempt_at <= clock_timestamp() ORDER BY occurred_at, version LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
            var row = batch.SingleOrDefault(); if (row is null) break;
            row.Attempts++;
            try
            {
                await notifier.NotifyAsync(new(row.AuctionId, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Id), ct);
                row.PublishedAt = DateTimeOffset.UtcNow; sent++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { row.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(row.Attempts, 6)))); }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
        return sent;
    }
}
