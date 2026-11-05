using AgriAuction.Application.Identity;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignInResult = AgriAuction.Application.Identity.SignInResult;

namespace AgriAuction.Infrastructure.Identity;

public sealed class IdentityService(AppDbContext db, IPasswordHasher<Account> hasher) : IIdentityService
{
    public async Task<SignInResult?> SignInAsync(string? email, string? password, int lifetimeMinutes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || string.IsNullOrEmpty(password) || password.Length > 1024)
            return null;
        var normalized = email.Trim().ToUpperInvariant();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE normalized_email={normalized} FOR UPDATE",ct);
        var account = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
        if (account is null || !account.IsEnabled) return null;
        var result = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded) account.PasswordHash = hasher.HashPassword(account, password);
        var session = new LoginSession { AccountId = account.Id, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(lifetimeMinutes) };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(new(account.Id, account.Email, account.DisplayName, account.Role, account.ApprovalState, account.ApprovalVersion), session.Id, session.ExpiresAt);
    }

    public Task<UserView> GetUserAsync(Guid id, CancellationToken ct) => db.Users.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new UserView(x.Id, x.Email, x.DisplayName, x.Role, x.ApprovalState, x.ApprovalVersion)).SingleAsync(ct);

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.SingleAsync(x => x.Id == sessionId, ct);
        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
