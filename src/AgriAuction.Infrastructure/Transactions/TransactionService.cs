using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Transactions;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Transactions;

public sealed class TransactionService(AppDbContext db) : ITransactionService
{
    internal static void Page(int page, int size)
    {
        if (page is < 1 or > 1000000 || size is < 1 or > 100) throw new RuleViolation(new() { ["page"] = ["page 1–1000000; pageSize 1–100."] });
    }
    private async Task<IQueryable<PostAuctionTransaction>> Scope(Guid actor, string role, CancellationToken ct)
    {
        if (!Roles.All.Contains(role) || !await db.Users.AnyAsync(u => u.Id == actor && u.Role == role && u.IsEnabled, ct)) throw new PermissionDenied();
        return db.PostAuctionTransactions.AsNoTracking().Where(t => role == Roles.Administrator || (role == Roles.Farmer && t.FarmerId == actor) || (role == Roles.Enterprise && t.EnterpriseId == actor));
    }
    private static TransactionView View(PostAuctionTransaction t) => new(t.Id, t.RoomId, t.AuctionId, t.LotId, t.RevisionId, t.FarmerId, t.EnterpriseId, t.WinningBidId,
        t.ResultVersion, t.Title, t.WinningPrice, t.Currency, t.PriceBasis, t.Mode, t.State, t.PaymentState, t.DeliveryState, t.Version, t.ClosedAt, t.CreatedAt, t.PolicyProfile, t.CompletedAt,t.DisputeState);
    public async Task<TransactionPage> ListAsync(Guid actor, string role, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = await Scope(actor, role, ct); var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await tx.CommitAsync(ct); return new(rows.Select(View).ToList(), count, page, pageSize);
    }
    public async Task<TransactionDetail> GetAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var query = await Scope(actor, role, ct); var row = await query.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new ResourceMissing();
        var history = await db.TransactionHistory.AsNoTracking().Where(h => h.TransactionId == id).OrderBy(h => h.Version)
            .Select(h => new TransactionHistoryView(h.Kind, h.State, h.Version, h.OccurredAt, h.ActorId, h.Note)).ToListAsync(ct);
        return new(View(row), JsonSerializer.Deserialize<TransactionSnapshot>(row.SnapshotJson)!, history,row.AllowedActions(actor,role));
    }
}
