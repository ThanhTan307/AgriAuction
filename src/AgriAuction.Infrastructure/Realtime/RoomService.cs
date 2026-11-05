using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Realtime;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Realtime;

public sealed class RoomService(AppDbContext db,CompletionProfile? completion=null) : IRoomService
{
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private async Task Account(Guid actor, string role, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {actor} FOR SHARE", ct);
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.Role == role && x.IsEnabled, ct)) throw new PermissionDenied();
        if(completion?.Enabled==true&&role!=Roles.Administrator&&!await db.Users.AnyAsync(x=>x.Id==actor&&x.ApprovalState=="Approved",ct))throw new WorkflowConflict("ACCOUNT_APPROVAL_REQUIRED","Tài khoản chưa được Admin duyệt.");
        if(completion?.Enabled==true){var now=await Now(ct);if(await db.Users.AnyAsync(x=>x.Id==actor&&x.BiddingSuspendedUntil!=null&&x.BiddingSuspendedUntil>now,ct))throw new WorkflowConflict("BIDDING_SUSPENDED","Quyền tham gia đấu giá đang tạm khóa theo quyết định.");}
    }
    private async Task<Auction> LockAuction(Guid id, CancellationToken ct)
    {
        // Exactly the same parent row used by Phase 3. Bids/start/close share it.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM auctions WHERE id = {id} FOR UPDATE", ct);
        return await db.Auctions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissing();
    }
    private async Task ValidateAuction(Auction auction, CancellationToken ct)
    {
        if (!auction.IsSimulation || auction.State != "Scheduled" || auction.PublishedAt is null) throw new WorkflowConflict("AUCTION_NOT_PUBLISHED", "Cần phiên đã công bố Simulation.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM lots WHERE id = {auction.LotId} FOR UPDATE", ct);
        var lot = await db.Lots.AsNoTracking().SingleAsync(x => x.Id == auction.LotId, ct);
        if (lot.State != LotStates.Approved || lot.CurrentRevisionId != auction.RevisionId || lot.FarmerId != auction.FarmerId) throw new WorkflowConflict("LOT_NOT_APPROVED", "Hồ sơ duyệt của phiên không còn hợp lệ.");
        await Account(lot.FarmerId, Roles.Farmer, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM categories WHERE id = {lot.Content.CategoryId} FOR SHARE", ct);
        if (!await db.Categories.AnyAsync(x => x.Id == lot.Content.CategoryId && x.Active, ct)) throw new WorkflowConflict("LOT_CATEGORY_INACTIVE", "Danh mục đã ngừng sử dụng.");
    }
    private async Task<SimulationRoom> Room(Guid auctionId, CancellationToken ct)
        => await db.SimulationRooms.SingleOrDefaultAsync(x => x.AuctionId == auctionId, ct) ?? throw new ResourceMissing();
    private Task<bool> Eligible(Guid auctionId, Guid actor, CancellationToken ct)
        => db.AuctionRegistrations.AnyAsync(r => r.AuctionId == auctionId && r.EnterpriseId == actor && r.State == "SimulationEligible"
            && db.SimulationDeposits.Any(d => d.RegistrationId == r.Id && d.Mode == "Simulation" && d.State == "SimulatedHeld"), ct);
    private void Record(SimulationRoom room, string kind, Guid? actor, DateTimeOffset at, object? before = null, object? detail = null)
    {
        var after = JsonSerializer.Serialize(new { room.State, room.Version, room.CurrentPrice, room.LeadingBidId, room.EndsAt, room.ExtensionCount, detail, mode = "Simulation" });
        db.SimulationRoomEvents.Add(new() { RoomId = room.Id, AuctionId = room.AuctionId, Version = room.Version, Kind = kind, ActorId = actor, OccurredAt = at, NextAttemptAt = at, Details = after });
        db.AuditLogs.Add(new() { EntityId = room.Id, ActorId = actor ?? Guid.Empty, Action = kind, Before = before is null ? null : JsonSerializer.Serialize(before), After = after, OccurredAt = at });
    }
    public async Task<RoomView> ConfigureAsync(Guid id, ConfigureRoomRequest request, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = await LockAuction(id, ct); a.Expect(request.ExpectedAuctionVersion); await ValidateAuction(a, ct); await Account(actor, Roles.Administrator, ct);
        if (await db.SimulationRooms.AnyAsync(x => x.AuctionId == id, ct)) throw new WorkflowConflict("ROOM_ALREADY_CONFIGURED", "Phòng đã cấu hình. Không thay quy tắc/lịch của phòng hiện có.");
        if (a.PolicyProfile is not null && (request.Rules.Currency != "VND" || request.Rules.PriceBasis != "whole lot simulation"
            || request.Rules.ExtensionRule != "AddToDeadline" || request.Rules.ExtensionWindowSeconds != 60
            || request.Rules.ExtensionSeconds != 60 || request.Rules.MaxExtensions != 3))
            throw new WorkflowConflict("PROFILE_RULES_REJECTED", "Profile D35 yêu cầu giá toàn lô VND, gia hạn 60 giây cuối thêm 60 giây, tối đa 3 lần.");
        var now = await Now(ct); var room = SimulationRoom.Configure(id, actor, request.Rules, a.Terms.StartsAt, a.Terms.EndsAt, now);
        db.SimulationRooms.Add(room); Record(room, "RoomConfigured", actor, now, detail: room.Rules);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await SnapshotAsync(id, actor, Roles.Administrator, ct);
    }
    public async Task<RoomView> StartAsync(Guid id, long version, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = await LockAuction(id, ct); await ValidateAuction(a, ct); await Account(actor, Roles.Administrator, ct);
        var room = await Room(id, ct); room.Expect(version); var before = new { room.State, room.Version }; var now = await Now(ct); room.Start(actor, now);
        Record(room, "RoomOpened", actor, now, before); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await SnapshotAsync(id, actor, Roles.Administrator, ct);
    }
    public async Task<BidReceipt> BidAsync(Guid id, Guid key, PlaceBidRequest request, Guid actor, CancellationToken ct)
    {
        if (key == Guid.Empty) throw new AgriAuction.Domain.Catalog.RuleViolation(new() { ["idempotencyKey"] = ["Cần UUID khác rỗng."] });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString("N") + ":" + request.Amount.ToString("G29", CultureInfo.InvariantCulture))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("bid:" + actor.ToString("N") + key.ToString("N"))));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        await Account(actor, Roles.Enterprise, ct);
        var previous = await db.SimulationBidCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor && x.RequestId == key, ct);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) throw new WorkflowConflict("IDEMPOTENCY_CONFLICT", "Khóa đặt giá đã dùng với nội dung khác.");
            return JsonSerializer.Deserialize<BidReceipt>(previous.ResponseJson)!;
        }
        var a = await LockAuction(id, ct); await ValidateAuction(a, ct);
        if (a.FarmerId == actor || !await Eligible(id, actor, ct)) throw new PermissionDenied();
        var room = await Room(id, ct); var before = new { room.State, room.Version, room.CurrentPrice, room.EndsAt }; var now = await Now(ct);
        var bid = room.Accept(actor, request.Amount, now); db.SimulationBids.Add(bid);
        var receipt = new BidReceipt(bid.Id, id, bid.RoomVersion, bid.Amount, bid.EndsAt, "Simulation");
        db.SimulationBidCommands.Add(new() { ActorId = actor, RequestId = key, RoomId = room.Id, PayloadHash = hash, ResponseJson = JsonSerializer.Serialize(receipt), CompletedAt = now });
        Record(room, "BidAccepted", actor, now, before, new { bid.Id, key, bid.Extended });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return receipt;
    }
    public async Task<RoomView> SnapshotAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var a = await db.Auctions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsSimulation && x.State == "Scheduled", ct) ?? throw new ResourceMissing();
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.IsEnabled && x.Role == role, ct)) throw new PermissionDenied();
        if (role == Roles.Enterprise ? !await Eligible(id, actor, ct) : role != Roles.Administrator && !(role == Roles.Farmer && a.FarmerId == actor)) throw new PermissionDenied();
        var r = await db.SimulationRooms.AsNoTracking().SingleOrDefaultAsync(x => x.AuctionId == id, ct) ?? throw new ResourceMissing();
        var revision = await db.LotRevisions.AsNoTracking().SingleAsync(x => x.Id == a.RevisionId, ct);
        var category = await db.Categories.Where(x => x.Id == revision.Content.CategoryId).Select(x => x.Name).SingleAsync(ct);
        var photoRows = await db.RevisionImages.Where(x => x.RevisionId == a.RevisionId).Join(db.LotImages, x => x.ImageId, x => x.Id, (link, image) => new { image.Id, image.Width, image.Height, image.UploadedAt }).ToListAsync(ct);
        // Stable aliases avoid exposing another Enterprise's profile or account ID.
        string Alias(Guid account) => "DN-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.Id + ":" + account)))[..12];
        var bids = await db.SimulationBids.AsNoTracking().Where(x => x.RoomId == r.Id).OrderByDescending(x => x.RoomVersion).Take(100).ToListAsync(ct);
        var result = await db.SimulationResults.AsNoTracking().SingleOrDefaultAsync(x => x.RoomId == r.Id, ct);
        var now = await Now(ct);
        var canBid = role == Roles.Enterprise && r.State == "Open" && now >= r.StartsAt && now < r.EndsAt && r.MinimumBid <= SimulationBidRules.MaxAmount
            && await db.Lots.AnyAsync(x => x.Id == a.LotId && x.State == LotStates.Approved && x.CurrentRevisionId == a.RevisionId && db.Categories.Any(c => c.Id == x.Content.CategoryId && c.Active), ct)
            && await db.Users.AnyAsync(x => x.Id == a.FarmerId && x.IsEnabled && x.Role == Roles.Farmer, ct);
        if(completion?.Enabled==true)canBid=canBid&&await db.Users.AnyAsync(u=>u.Id==actor&&u.ApprovalState=="Approved"&&(u.BiddingSuspendedUntil==null||u.BiddingSuspendedUntil<=now),ct)&&await db.Users.AnyAsync(u=>u.Id==a.FarmerId&&u.ApprovalState=="Approved"&&(u.BiddingSuspendedUntil==null||u.BiddingSuspendedUntil<=now),ct);
        var view = new RoomView(id, r.Id, revision.Content.Title, r.Mode, r.State, r.Version, now, r.StartsAt, r.EndsAt, r.CurrentPrice, r.MinimumBid,
            r.LeadingEnterpriseId.HasValue ? Alias(r.LeadingEnterpriseId.Value) : null, canBid, canBid ? "SimulationEligible" : "Hiện không được đặt giá; cần Enterprise đủ điều kiện, phòng Open và còn hạn.",
            r.BidCount, r.ExtensionCount, r.Rules, bids.Select(x => new BidView(x.Id, Alias(x.EnterpriseId), x.EnterpriseId == actor, x.Amount, x.RoomVersion, x.AcceptedAt, x.Extended)).ToList(),
            result is null ? null : new(result.WinnerId.HasValue ? Alias(result.WinnerId.Value) : null, result.WinningBidId, result.Amount, result.ClosedAt, result.RoomVersion, result.Mode),
            revision.Content, category, photoRows.Select(x => new ImageView(x.Id, role == Roles.Farmer ? $"/api/v1/media/{x.Id}" : $"/api/v1/auctions/{id}/images/{x.Id}", x.Width, x.Height, x.UploadedAt)).ToList());
        await tx.CommitAsync(ct); return view;
    }
    public async Task<IReadOnlyList<RoomHistoryItem>> HistoryAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var view = await SnapshotAsync(id, actor, role, ct);
        var events = await db.SimulationRoomEvents.AsNoTracking().Where(x => x.RoomId == view.RoomId).OrderByDescending(x => x.Version).Take(100).ToListAsync(ct);
        return events.Select(x => new RoomHistoryItem(x.Id, x.Kind, x.Version.ToString(CultureInfo.InvariantCulture), x.OccurredAt, role == Roles.Administrator ? x.ActorId : null)).ToList();
    }
    public async Task<BidPage> BidsAsync(Guid id,Guid actor,string role,long? beforeVersion,int pageSize,CancellationToken ct)
    {
        if(pageSize is <1 or >100||beforeVersion is <=0)throw new AgriAuction.Domain.Catalog.RuleViolation(new(){["page"]=["pageSize 1–100 và cursor phiên bản dương."]});
        var view=await SnapshotAsync(id,actor,role,ct);
        var rows=await db.SimulationBids.AsNoTracking().Where(b=>b.RoomId==view.RoomId&&(beforeVersion==null||b.RoomVersion<beforeVersion)).OrderByDescending(b=>b.RoomVersion).Take(pageSize+1).ToListAsync(ct);
        string Alias(Guid account)=>"DN-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(view.RoomId+":"+account)))[..12];
        var items=rows.Take(pageSize).Select(b=>new BidView(b.Id,Alias(b.EnterpriseId),b.EnterpriseId==actor,b.Amount,b.RoomVersion,b.AcceptedAt,b.Extended)).ToList();
        return new(items,rows.Count>pageSize?items[^1].Version.ToString(CultureInfo.InvariantCulture):null);
    }
    public async Task<bool> CloseDueAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAuction(id, ct);
        var room = await Room(id, ct); var before = new { room.State, room.Version }; var now = await Now(ct); var result = room.CloseIfDue(now);
        if (result is null) return false;
        db.SimulationResults.Add(result); Record(room, "RoomClosed", null, now, before, new { result.WinningBidId, source = "Worker" });
        db.PostAuctionEvents.Add(new() { RoomId = result.RoomId, CreatedAt = now, NextAttemptAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task<int> CloseDueBatchAsync(CancellationToken ct)
    {
        var now = await Now(ct);
        var ids = await db.SimulationRooms.AsNoTracking().Where(x => x.State == "Open" && x.EndsAt <= now).OrderBy(x => x.EndsAt).Take(50).Select(x => x.AuctionId).ToListAsync(ct);
        var count = 0; foreach (var id in ids) { db.ChangeTracker.Clear(); if (await CloseDueAsync(id, ct)) count++; } return count;
    }
}
