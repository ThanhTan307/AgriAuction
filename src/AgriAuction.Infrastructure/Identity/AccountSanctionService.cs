using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriAuction.Application.Identity;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AgriAuction.Infrastructure.Identity;
public sealed class AccountSanctionService(AppDbContext db):IAccountSanctionService
{
    public async Task<IReadOnlyList<AccountSanctionView>> ListAsync(Guid id,Guid actor,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(u=>u.Id==actor&&u.Role=="Administrator"&&u.IsEnabled,ct))throw new PermissionDenied();
        return await db.AccountSanctions.AsNoTracking().Where(s=>s.AccountId==id).OrderByDescending(s=>s.Version).Take(100).Select(s=>new AccountSanctionView(s.Id,s.AccountId,s.Kind,s.Reason,s.Until,s.Version,s.OccurredAt)).ToListAsync(ct);
    }
    private static AccountSanctionView View(AccountSanction s)=>new(s.Id,s.AccountId,s.Kind,s.Reason,s.Until,s.Version,s.OccurredAt);
    public async Task<AccountSanctionView> ApplyAsync(Guid id,AccountSanctionRequest r,Guid key,Guid actor,CancellationToken ct)
    {
        if(key==Guid.Empty||r.Kind is not ("SuspendBidding" or "LiftBiddingSuspension")||string.IsNullOrWhiteSpace(r.Reason)||r.Reason.Length>2000)throw new WorkflowConflict("SANCTION_BASIS_REQUIRED","Cần thao tác, UUID và lý do chế tài.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,r}))));var lk=System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"sanction:{actor}:{key}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lk})",ct);
        var decision=await db.ComplaintEntries.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==r.DecisionEntryId&&e.Kind=="DecisionRecorded"&&e.Outcome=="Accept",ct)??throw new WorkflowConflict("SANCTION_DECISION_REQUIRED","Phải có quyết định chấp nhận khiếu nại đã lưu.");
        var complaint=await db.Complaints.AsNoTracking().SingleAsync(c=>c.Id==decision.ComplaintId,ct);
        if(complaint.PolicyProfile is null||complaint.RespondentId!=id)throw new WorkflowConflict("SANCTION_SCOPE_REJECTED","Chế tài chỉ áp dụng bên bị kết luận trong đúng hồ sơ Completion.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id={id} FOR UPDATE",ct);
        if(!await db.Users.AnyAsync(u=>u.Id==actor&&u.Role=="Administrator"&&u.IsEnabled,ct))throw new PermissionDenied();
        var previous=await db.AccountSanctions.AsNoTracking().SingleOrDefaultAsync(s=>s.ActorId==actor&&s.RequestId==key,ct);
        if(previous is not null){if(previous.PayloadHash!=hash)throw new WorkflowConflict("IDEMPOTENCY_CONFLICT","Khóa đã dùng với nội dung khác.");return View(previous);}
        var a=await db.Users.SingleOrDefaultAsync(a=>a.Id==id&&a.Role!="Administrator",ct)??throw new ResourceMissing();
        if(a.RestrictionVersion!=r.ExpectedVersion)throw new WorkflowConflict("VERSION_CONFLICT","Chế tài tài khoản đã thay đổi.");
        var now=await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        if(r.Kind=="SuspendBidding"&&(r.Until is null||r.Until<=now))throw new WorkflowConflict("SANCTION_UNTIL_REQUIRED","Admin phải nhập mốc kết thúc tạm khóa ở tương lai; không có hạn mặc định.");
        a.BiddingSuspendedUntil=r.Kind=="SuspendBidding"?r.Until:null;a.RestrictionVersion++;
        var entry=new AccountSanction{AccountId=id,ActorId=actor,DecisionEntryId=r.DecisionEntryId,RequestId=key,PayloadHash=hash,Kind=r.Kind,Reason=r.Reason.Trim(),Until=a.BiddingSuspendedUntil,Version=a.RestrictionVersion,OccurredAt=now};db.AccountSanctions.Add(entry);
        db.AuditLogs.Add(new(){ActorId=actor,EntityId=id,Action=r.Kind,After=JsonSerializer.Serialize(View(entry)),OccurredAt=now});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(entry);
    }
}
