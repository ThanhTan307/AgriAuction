using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Disputes;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Transactions;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Disputes;

public sealed class ComplaintService(AppDbContext db, ITransactionService transactions) : IComplaintService
{
    private async Task Actor(Guid actor, string role, CancellationToken ct)
    {
        if (!Roles.All.Contains(role) || !await db.Users.AnyAsync(u => u.Id == actor && u.Role == role && u.IsEnabled, ct)) throw new PermissionDenied();
    }
    private static void Party(string role) { if (role != Roles.Farmer && role != Roles.Enterprise) throw new PermissionDenied(); }
    private static void Admin(string role) { if (role != Roles.Administrator) throw new PermissionDenied(); }
    private IQueryable<Complaint> Scoped(Guid actor, string role) => db.Complaints.Where(c => role == Roles.Administrator || c.ComplainantId == actor || c.RespondentId == actor);
    public async Task AuthorizeTargetAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        await Actor(actor, role, ct); Party(role);
        _ = await Target(id, actor, role, ct);
    }
    private async Task<PostAuctionTransaction> Target(Guid id, Guid actor, string role, CancellationToken ct)
        => await db.PostAuctionTransactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id &&
            ((role == Roles.Farmer && t.FarmerId == actor) || (role == Roles.Enterprise && t.EnterpriseId == actor)), ct) ?? throw new ResourceMissing();
    public async Task AuthorizeCaseAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        await Actor(actor, role, ct);
        if (!await Scoped(actor, role).AnyAsync(c => c.Id == id, ct)) throw new ResourceMissing();
    }
    private static ComplaintView View(Complaint c) => new(c.Id, c.TransactionId, c.ComplainantId, c.RespondentId, c.Title, c.Scope, c.Reason, c.Description, c.Mode, c.State, c.Outcome, c.Version, c.CreatedAt, c.UpdatedAt,c.ComplaintDeadline,c.ResponseDueAt,c.RespondedAt);
    private static void Page(int page, int size) { if (page is < 1 or > 1000000 || size is < 1 or > 100) throw new RuleViolation(new() { ["page"] = ["page 1–1000000; pageSize 1–100."] }); }
    public async Task<ComplaintPage> ListAsync(Guid actor, string role, string? state, string? scope, string? query, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await Actor(actor, role, ct);
        if (state is not null && !ComplaintStates.All.Contains(state)) throw new RuleViolation(new() { ["state"] = ["Trạng thái không hợp lệ."] });
        if (scope is not null && scope is not "AuctionRecord" and not "TransactionRecord") throw new RuleViolation(new() { ["scope"] = ["Phạm vi không hợp lệ."] });
        if (query?.Length > 200) throw new RuleViolation(new() { ["query"] = ["Tối đa 200 ký tự."] });
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var rows = Scoped(actor, role).AsNoTracking();
        if (state is not null) rows = rows.Where(c => c.State == state);
        if (scope is not null) rows = rows.Where(c => c.Scope == scope);
        if (!string.IsNullOrWhiteSpace(query)) rows = rows.Where(c => c.Title.Contains(query.Trim()) || c.Reason.Contains(query.Trim()));
        var count = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await tx.CommitAsync(ct); return new(items.Select(View).ToList(), count, page, pageSize);
    }
    private async Task<ComplaintEntryPage> History(Guid id, int page, int size, CancellationToken ct)
    {
        Page(page, size);
        var rows = db.ComplaintEntries.AsNoTracking().Where(e => e.ComplaintId == id);
        var count = await rows.CountAsync(ct);
        var entries = await rows.OrderBy(e => e.Version).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = entries.Select(e => e.Id).ToArray();
        var evidence = await db.ComplaintEvidence.AsNoTracking().Where(e => e.ComplaintId == id && ids.Contains(e.EntryId))
            .OrderBy(e => e.Id).Select(e => new EvidenceView(e.Id, e.EntryId, e.UploadedBy, "/api/v1/complaints/evidence/" + e.Id, e.ServerFileName, e.Width, e.Height, e.Sha256, e.UploadedAt)).ToListAsync(ct);
        return new(entries.Select(e => new ComplaintEntryView(e.Id, e.ActorId, e.ActorRole, e.Kind, e.Text, e.State, e.Outcome, e.Version, e.OccurredAt, evidence.Where(x => x.EntryId == e.Id).ToList())).ToList(), count, page, size);
    }
    public async Task<ComplaintDetail> GetAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await AuthorizeCaseAsync(id, actor, role, ct);
        var row = await Scoped(actor, role).AsNoTracking().SingleAsync(c => c.Id == id, ct);
        var trade = await transactions.GetAsync(row.TransactionId, actor, role, ct);
        var history = await History(id, 1, 50, ct); await tx.CommitAsync(ct);
        return new(View(row), trade, history);
    }
    public async Task<ComplaintEntryPage> EntriesAsync(Guid id, Guid actor, string role, int page, int pageSize, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await AuthorizeCaseAsync(id, actor, role, ct); var result = await History(id, page, pageSize, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<EvidenceContent> EvidenceAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        await Actor(actor, role, ct);
        var row = await db.ComplaintEvidence.AsNoTracking().Where(e => e.Id == id && Scoped(actor, role).Any(c => c.Id == e.ComplaintId)).SingleOrDefaultAsync(ct) ?? throw new ResourceMissing();
        return new(row.Data, row.ServerFileName);
    }
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string[] ImageHashes(IReadOnlyList<ImagePayload> images)
    {
        Complaint.EvidenceCount(images.Count);
        foreach (var i in images) if (i.Data.Length is < 1 or > 5242880 || i.Width is < 1 or > 6000 || i.Height is < 1 or > 6000 || (long)i.Width * i.Height > 16000000)
            throw new RuleViolation(new() { ["files"] = ["Ảnh sau chuẩn hóa vượt giới hạn tài nguyên."] });
        return images.Select(i => Convert.ToHexString(SHA256.HashData(i.Data))).ToArray();
    }
    private async Task<ComplaintReceipt?> Replay(Guid actor, Guid key, string hash, CancellationToken ct)
    {
        if (key == Guid.Empty) throw new RuleViolation(new() { ["Idempotency-Key"] = ["Cần UUID khác rỗng."] });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("complaint:" + actor + ":" + key));
        var lockKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        var old = await db.ComplaintCommands.AsNoTracking().SingleOrDefaultAsync(c => c.ActorId == actor && c.RequestId == key, ct);
        if (old is null) return null;
        if (old.PayloadHash != hash) throw new WorkflowConflict("IDEMPOTENCY_CONFLICT", "Khóa đã dùng với nội dung hoặc thao tác khác.");
        return JsonSerializer.Deserialize<ComplaintReceipt>(old.ReceiptJson)!;
    }
    private async Task<ComplaintReceipt> Append(Complaint c, string kind, string text, IReadOnlyList<ImagePayload> images, Guid key, string hash, Guid actor, string role, DateTimeOffset now, CancellationToken ct)
    {
        var recipients = await db.Users.AsNoTracking().Where(u => u.IsEnabled && (u.Role == Roles.Administrator || u.Id == c.ComplainantId || u.Id == c.RespondentId) && u.Id != actor).Select(u => u.Id).Distinct().ToArrayAsync(ct);
        var entry = new ComplaintEntry { ComplaintId = c.Id, ActorId = actor, ActorRole = role, Kind = kind, Text = text, State = c.State, Outcome = kind == "DecisionRecorded" ? c.Outcome : null, Version = c.Version, OccurredAt = now, NextAttemptAt = now, RecipientIds = recipients };
        db.ComplaintEntries.Add(entry);
        foreach (var i in images) db.ComplaintEvidence.Add(new() { ComplaintId = c.Id, EntryId = entry.Id, UploadedBy = actor, Data = i.Data, Width = i.Width, Height = i.Height, Sha256 = Convert.ToHexString(SHA256.HashData(i.Data)), UploadedAt = now });
        var receipt = new ComplaintReceipt(c.Id, entry.Id, c.Version, c.State);
        db.ComplaintCommands.Add(new() { ActorId = actor, RequestId = key, ComplaintId = c.Id, PayloadHash = hash, ReceiptJson = JsonSerializer.Serialize(receipt), CreatedAt = now });
        db.AuditLogs.Add(new() { EntityId = c.Id, ActorId = actor, Action = "Complaint" + kind, After = JsonSerializer.Serialize(new { c.Id, entryId = entry.Id, c.Version, c.State, c.Outcome, c.Mode }), OccurredAt = now });
        await db.SaveChangesAsync(ct); return receipt;
    }
    public async Task<ComplaintReceipt> CreateAsync(CreateComplaintRequest request, IReadOnlyList<ImagePayload> images, Guid key, Guid actor, string role, CancellationToken ct)
    {
        await Actor(actor, role, ct); Party(role);
        var reason = Complaint.Text(request.Reason, "reason", 200); var description = Complaint.Text(request.Description, "description", 4000);
        var hash = Hash(new { op = "Create", request.TransactionId, request.Scope, reason, description, request.ReasonId, images = ImageHashes(images) });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var replay = await Replay(actor, key, hash, ct); if (replay is not null) { await tx.CommitAsync(ct); return replay; }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM post_auction_transactions WHERE id = {request.TransactionId} FOR UPDATE", ct);
        var trade = await Target(request.TransactionId, actor, role, ct);
        if (await db.Complaints.AnyAsync(c => c.TransactionId == trade.Id && c.State != ComplaintStates.DecisionRecorded, ct)) throw new WorkflowConflict("ACTIVE_COMPLAINT_EXISTS", "Đã có hồ sơ đang mở cho giao dịch; hãy gửi giải trình tại hồ sơ đó.");
        var now = await Now(ct);
        if(trade.PolicyProfile is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(7360021)",ct);
            var catalog=await db.WorkflowCatalogItems.AsNoTracking().SingleOrDefaultAsync(i=>i.Id==request.ReasonId&&i.Kind=="ComplaintReason"&&i.Active,ct)??throw new WorkflowConflict("COMPLAINT_REASON_REQUIRED","Chọn lý do đang hoạt động từ danh mục dùng chung.");
            reason=catalog.Name;
        }
        var row = Complaint.Create(trade.Id, actor, actor == trade.FarmerId ? trade.EnterpriseId : trade.FarmerId, request.Scope, trade.Title, reason, description, images.Count, now);
        if(trade.PolicyProfile is not null)
        {
            row.UseCompletionPolicy(request.ReasonId!.Value,trade.CompletedAt,now);
            var tracked=await db.PostAuctionTransactions.SingleAsync(t=>t.Id==trade.Id,ct);tracked.RecordDispute("Open");
            db.TransactionHistory.Add(new(){TransactionId=tracked.Id,ActorId=actor,Kind="DisputeOpened",State=tracked.State,Version=tracked.Version,OccurredAt=now});
        }
        db.Complaints.Add(row);
        var result = await Append(row, "Created", description, images, key, hash, actor, role, now, ct); await tx.CommitAsync(ct); return result;
    }
    private async Task<ComplaintReceipt> Change(Guid id, long version, string? rawText, string kind, string? outcome, bool confirmed, IReadOnlyList<ImagePayload> images, Guid key, Guid actor, string role, CancellationToken ct)
    {
        await Actor(actor, role, ct); if (kind == "ResponseAdded") Party(role); else Admin(role);
        var text = Complaint.Text(rawText, "text", 4000);
        var imageHashes = kind == "ResponseAdded" ? ImageHashes(images) : [];
        var hash = Hash(new { op = kind, id, version, text, outcome, confirmed, images = imageHashes });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var replay = await Replay(actor, key, hash, ct); if (replay is not null) { await tx.CommitAsync(ct); return replay; }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM complaints WHERE id = {id} FOR UPDATE", ct);
        var row = await Scoped(actor, role).SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ResourceMissing();
        var now = await Now(ct);
        switch (kind)
        {
            case "ResponseAdded": row.Respond(actor, version, images.Count, now); break;
            case "ReviewStarted": row.StartReview(version, now); break;
            case "AdminComment": row.Comment(version, now); break;
            case "DecisionRecorded": row.Decide(version, outcome, confirmed, now); break;
            default: throw new InvalidOperationException();
        }
        if(kind=="DecisionRecorded"&&row.PolicyProfile is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM post_auction_transactions WHERE id={row.TransactionId} FOR UPDATE",ct);
            var trade=await db.PostAuctionTransactions.SingleAsync(t=>t.Id==row.TransactionId,ct);trade.RecordDispute(outcome switch{"Accept"=>"Accepted","Reject"=>"Rejected",_=>"MediationPending"});
            db.TransactionHistory.Add(new(){TransactionId=trade.Id,ActorId=actor,Kind="DisputeDecisionRecorded",State=trade.State,Version=trade.Version,OccurredAt=now,Note=text});
        }
        var result = await Append(row, kind, text, images, key, hash, actor, role, now, ct); await tx.CommitAsync(ct); return result;
    }
    public Task<ComplaintReceipt> RespondAsync(Guid id, ComplaintResponseRequest request, IReadOnlyList<ImagePayload> images, Guid key, Guid actor, string role, CancellationToken ct) => Change(id, request.ExpectedVersion, request.Text, "ResponseAdded", null, false, images, key, actor, role, ct);
    public Task<ComplaintReceipt> ReviewAsync(Guid id, ComplaintReviewRequest request, Guid key, Guid actor, string role, CancellationToken ct) => Change(id, request.ExpectedVersion, request.Text, "ReviewStarted", null, false, [], key, actor, role, ct);
    public Task<ComplaintReceipt> CommentAsync(Guid id, ComplaintReviewRequest request, Guid key, Guid actor, string role, CancellationToken ct) => Change(id, request.ExpectedVersion, request.Text, "AdminComment", null, false, [], key, actor, role, ct);
    public Task<ComplaintReceipt> DecideAsync(Guid id, ComplaintDecisionRequest request, Guid key, Guid actor, string role, CancellationToken ct) => Change(id, request.ExpectedVersion, request.Text, "DecisionRecorded", request.Outcome, request.ConfirmedSimulation, [], key, actor, role, ct);
}
