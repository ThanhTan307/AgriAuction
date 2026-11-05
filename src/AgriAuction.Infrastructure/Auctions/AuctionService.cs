using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Auctions;
using AgriAuction.Application.Transactions;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Auctions;

public sealed class AuctionService(AppDbContext db, IDepositSimulationProvider provider, CompletionProfile? completion = null) : IAuctionService
{
    private async Task<DateTimeOffset> Now(CancellationToken ct)
        => await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private async Task Account(Guid actor, string role, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {actor} FOR SHARE", ct);
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.Role == role && x.IsEnabled, ct)) throw new PermissionDenied();
        if(completion?.Enabled==true&&role!=Roles.Administrator&&!await db.Users.AnyAsync(x=>x.Id==actor&&x.ApprovalState=="Approved",ct))throw new WorkflowConflict("ACCOUNT_APPROVAL_REQUIRED","Tài khoản chưa được Admin duyệt.");
        if(completion?.Enabled==true){var now=await Now(ct);if(await db.Users.AnyAsync(x=>x.Id==actor&&x.BiddingSuspendedUntil!=null&&x.BiddingSuspendedUntil>now,ct))throw new WorkflowConflict("BIDDING_SUSPENDED","Quyền tham gia đấu giá đang tạm khóa theo quyết định.");}
    }
    private async Task<Lot> ValidLot(Guid id, Guid? revision, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM lots WHERE id = {id} FOR UPDATE", ct);
        var lot = await db.Lots.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissing();
        if (lot.State != LotStates.Approved || lot.CurrentRevisionId is null || (revision.HasValue && lot.CurrentRevisionId != revision))
            throw new WorkflowConflict("LOT_NOT_APPROVED", "Lô không còn đúng hồ sơ được duyệt.");
        _ = lot.Content.Validated();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM categories WHERE id = {lot.Content.CategoryId} FOR SHARE", ct);
        if (!await db.Categories.AnyAsync(x => x.Id == lot.Content.CategoryId && x.Active, ct))
            throw new WorkflowConflict("LOT_CATEGORY_INACTIVE", "Danh mục lô đã ngừng sử dụng.");
        await Account(lot.FarmerId, Roles.Farmer, ct);
        if (!await db.LotReviews.AnyAsync(x => x.LotId == id && x.RevisionId == lot.CurrentRevisionId && x.Outcome == "Approve", ct)
            || !await db.RevisionImages.AnyAsync(x => x.RevisionId == lot.CurrentRevisionId, ct))
            throw new WorkflowConflict("LOT_NOT_APPROVED", "Thiếu quyết định duyệt hoặc ảnh hồ sơ.");
        return lot;
    }
    private async Task<Auction> LockedAuction(Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM auctions WHERE id = {id} FOR UPDATE", ct);
        return await db.Auctions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissing();
    }
    private void Audit(Guid id, Guid actor, string action, object after, object? before = null) => db.AuditLogs.Add(new()
        { EntityId = id, ActorId = actor, Action = action, Before = before is null ? null : JsonSerializer.Serialize(before), After = JsonSerializer.Serialize(after) });
    private static void Page(int page, int size, string? query = null)
    {
        if (page is < 1 or > 1000000 || size is < 1 or > 100 || query?.Length > 160)
            throw new RuleViolation(new() { ["query"] = ["Trang 1–1000000; kích thước 1–100; tìm kiếm tối đa 160 ký tự."] });
    }
    public async Task<IReadOnlyList<ApprovedLotOption>> ApprovedLotsAsync(CancellationToken ct)
        => await db.Lots.AsNoTracking().Where(x => x.State == LotStates.Approved && x.CurrentRevisionId != null && !db.Auctions.Any(a => a.LotId == x.Id))
            .Join(db.Users.Where(x => x.Role == Roles.Farmer && x.IsEnabled), x => x.FarmerId, u => u.Id, (lot, user) => new { lot, user })
            .Where(x => db.Categories.Any(c => c.Id == x.lot.Content.CategoryId && c.Active))
            .OrderByDescending(x => x.lot.UpdatedAt).Take(100)
            .Select(x => new ApprovedLotOption(x.lot.Id, x.lot.Content.Title, x.lot.Version, x.lot.CurrentRevisionId!.Value, x.user.DisplayName)).ToListAsync(ct);
    public async Task<AuctionView> PrepareAsync(PrepareAuctionRequest request, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lot = await ValidLot(request.LotId, null, ct); lot.Expect(request.ExpectedLotVersion);
        await Account(actor, Roles.Administrator, ct);
        if (await db.Auctions.AnyAsync(x => x.LotId == lot.Id, ct)) throw new WorkflowConflict("AUCTION_ALREADY_PREPARED", "Lô đã có phiên. Tái đấu giá chưa thuộc Phase 3.");
        var auction = Auction.Prepare(lot, actor, request.Terms, await Now(ct));
        if (completion?.Enabled == true)
        {
            if (lot.Content.Currency != "VND" || lot.Content.PriceBasis != CompletionProfile.PriceBasis)
                throw new WorkflowConflict("PROFILE_PRICE_REJECTED", "Profile mới yêu cầu VND và cơ sở giá whole lot simulation (toàn bộ lô).");
            auction.UseCompletionProfile(CompletionProfile.Version);
        }
        db.Auctions.Add(auction); Audit(auction.Id, actor, "AuctionSimulationPrepared", new { auction.LotId, auction.RevisionId, auction.State, auction.Terms, mode = "Simulation" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetAsync(auction.Id, true, ct);
    }
    public async Task<AuctionView> PublishAsync(Guid id, long version, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var auction = await LockedAuction(id, ct); auction.Expect(version);
        await ValidLot(auction.LotId, auction.RevisionId, ct); await Account(actor, Roles.Administrator, ct);
        auction.Publish(actor, await Now(ct));
        Audit(id, actor, "AuctionSimulationPublished", new { auction.State, auction.Version, auction.PublishedAt, mode = "Simulation" }, new { State = "Draft", Version = version });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetAsync(id, true, ct);
    }
    public async Task<AuctionPage> ListAsync(bool admin, string? query, int page, int pageSize, CancellationToken ct,Guid? categoryId=null,string? location=null,string? state=null)
    {
        Page(page, pageSize, query);
        var rows = db.Auctions.AsNoTracking().Where(x => admin || x.State == "Scheduled");
        if(location?.Length>160||state is not (null or "Scheduled" or "Prepared" or "Open" or "ClosedWithWinner" or "ClosedNoWinner"))throw new RuleViolation(new(){["filter"]=["Bộ lọc trạng thái/địa phương không hợp lệ."]});
        if(categoryId is not null)rows=rows.Where(a=>db.LotRevisions.Any(r=>r.Id==a.RevisionId&&r.Content.CategoryId==categoryId));
        if(!string.IsNullOrWhiteSpace(location))rows=rows.Where(a=>db.LotRevisions.Any(r=>r.Id==a.RevisionId&&r.Content.Location.Contains(location.Trim())));
        if(state is not null)rows=state=="Scheduled"?rows.Where(a=>!db.SimulationRooms.Any(r=>r.AuctionId==a.Id)):rows.Where(a=>db.SimulationRooms.Any(r=>r.AuctionId==a.Id&&r.State==state));
        if (!string.IsNullOrWhiteSpace(query)) rows = rows.Where(x => db.LotRevisions.Any(r => r.Id == x.RevisionId && r.Content.Title.Contains(query.Trim())));
        var total = await rows.CountAsync(ct);
        var ids = await rows.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToListAsync(ct);
        var items = new List<AuctionView>(); foreach (var id in ids) items.Add(await GetAsync(id, admin, ct));
        return new(items, total, page, pageSize);
    }
    public async Task<AuctionPage> OwnerAsync(Guid actor,int page,int pageSize,CancellationToken ct)
    {
        Page(page,pageSize);if(!await db.Users.AnyAsync(u=>u.Id==actor&&u.Role==Roles.Farmer&&u.IsEnabled,ct))throw new PermissionDenied();
        var query=db.Auctions.AsNoTracking().Where(a=>a.FarmerId==actor);var count=await query.CountAsync(ct);var ids=await query.OrderByDescending(a=>a.CreatedAt).ThenBy(a=>a.Id).Skip((page-1)*pageSize).Take(pageSize).Select(a=>a.Id).ToListAsync(ct);
        var items=new List<AuctionView>();foreach(var id in ids)items.Add(await GetAsync(id,true,ct));return new(items,count,page,pageSize);
    }
    public async Task<AuctionView> GetAsync(Guid id, bool admin, CancellationToken ct)
    {
        var auction = await db.Auctions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.State == "Scheduled"), ct) ?? throw new ResourceMissing();
        var revision = await db.LotRevisions.AsNoTracking().SingleAsync(x => x.Id == auction.RevisionId, ct);
        var farmerName = await db.Users.Where(x => x.Id == auction.FarmerId).Select(x => x.DisplayName).SingleAsync(ct);
        var categoryName = await db.Categories.Where(x => x.Id == revision.Content.CategoryId).Select(x => x.Name).SingleAsync(ct);
        var images = await db.RevisionImages.Where(x => x.RevisionId == auction.RevisionId).Join(db.LotImages, x => x.ImageId, x => x.Id,
            (link, image) => new ImageView(image.Id, $"/api/v1/auctions/{id}/images/{image.Id}", image.Width, image.Height, image.UploadedAt)).ToListAsync(ct);
        var now = await Now(ct);
        var available = auction.State == "Scheduled" && now >= auction.Terms.RegistrationOpensAt && now < auction.Terms.RegistrationClosesAt
            && await db.Lots.AnyAsync(x => x.Id == auction.LotId && x.State == LotStates.Approved && x.CurrentRevisionId == auction.RevisionId, ct)
            && await db.Users.AnyAsync(x => x.Id == auction.FarmerId && x.IsEnabled && x.Role == Roles.Farmer, ct)
            && await db.Categories.AnyAsync(x => x.Id == revision.Content.CategoryId && x.Active, ct);
        return new(id, auction.LotId, auction.RevisionId, auction.FarmerId, farmerName, categoryName, auction.State, auction.IsSimulation, auction.Version,
            auction.Terms, revision.Content, auction.CreatedAt, auction.PublishedAt, available, images);
    }
    public async Task<byte[]> ImageAsync(Guid id, Guid imageId, bool admin, CancellationToken ct)
    {
        var revision = await db.Auctions.Where(x => x.Id == id && (admin || x.State == "Scheduled")).Select(x => (Guid?)x.RevisionId).SingleOrDefaultAsync(ct) ?? throw new ResourceMissing();
        return await db.RevisionImages.Where(x => x.RevisionId == revision && x.ImageId == imageId).Join(db.LotImages, x => x.ImageId, x => x.Id, (link, image) => image.Data).SingleOrDefaultAsync(ct) ?? throw new ResourceMissing();
    }
    public async Task<RegistrationView> RegisterAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var auction = await LockedAuction(id, ct);
        await ValidLot(auction.LotId, auction.RevisionId, ct); await Account(actor, Roles.Enterprise, ct);
        var now = await Now(ct); // Recheck database wall clock AFTER waiting for locks.
        auction.RequireRegistrationWindow(now);
        if (await db.AuctionRegistrations.AnyAsync(x => x.AuctionId == id && x.EnterpriseId == actor, ct))
            throw new WorkflowConflict("DUPLICATE_REGISTRATION", "Tài khoản đã đăng ký phiên này.");
        var registration = AuctionRegistration.Register(auction, actor, now);
        db.AuctionRegistrations.Add(registration);
        var deposit = new SimulationDeposit { RegistrationId = registration.Id, Amount = auction.Terms.DepositAmount, Currency = auction.Terms.Currency, CreatedAt = now };
        db.SimulationDeposits.Add(deposit);
        db.RegistrationEvents.Add(new() { RegistrationId = registration.Id, State = registration.State, ActorId = actor, OccurredAt = now });
        Audit(registration.Id, actor, "AuctionRegisteredSimulation", new { registration.AuctionId, registration.EnterpriseId, registration.State, deposit.Id, deposit.Amount, deposit.Currency, deposit.Mode });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await RegistrationAsync(registration.Id, actor, false, ct);
    }
    public async Task<RegistrationView?> MyRegistrationAsync(Guid auctionId, Guid actor, CancellationToken ct)
    {
        var id = await db.AuctionRegistrations.Where(x => x.AuctionId == auctionId && x.EnterpriseId == actor).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return id.HasValue ? await RegistrationAsync(id.Value, actor, false, ct) : null;
    }
    public async Task<RegistrationPage> RegistrationsAsync(Guid actor, bool admin, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        var rows = db.AuctionRegistrations.AsNoTracking().Where(x => admin || x.EnterpriseId == actor);
        var total = await rows.CountAsync(ct);
        var ids = await rows.OrderByDescending(x => x.RegisteredAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToListAsync(ct);
        var items = new List<RegistrationView>(); foreach (var id in ids) items.Add(await RegistrationAsync(id, actor, admin, ct));
        return new(items, total, page, pageSize);
    }
    public async Task<RegistrationView> RegistrationAsync(Guid id, Guid actor, bool admin, CancellationToken ct)
    {
        // Read state/deposit/history from one PostgreSQL snapshot while an Admin
        // may be confirming concurrently; never return a mixed state.
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var r = await db.AuctionRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.EnterpriseId == actor), ct) ?? throw new ResourceMissing();
        var d = await db.SimulationDeposits.AsNoTracking().SingleAsync(x => x.RegistrationId == id, ct);
        var title = await db.Auctions.Where(x => x.Id == r.AuctionId).Join(db.LotRevisions, x => x.RevisionId, x => x.Id, (a, revision) => revision.Content.Title).SingleAsync(ct);
        var name = await db.Users.Where(x => x.Id == r.EnterpriseId).Select(x => x.DisplayName).SingleAsync(ct);
        var reference = await db.SimulationLedger.Where(x => x.DepositId == d.Id).Select(x => x.Reference).SingleOrDefaultAsync(ct);
        var settlement = completion?.Enabled == true
            ? await db.DepositSettlements.AsNoTracking().SingleOrDefaultAsync(x => x.DepositId == d.Id, ct) : null;
        var history = await db.RegistrationEvents.Where(x => x.RegistrationId == id).OrderBy(x => x.OccurredAt).Select(x => new RegistrationEventView(x.Id, x.State, x.ActorId, x.OccurredAt)).ToListAsync(ct);
        var result = new RegistrationView(r.Id, r.AuctionId, title, r.EnterpriseId, name, r.State, r.Version, r.RegisteredAt, r.EligibleAt,
            new(d.Id, d.Mode, d.State, d.Amount, d.Currency, d.ConfirmedAt, d.ConfirmedBy, reference, settlement?.Kind, settlement?.OccurredAt), history);
        await snapshot.CommitAsync(ct);
        return result;
    }
    public async Task<SimulationConfirmation> ConfirmAsync(Guid id, Guid requestId, ConfirmSimulationRequest request, Guid actor, CancellationToken ct)
    {
        if (requestId == Guid.Empty) throw new RuleViolation(new() { ["idempotencyKey"] = ["Idempotency-Key phải là UUID khác rỗng."] });
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is < 1 or > 2000 || reason.Any(c => char.IsControl(c))) throw new RuleViolation(new() { ["reason"] = ["Ghi lý do mô phỏng 1–2000 ký tự, không ký tự điều khiển."] });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, request.ExpectedVersion, reason }))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Transaction-scoped PostgreSQL lock, shared across processes. It protects
        // the actor/key even when concurrent calls target different auctions.
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(actor.ToString("N") + requestId.ToString("N")));
        var key = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(keyBytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        await Account(actor, Roles.Administrator, ct);
        var previous = await db.DepositCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor && x.RequestId == requestId, ct);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) throw new WorkflowConflict("IDEMPOTENCY_CONFLICT", "Khóa đã dùng với nội dung khác.");
            return JsonSerializer.Deserialize<SimulationConfirmation>(previous.ResponseJson)!;
        }
        var auctionId = await db.AuctionRegistrations.Where(x => x.Id == id).Select(x => (Guid?)x.AuctionId).SingleOrDefaultAsync(ct) ?? throw new ResourceMissing();
        var auction = await LockedAuction(auctionId, ct);
        await ValidLot(auction.LotId, auction.RevisionId, ct);
        var registration = await db.AuctionRegistrations.SingleAsync(x => x.Id == id, ct);
        await Account(registration.EnterpriseId, Roles.Enterprise, ct);
        var now = await Now(ct); auction.RequireRegistrationWindow(now);
        registration.ConfirmSimulation(request.ExpectedVersion, now);
        var deposit = await db.SimulationDeposits.SingleAsync(x => x.RegistrationId == id, ct);
        if (deposit.State != "AwaitingSimulation" || deposit.Mode != "Simulation") throw new WorkflowConflict("DEPOSIT_STATE_CONFLICT", "Cọc không ở trạng thái chờ mô phỏng.");
        var receipt = provider.Confirm(new(deposit.Id, deposit.Amount, deposit.Currency));
        deposit.State = "SimulatedHeld"; deposit.ConfirmedAt = now; deposit.ConfirmedBy = actor;
        var entry = new SimulationLedgerEntry { DepositId = deposit.Id, ActorId = actor, Amount = deposit.Amount, Currency = deposit.Currency,
            Provider = receipt.Provider, Reference = receipt.Reference, OccurredAt = now };
        db.SimulationLedger.Add(entry);
        db.RegistrationEvents.Add(new() { RegistrationId = id, State = registration.State, ActorId = actor, OccurredAt = now });
        var result = new SimulationConfirmation(id, deposit.Id, entry.Id, registration.State, deposit.State, registration.Version, "Simulation", receipt.Reference, now);
        db.DepositCommands.Add(new() { ActorId = actor, RequestId = requestId, RegistrationId = id, PayloadHash = hash, ResponseJson = JsonSerializer.Serialize(result), CompletedAt = now });
        Audit(id, actor, "DepositSimulationConfirmed", new { result, requestId, reason }, new { registrationState = "PendingDeposit", depositState = "AwaitingSimulation", Version = request.ExpectedVersion });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }
}
