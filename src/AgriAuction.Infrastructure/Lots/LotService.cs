using System.Security.Cryptography;
using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Lots;

public sealed class LotService(AppDbContext db, CompletionProfile? completion=null) : ILotService
{
    private async Task Approved(Guid actor,CancellationToken ct)
    {
        if(completion?.Enabled!=true)return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id={actor} FOR SHARE",ct);
        if(!await db.Users.AnyAsync(u=>u.Id==actor&&u.Role=="Farmer"&&u.IsEnabled&&u.ApprovalState=="Approved",ct))throw new WorkflowConflict("ACCOUNT_APPROVAL_REQUIRED","Cần Admin duyệt tài khoản trước khi tạo/gửi lô.");
    }
    private static ImageView Image(Guid id, int width, int height, DateTimeOffset at) => new(id, "/api/v1/media/" + id, width, height, at);
    public async Task<LotPage> ListAsync(Guid actorId, bool admin, string? query, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100 || query?.Length > 160)
            throw new RuleViolation(new() { ["query"] = ["Trang 1–1000000, kích thước 1–100, tìm kiếm tối đa 160 ký tự."] });
        var rows = db.Lots.AsNoTracking().Where(x => admin ? x.State == LotStates.PendingReview : x.FarmerId == actorId);
        if (!string.IsNullOrWhiteSpace(query)) rows = rows.Where(x => x.Content.Title.Contains(query.Trim()));
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.SubmittedAt).ThenByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Join(db.Users, x => x.FarmerId, x => x.Id,
                (lot, owner) => new LotListItem(lot.Id, lot.Content.Title, owner.DisplayName, lot.State, lot.Version, lot.SubmittedAt, lot.UpdatedAt)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }
    public async Task<LotView> GetAsync(Guid id, Guid actorId, bool admin, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin ? x.SubmissionNumber > 0 : x.FarmerId == actorId), ct)
            ?? throw new ResourceMissing();
        var farmerName = await db.Users.Where(x => x.Id == lot.FarmerId).Select(x => x.DisplayName).SingleAsync(ct);
        var categoryName = await db.Categories.Where(x => x.Id == lot.Content.CategoryId).Select(x => x.Name).SingleAsync(ct);
        var images = await db.LotImages.AsNoTracking().Where(x => x.LotId == id)
            .Select(x => new { x.Id, x.Width, x.Height, x.UploadedAt, x.IsRemoved }).ToListAsync(ct);
        var versions = await db.LotRevisions.AsNoTracking().Where(x => x.LotId == id).OrderBy(x => x.Number).ToListAsync(ct);
        var versionIds = versions.Select(x => x.Id).ToArray();
        var links = await db.RevisionImages.AsNoTracking().Where(x => versionIds.Contains(x.RevisionId)).ToListAsync(ct);
        var reviews = await db.LotReviews.AsNoTracking().Where(x => x.LotId == id).OrderBy(x => x.ReviewedAt)
            .Join(db.Users, x => x.AdminId, x => x.Id,
                (review, adminUser) => new ReviewView(review.Id, review.RevisionId, review.AdminId, adminUser.DisplayName, review.Outcome, review.Reason, review.ReviewedAt)).ToListAsync(ct);
        return new(lot.Id, lot.FarmerId, farmerName, categoryName, lot.State, lot.Version, lot.CurrentRevisionId, lot.Content,
            lot.CreatedAt, lot.UpdatedAt, lot.SubmittedAt,
            images.Where(x => !x.IsRemoved).OrderBy(x => x.UploadedAt).Select(x => Image(x.Id, x.Width, x.Height, x.UploadedAt)).ToList(),
            versions.Select(v => new RevisionView(v.Id, v.Number, v.Content, v.SubmittedAt,
                images.Where(i => links.Any(l => l.RevisionId == v.Id && l.ImageId == i.Id)).Select(i => Image(i.Id, i.Width, i.Height, i.UploadedAt)).ToList())).ToList(), reviews);
    }
    private async Task CategoryAsync(Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(x => x.Id == categoryId && x.Active, ct))
            throw new RuleViolation(new() { ["categoryId"] = ["Danh mục không tồn tại hoặc đã ngừng sử dụng."] });
    }
    private async Task<Lot> LockedAsync(Guid id, Guid actorId, bool admin, long expectedVersion, CancellationToken ct)
    {
        // The caller owns a transaction. All edits, image changes, submit and review
        // serialize on this row; the version/state checks happen AFTER the lock.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM lots WHERE id = {id} FOR UPDATE", ct);
        var lot = await db.Lots.SingleOrDefaultAsync(x => x.Id == id && (admin ? x.SubmissionNumber > 0 : x.FarmerId == actorId), ct)
            ?? throw new ResourceMissing();
        lot.Expect(expectedVersion);
        return lot;
    }
    private void Audit(Lot lot, Guid actorId, string action, string? before = null) => db.AuditLogs.Add(new()
    {
        ActorId = actorId, EntityId = lot.Id, Action = action, Before = before,
        After = JsonSerializer.Serialize(new { lot.State, lot.Version, lot.CurrentRevisionId })
    });
    public async Task<LotView> CreateAsync(CreateLotRequest request, Guid actorId, CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Approved(actorId,ct);
        var lot = Lot.Create(actorId, request.Content);
        await CategoryAsync(lot.Content.CategoryId, ct);
        db.Lots.Add(lot);
        Audit(lot, actorId, "LotCreated");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(lot.Id, actorId, false, ct);
    }
    public async Task<LotView> UpdateAsync(Guid id, UpdateLotRequest request, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lot = await LockedAsync(id, actorId, false, request.ExpectedVersion, ct);
        var before = JsonSerializer.Serialize(new { lot.State, lot.Version, lot.Content });
        lot.Update(request.Content);
        await CategoryAsync(lot.Content.CategoryId, ct);
        Audit(lot, actorId, "LotUpdated", before);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, actorId, false, ct);
    }
    public async Task<LotView> SubmitAsync(Guid id, long expectedVersion, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lot = await LockedAsync(id, actorId, false, expectedVersion, ct);
        await Approved(actorId,ct);
        await CategoryAsync(lot.Content.CategoryId, ct);
        var imageIds = await db.LotImages.Where(x => x.LotId == id && !x.IsRemoved).Select(x => x.Id).ToListAsync(ct);
        var version = lot.Submit(imageIds.Count);
        db.LotRevisions.Add(version);
        db.RevisionImages.AddRange(imageIds.Select(imageId => new RevisionImage { RevisionId = version.Id, ImageId = imageId }));
        Audit(lot, actorId, "LotSubmitted");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, actorId, false, ct);
    }
    public async Task<LotView> DecideAsync(Guid id, LotDecisionRequest request, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lot = await LockedAsync(id, actorId, true, request.ExpectedVersion, ct);
        if (lot.FarmerId == actorId) throw new PermissionDenied();
        string? checklist=null;
        if(completion?.Enabled==true&&request.Outcome=="Approve")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(7360021)",ct);
            var items=await db.WorkflowCatalogItems.AsNoTracking().Where(i=>i.Kind=="Inspection"&&i.Active).OrderBy(i=>i.Id).ToListAsync(ct);
            if(items.Count > 0 && (request.ChecklistItems is null || !items.Select(i=>i.Id).Order().SequenceEqual(request.ChecklistItems.Distinct().Order())))
                throw new WorkflowConflict("INSPECTION_CHECKLIST_REQUIRED","Admin phải xác nhận đủ checklist đang hoạt động trước khi duyệt.");
            checklist=JsonSerializer.Serialize(items.Select(i=>new{i.Id,i.Name,i.Version}));
        }
        lot.Decide(request.ReviewedRevisionId, request.Outcome, request.Reason);
        db.LotReviews.Add(new() { LotId = id, RevisionId = request.ReviewedRevisionId, AdminId = actorId,
            Outcome = request.Outcome!, Reason = request.Reason!.Trim(), ChecklistJson=checklist });
        Audit(lot, actorId, "Lot" + lot.State);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, actorId, true, ct);
    }
    public async Task<LotView> AddImageAsync(Guid id, long expectedVersion, Guid actorId, ImagePayload image, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lot = await LockedAsync(id, actorId, false, expectedVersion, ct);
        lot.RequireEditable();
        if (await db.LotImages.CountAsync(x => x.LotId == id && !x.IsRemoved, ct) >= ImageLimits.MaxImagesPerLot)
            throw new RuleViolation(new() { ["file"] = [$"Tối đa {ImageLimits.MaxImagesPerLot} ảnh đang gắn với lô (giới hạn tài nguyên demo)."] });
        db.LotImages.Add(new() { LotId = id, UploadedBy = actorId, Data = image.Data, Width = image.Width, Height = image.Height,
            Sha256 = Convert.ToHexString(SHA256.HashData(image.Data)) });
        lot.ImagesChanged();
        Audit(lot, actorId, "LotImageAdded");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, actorId, false, ct);
    }
    public async Task<LotView> RemoveImageAsync(Guid id, Guid imageId, long expectedVersion, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lot = await LockedAsync(id, actorId, false, expectedVersion, ct);
        lot.RequireEditable();
        var image = await db.LotImages.SingleOrDefaultAsync(x => x.Id == imageId && x.LotId == id && !x.IsRemoved, ct)
            ?? throw new ResourceMissing();
        image.IsRemoved = true; // Retain bytes and immutable submitted-image references.
        lot.ImagesChanged();
        Audit(lot, actorId, "LotImageDetached");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, actorId, false, ct);
    }
    public async Task<byte[]> ReadImageAsync(Guid imageId, Guid actorId, bool admin, CancellationToken ct)
    {
        var bytes = await db.LotImages.AsNoTracking().Where(x => x.Id == imageId)
            .Join(db.Lots, x => x.LotId, x => x.Id, (image, lot) => new { image, lot })
            .Where(x => admin ? x.lot.SubmissionNumber > 0 : x.lot.FarmerId == actorId).Select(x => x.image.Data).SingleOrDefaultAsync(ct);
        return bytes ?? throw new ResourceMissing();
    }
}
