using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Notifications;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Transactions;

public sealed class TransactionLifecycle(AppDbContext db, CompletionProfile? profile = null) : ITransactionLifecycle
{
    public async Task<TransactionReceipt> ExecuteAsync(Guid id, string action, TransactionActionRequest request, Guid key, Guid actor, string role, CancellationToken ct)
    {
        if (profile?.Enabled != true) throw new WorkflowConflict("POLICY_LOCKED", "Lifecycle chỉ mở trên môi trường Completion đã kiểm chứng.");
        if (key == Guid.Empty || request.ExpectedVersion < 1 || request.Note?.Length > 1000
            || (action is "ReportDeliveryIssue" or "ResolveDeliveryIssue" && string.IsNullOrWhiteSpace(request.Note)))
            throw new RuleViolation(new() { ["command"] = ["Cần UUID, phiên bản dương và lý do tối đa 1000 ký tự (bắt buộc khi báo vấn đề giao nhận)."] });
        if (!Roles.All.Contains(role)) throw new PermissionDenied();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize actor/key first; then transaction, then shared actor lock. No account-exclusive locks here.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, action, request }))));
        var lockKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"transaction:{actor}:{key}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM post_auction_transactions WHERE id = {id} FOR UPDATE", ct);
        var trade = await db.PostAuctionTransactions.SingleOrDefaultAsync(t => t.Id == id &&
            (role == Roles.Administrator || (role == Roles.Farmer && t.FarmerId == actor) || (role == Roles.Enterprise && t.EnterpriseId == actor)), ct) ?? throw new ResourceMissing();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {actor} FOR SHARE", ct);
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.Role == role && u.IsEnabled, ct)) throw new PermissionDenied();
        var previous = await db.TransactionCommands.AsNoTracking().SingleOrDefaultAsync(c => c.ActorId == actor && c.RequestId == key, ct);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) throw new WorkflowConflict("IDEMPOTENCY_CONFLICT", "Khóa đã dùng với nội dung khác.");
            return JsonSerializer.Deserialize<TransactionReceipt>(previous.ReceiptJson)!;
        }
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        trade.Apply(action, actor, role, request.ExpectedVersion, now);
        var command = new AgriAuction.Domain.Transactions.TransactionCommand { TransactionId = id, ActorId = actor, RequestId = key, PayloadHash = hash, OccurredAt = now };
        var receipt = new TransactionReceipt(id, command.Id, action, trade.Version, trade.State, trade.PaymentState, trade.DeliveryState, trade.WinningPrice, trade.Currency, now, trade.CompletedAt);
        command.ReceiptJson = JsonSerializer.Serialize(receipt); db.TransactionCommands.Add(command);
        db.TransactionHistory.Add(new() { TransactionId = id, ActorId = actor, Kind = action, State = trade.State, Version = trade.Version, OccurredAt = now, Note = request.Note });
        db.AuditLogs.Add(new() { ActorId = actor, EntityId = id, Action = "Transaction" + action, After = command.ReceiptJson, OccurredAt = now });
        // Generated notification source FK is populated by PostgreSQL, so persist
        // its command principal first inside the SAME uncommitted transaction.
        await db.SaveChangesAsync(ct);
        var recipients = await db.Users.Where(u => u.IsEnabled && (u.Id == trade.FarmerId || u.Id == trade.EnterpriseId || u.Role == Roles.Administrator))
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var recipient in recipients)
            db.Notifications.Add(new Notification { RecipientId = recipient, EventId = command.Id, AuctionId = trade.AuctionId, TransactionId = id,
                Kind = "TransactionActionRecorded", Title = "Hồ sơ giao dịch mô phỏng đã cập nhật", Message = $"{action}: {trade.Title}. Chỉ mô phỏng, không phát sinh tiền thật.", CreatedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return receipt;
    }
}
