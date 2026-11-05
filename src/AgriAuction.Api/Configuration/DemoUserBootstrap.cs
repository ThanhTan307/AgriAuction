using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Api.Configuration;

internal static class DemoUserBootstrap
{
    public static async Task RunAsync(AppDbContext db, IPasswordHasher<Account> hasher, IConfiguration config, IHostEnvironment environment)
    {
        if ((!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || db.Database.GetDbConnection().Database != "agri_phase1_test" || ProjectSettings.Schema(config) != "agri_demo")
            throw new InvalidOperationException("Demo user bootstrap is restricted to the isolated agri_phase1_test / agri_demo environment.");
        foreach (var role in new[] { Roles.Farmer, Roles.Enterprise })
        {
            var email = config[$"Demo:{role}:Email"] ?? "";
            var password = config[$"Demo:{role}:Password"] ?? "";
            if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || password.Length < 12)
                throw new InvalidOperationException("Provide generated demo credentials via private environment configuration.");
            var existing = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
            if (existing is not null)
            {
                if (existing.Role != role || !existing.IsEnabled || hasher.VerifyHashedPassword(existing, existing.PasswordHash, password) == PasswordVerificationResult.Failed)
                    throw new InvalidOperationException("Demo identity already exists with different credentials; refusing to overwrite it.");
                continue;
            }
            var account = new Account { Email = email, NormalizedEmail = email.ToUpperInvariant(), Role = role,
                DisplayName = role == Roles.Farmer ? "Nông dân demo" : "Doanh nghiệp demo" };
            account.PasswordHash = hasher.HashPassword(account, password);
            db.Users.Add(account);
        }
        await db.SaveChangesAsync();
        Console.WriteLine("Isolated demo users ready; no credentials printed or existing accounts overwritten.");
    }
}
