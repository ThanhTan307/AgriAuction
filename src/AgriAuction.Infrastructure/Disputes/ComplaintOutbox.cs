using AgriAuction.Application.Disputes;
using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Notifications;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Disputes;

public sealed class ComplaintOutbox(AppDbContext db) : IComplaintOutbox
{
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    public async Task<int> DispatchAsync(CancellationToken ct)
    {
        var count = 0;
        for (var n = 0; n < 50; n++)
        {
            db.ChangeTracker.Clear(); Guid? failed = null; Exception? failure = null;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                var entry = (await db.ComplaintEntries.FromSqlRaw("SELECT * FROM complaint_entries WHERE notified_at IS NULL AND next_attempt_at <= clock_timestamp() ORDER BY occurred_at, id LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct)).SingleOrDefault();
                if (entry is null) break;
                try
                {
                    var complaint = await db.Complaints.AsNoTracking().SingleAsync(c => c.Id == entry.ComplaintId, ct);
                    var trade = await db.PostAuctionTransactions.AsNoTracking().SingleAsync(t => t.Id == complaint.TransactionId, ct);
                    var now = await Now(ct);
                    var kind = entry.Kind switch { "Created" => "ComplaintCreated", "ResponseAdded" => "ComplaintResponded", "ReviewStarted" or "ResponseDeadlineExpired" => "ComplaintStatusChanged", "AdminComment" => "ComplaintCommented", "DecisionRecorded" => "ComplaintDecisionRecorded", _ => throw new InvalidOperationException() };
                    foreach (var recipient in entry.RecipientIds.Distinct())
                        db.Notifications.Add(new Notification { EventId = entry.Id, RecipientId = recipient, AuctionId = trade.AuctionId, TransactionId = trade.Id, ComplaintId = complaint.Id, Kind = kind,
                            Title = "Khiếu nại Simulation: " + entry.Kind, Message = "Hồ sơ có cập nhật. Xem lịch sử được phân quyền; không có thao tác tiền hoặc chế tài.", CreatedAt = now });
                    entry.NotifiedAt = now; entry.Attempts++; entry.LastErrorType = null;
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); count++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception error) { await tx.RollbackAsync(ct); failed = entry.Id; failure = error; }
            }
            if (failed.HasValue)
            {
                db.ChangeTracker.Clear(); await using var retry = await db.Database.BeginTransactionAsync(ct);
                var entry = (await db.ComplaintEntries.FromSqlInterpolated($"SELECT * FROM complaint_entries WHERE id = {failed.Value} FOR UPDATE").ToListAsync(ct)).Single();
                if (entry.NotifiedAt is null)
                {
                    entry.Attempts++; entry.LastErrorType = failure!.GetType().Name;
                    entry.NextAttemptAt = (await Now(ct)).AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(entry.Attempts, 6))));
                    await db.SaveChangesAsync(ct);
                }
                await retry.CommitAsync(ct);
            }
        }
        db.ChangeTracker.Clear(); return count;
    }
}
