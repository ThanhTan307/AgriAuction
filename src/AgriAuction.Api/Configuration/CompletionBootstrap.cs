using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Api.Configuration;

public static class CompletionBootstrap
{
    public static async Task RunAsync(AppDbContext db, IPasswordHasher<Account> hasher, IConfiguration config)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var name in new[] { "Administrator", "Farmer", "Enterprise", "Enterprise2" })
        {
            var email = config[$"CompletionSeed:{name}:Email"] ?? "";
            var password = config[$"CompletionSeed:{name}:Password"] ?? "";
            if (!email.EndsWith("@completion.invalid", StringComparison.Ordinal) || password.Length < 24)
                throw new CompletionTargetRejected("COMPLETION_SEED_REQUIRED");
            var role = name == "Enterprise2" ? Roles.Enterprise : name;
            var normalized = email.ToUpperInvariant();
            var old = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized);
            if (old is not null)
            {
                if (old.Role != role || !old.IsEnabled || hasher.VerifyHashedPassword(old, old.PasswordHash, password) == PasswordVerificationResult.Failed)
                    throw new CompletionTargetRejected("COMPLETION_SEED_CONFLICT");
                continue;
            }
            var account = new Account { Email = email, NormalizedEmail = normalized, DisplayName = name + " Completion", Role = role };
            account.PasswordHash = hasher.HashPassword(account, password); db.Users.Add(account);
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
