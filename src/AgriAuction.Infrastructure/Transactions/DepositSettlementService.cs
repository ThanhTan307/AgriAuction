using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Transactions;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Transactions;

public sealed class DepositSettlementService(AppDbContext db, CompletionProfile profile) : IDepositSettlementService
{
    private async Task Admin(Guid actor, CancellationToken ct)
    {
        if (!profile.Enabled || !await db.Users.AnyAsync(u=>u.Id==actor&&u.Role==Roles.Administrator&&u.IsEnabled,ct)) throw new PermissionDenied();
    }
    public async Task<IReadOnlyList<SettlementCandidate>> ListAsync(Guid actor,CancellationToken ct)
    {
        await Admin(actor,ct);
        return await (from d in db.SimulationDeposits.AsNoTracking() join r in db.AuctionRegistrations on d.RegistrationId equals r.Id
            join a in db.Auctions on r.AuctionId equals a.Id
            join enterprise in db.Users on r.EnterpriseId equals enterprise.Id
            where a.PolicyProfile==CompletionProfile.Version && d.State=="SimulatedHeld"
            orderby d.CreatedAt descending
            select new SettlementCandidate(d.Id,a.Id,r.EnterpriseId,d.Amount,d.Currency,
                db.LotRevisions.Where(v=>v.Id==a.RevisionId).Select(v=>v.Content.Title).First(),
                db.SimulationRooms.Where(room=>room.AuctionId==a.Id).Select(room=>room.State).FirstOrDefault(),
                db.PostAuctionTransactions.Where(t=>t.AuctionId==a.Id).Select(t=>t.State).FirstOrDefault(),
                db.SimulationResults.Any(t=>t.WinnerId==r.EnterpriseId&&db.SimulationRooms.Any(room=>room.Id==t.RoomId&&room.AuctionId==a.Id)),
                db.DepositSettlements.Where(s=>s.DepositId==d.Id).Select(s=>s.Kind).FirstOrDefault(),
                enterprise.DisplayName,
                db.PostAuctionTransactions.Where(t=>t.AuctionId==a.Id).Select(t=>(DateTimeOffset?)t.ClosedAt).FirstOrDefault(),
                db.PostAuctionTransactions.Where(t=>t.AuctionId==a.Id).Select(t=>t.CompletedAt).FirstOrDefault(),
                db.PostAuctionTransactions.Where(t=>t.AuctionId==a.Id).Select(t=>t.CompletedAt ?? (DateTimeOffset?)t.ClosedAt).FirstOrDefault())).Take(100).ToListAsync(ct);
    }
    public async Task<DepositDispositionReceipt> ExecuteAsync(Guid id,string action,DepositDispositionRequest request,Guid key,Guid actor,CancellationToken ct)
    {
        await Admin(actor,ct);
        if(key==Guid.Empty || action is not ("Refund" or "RecordViolation" or "Forfeit")) throw new WorkflowConflict("SETTLEMENT_COMMAND_INVALID","Cần thao tác hợp lệ và UUID.");
        if(action=="RecordViolation"&&(string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>2000||string.IsNullOrWhiteSpace(request.EvidenceReference)||request.EvidenceReference.Length>1000))
            throw new WorkflowConflict("VIOLATION_BASIS_REQUIRED","Quyết định phải có lý do và tham chiếu chứng cứ.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,action,request}))));
        var lk=System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"settlement:{actor}:{key}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lk})",ct);
        var auctionId=await (from d in db.SimulationDeposits join r in db.AuctionRegistrations on d.RegistrationId equals r.Id where d.Id==id select r.AuctionId).SingleOrDefaultAsync(ct);
        if(auctionId==Guid.Empty)throw new ResourceMissing();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM auctions WHERE id={auctionId} FOR UPDATE",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM simulation_deposits WHERE id={id} FOR UPDATE",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id={actor} FOR SHARE",ct);await Admin(actor,ct);
        var previous=await db.DepositSettlementCommands.AsNoTracking().SingleOrDefaultAsync(c=>c.ActorId==actor&&c.RequestId==key,ct);
        if(previous is not null){if(previous.PayloadHash!=hash)throw new WorkflowConflict("IDEMPOTENCY_CONFLICT","Khóa đã dùng với nội dung khác.");return JsonSerializer.Deserialize<DepositDispositionReceipt>(previous.ReceiptJson)!;}
        var deposit=await db.SimulationDeposits.SingleAsync(d=>d.Id==id,ct);
        var reg=await db.AuctionRegistrations.SingleAsync(r=>r.Id==deposit.RegistrationId,ct);
        var auction=await db.Auctions.SingleAsync(a=>a.Id==auctionId,ct);
        var result=await db.SimulationResults.AsNoTracking().SingleOrDefaultAsync(r=>db.SimulationRooms.Any(room=>room.Id==r.RoomId&&room.AuctionId==auctionId),ct);
        if(auction.PolicyProfile!=CompletionProfile.Version||result is null||deposit.State!="SimulatedHeld"||deposit.Currency!="VND"
            ||!await db.SimulationLedger.AnyAsync(l=>l.DepositId==id&&l.Kind=="SimulatedConfirmation"&&l.Amount==deposit.Amount&&l.Currency==deposit.Currency,ct))
            throw new WorkflowConflict("SETTLEMENT_NOT_ELIGIBLE","Chỉ xử lý cọc đã xác nhận của phiên Completion đã chốt.");
        if(await db.DepositSettlements.AnyAsync(s=>s.DepositId==id,ct))throw new WorkflowConflict("DEPOSIT_ALREADY_SETTLED","Nghĩa vụ cọc đã được xử lý.");
        var now=await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        Guid recordId;
        if(action=="RecordViolation")
        {
            var decision=new DepositViolationDecision{DepositId=id,ActorId=actor,Reason=request.Reason!.Trim(),EvidenceReference=request.EvidenceReference!.Trim(),OccurredAt=now};db.DepositViolationDecisions.Add(decision);recordId=decision.Id;
        }
        else
        {
            if(action=="Refund"&&result.WinnerId==reg.EnterpriseId&&!await db.PostAuctionTransactions.AnyAsync(t=>t.AuctionId==auctionId&&t.State=="Completed"&&t.CompletedAt!=null,ct))
                throw new WorkflowConflict("WINNER_NOT_COMPLETED","Chỉ hoàn cọc người thắng sau Completed.");
            if(action=="Forfeit"&&!await db.DepositViolationDecisions.AnyAsync(d=>d.DepositId==id&&d.Id==request.DecisionId&&d.OccurredAt<=now,ct))
                throw new WorkflowConflict("VIOLATION_DECISION_REQUIRED","Phải có quyết định vi phạm đã lưu cho đúng nghĩa vụ cọc.");
            var settlement=new DepositSettlement{DepositId=id,ActorId=actor,DecisionId=action=="Forfeit"?request.DecisionId:null,Kind=action=="Refund"?"SimulatedRefunded":"SimulatedForfeited",Amount=deposit.Amount,Currency=deposit.Currency,OccurredAt=now};db.DepositSettlements.Add(settlement);recordId=settlement.Id;
        }
        var receipt=new DepositDispositionReceipt(id,recordId,action,deposit.Amount,deposit.Currency,now);var json=JsonSerializer.Serialize(receipt);
        var command=new DepositSettlementCommand{DepositId=id,ActorId=actor,RequestId=key,PayloadHash=hash,ReceiptJson=json,OccurredAt=now};db.DepositSettlementCommands.Add(command);
        db.AuditLogs.Add(new(){ActorId=actor,EntityId=id,Action="Deposit"+action,After=json,OccurredAt=now});
        await db.SaveChangesAsync(ct);
        db.Notifications.Add(new(){RecipientId=reg.EnterpriseId,EventId=command.Id,AuctionId=auctionId,Kind="DepositSettlementRecorded",Title="Xử lý cọc mô phỏng",Message=$"{action}: {deposit.Amount} {deposit.Currency}. Cọc độc lập tiền hàng; không có tiền thật.",CreatedAt=now});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return receipt;
    }
}
