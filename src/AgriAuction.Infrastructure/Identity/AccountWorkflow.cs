using AgriAuction.Application.Identity;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Lots;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace AgriAuction.Infrastructure.Identity;

public sealed class AccountWorkflow(AppDbContext db,IPasswordHasher<Account> hasher):IAccountWorkflow
{
    private static AccountApprovalView View(Account a)=>new(a.Id,a.Email,a.DisplayName,a.Role,a.ApprovalState,a.ApprovalVersion,a.RestrictionVersion,a.BiddingSuspendedUntil);
    private static void Password(string? value){if(value is null||value.Length is <8 or >1024)throw new RuleViolation(new(){["password"]=["Mật khẩu từ 8 đến 1024 ký tự."]});}
    private Task<DateTimeOffset> Now(CancellationToken ct)=>db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private async Task Admin(Guid id,CancellationToken ct){if(!await db.Users.AnyAsync(u=>u.Id==id&&u.Role==Roles.Administrator&&u.IsEnabled,ct))throw new PermissionDenied();}
    public async Task<AccountApprovalView> RegisterAsync(RegisterAccountRequest r,CancellationToken ct)
    {
        Password(r.Password);var email=r.Email?.Trim()??"";var name=r.DisplayName?.Trim()??"";
        if(r.Role is not (Roles.Farmer or Roles.Enterprise)||email.Length>254||!System.Net.Mail.MailAddress.TryCreate(email,out var parsed)||parsed.Address!=email||name.Length is <1 or >120||name.Any(char.IsControl))
            throw new RuleViolation(new(){["account"]=["Email, tên và vai trò Farmer/Enterprise phải hợp lệ."]});
        var a=new Account{Email=email,NormalizedEmail=email.ToUpperInvariant(),DisplayName=name,Role=r.Role,ApprovalState="PendingReview"};a.PasswordHash=hasher.HashPassword(a,r.Password);
        db.Users.Add(a);
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException e) when(e.InnerException is PostgresException{SqlState:"23505"}){throw new WorkflowConflict("ACCOUNT_EXISTS","Không thể đăng ký thông tin tài khoản này.");}
        return View(a);
    }
    public async Task ChangePasswordAsync(Guid actor,ChangePasswordRequest r,CancellationToken ct)
    {
        Password(r.NewPassword);await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id={actor} FOR UPDATE",ct);
        var a=await db.Users.SingleOrDefaultAsync(u=>u.Id==actor&&u.IsEnabled,ct)??throw new PermissionDenied();
        if(string.IsNullOrEmpty(r.CurrentPassword)||r.CurrentPassword.Length>1024||hasher.VerifyHashedPassword(a,a.PasswordHash,r.CurrentPassword)==PasswordVerificationResult.Failed)
            throw new WorkflowConflict("CURRENT_PASSWORD_INVALID","Mật khẩu hiện tại không đúng.");
        var now=await Now(ct);a.PasswordHash=hasher.HashPassword(a,r.NewPassword);
        await db.Sessions.Where(s=>s.AccountId==actor&&s.RevokedAt==null).ExecuteUpdateAsync(set=>set.SetProperty(s=>s.RevokedAt,now),ct);
        db.AuditLogs.Add(new(){ActorId=actor,EntityId=actor,Action="PasswordChanged",After="{\"sessionsRevoked\":true}",OccurredAt=now});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<AccountApprovalView>> ListAsync(Guid actor,CancellationToken ct)
    {await Admin(actor,ct);return await db.Users.AsNoTracking().Where(a=>a.Role!=Roles.Administrator).OrderByDescending(a=>a.CreatedAt).Take(100).Select(a=>new AccountApprovalView(a.Id,a.Email,a.DisplayName,a.Role,a.ApprovalState,a.ApprovalVersion,a.RestrictionVersion,a.BiddingSuspendedUntil)).ToListAsync(ct);}
    public async Task<IReadOnlyList<AccountReviewView>> HistoryAsync(Guid id,Guid actor,CancellationToken ct)
    {await Admin(actor,ct);return await db.AccountReviews.AsNoTracking().Where(r=>r.AccountId==id).OrderBy(r=>r.Version).Select(r=>new AccountReviewView(r.ActorId,r.Outcome,r.Reason,r.Version,r.OccurredAt)).ToListAsync(ct);}
    public async Task<AccountApprovalView> ReviewAsync(Guid id,Guid actor,ReviewAccountRequest r,CancellationToken ct)
    {
        await Admin(actor,ct);var reason=r.Reason?.Trim()??"";
        if(r.Outcome is not ("Approve" or "Reject")||reason.Length is <1 or >2000||reason.Any(char.IsControl))throw new RuleViolation(new(){["review"]=["Chọn Approve/Reject và nhập lý do 1–2000 ký tự."]});
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id={id} FOR UPDATE",ct);
        var a=await db.Users.SingleOrDefaultAsync(a=>a.Id==id&&a.Role!=Roles.Administrator,ct)??throw new ResourceMissing();
        if(a.ApprovalVersion!=r.ExpectedVersion)throw new WorkflowConflict("VERSION_CONFLICT","Hồ sơ tài khoản đã thay đổi.");
        var next=r.Outcome=="Approve"?"Approved":"Rejected";if(a.ApprovalState==next)throw new WorkflowConflict("ACCOUNT_ALREADY_REVIEWED","Trạng thái này đã được ghi nhận.");
        var now=await Now(ct);a.ApprovalState=next;a.ApprovalVersion++;
        db.AccountReviews.Add(new(){AccountId=id,ActorId=actor,Outcome=r.Outcome,Reason=reason,Version=a.ApprovalVersion,OccurredAt=now});
        db.AuditLogs.Add(new(){ActorId=actor,EntityId=id,Action="Account"+r.Outcome,After=System.Text.Json.JsonSerializer.Serialize(new{state=next,reason,a.ApprovalVersion}),OccurredAt=now});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(a);
    }
}
