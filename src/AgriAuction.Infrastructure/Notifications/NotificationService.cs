using AgriAuction.Application.Catalog;
using AgriAuction.Application.Notifications;
using AgriAuction.Domain.Notifications;
using AgriAuction.Infrastructure.Persistence;
using AgriAuction.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Notifications;

public sealed class NotificationService(AppDbContext db) : INotificationService
{
    private static NotificationView View(Notification n) => new(n.Id, n.AuctionId, n.TransactionId, n.Kind, n.Title, n.Message, n.Mode, n.CreatedAt, n.ReadAt, n.ComplaintId);
    public Task<int> UnreadAsync(Guid actor, CancellationToken ct) => db.Notifications.CountAsync(n => n.RecipientId == actor && n.ReadAt == null, ct);
    public async Task<NotificationPage> ListAsync(Guid actor, int page, int pageSize, CancellationToken ct)
    {
        TransactionService.Page(page, pageSize);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == actor);
        var count = await query.CountAsync(ct); var unread = await query.CountAsync(n => n.ReadAt == null, ct);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await tx.CommitAsync(ct); return new(rows.Select(View).ToList(), count, unread, page, pageSize);
    }
    public async Task<NotificationView> ReadAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = (await db.Notifications.FromSqlInterpolated($"SELECT * FROM notifications WHERE id = {id} AND recipient_id = {actor} FOR UPDATE").ToListAsync(ct)).SingleOrDefault() ?? throw new ResourceMissing();
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        row.MarkRead(now); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return View(row);
    }
}
