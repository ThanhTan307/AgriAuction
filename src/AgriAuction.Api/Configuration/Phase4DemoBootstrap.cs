using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Api.Configuration;

internal static class Phase4DemoBootstrap
{
    public static async Task RunAsync(AppDbContext db, IPasswordHasher<Account> hasher, IConfiguration config, IHostEnvironment env)
    {
        if ((!env.IsDevelopment() && !env.IsEnvironment("Testing")) || db.Database.GetDbConnection().Database != "agri_phase1_test" || ProjectSettings.Schema(config) != "agri_demo") throw new InvalidOperationException("Only isolated test demo bootstrap is allowed.");
        var email = config["Demo:Enterprise2:Email"] ?? ""; var password = config["Demo:Enterprise2:Password"] ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || password.Length < 12) throw new InvalidOperationException("Provide private generated Enterprise2 credentials.");
        var existing = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        if (existing is not null)
        {
            if (existing.Role != Roles.Enterprise || !existing.IsEnabled || hasher.VerifyHashedPassword(existing, existing.PasswordHash, password) == PasswordVerificationResult.Failed) throw new InvalidOperationException("Existing account differs; no account was overwritten.");
            return;
        }
        var account = new Account { Email = email, NormalizedEmail = email.ToUpperInvariant(), Role = Roles.Enterprise, DisplayName = "Doanh nghiệp demo 2" };
        account.PasswordHash = hasher.HashPassword(account, password); db.Users.Add(account); await db.SaveChangesAsync();
        Console.WriteLine("Second isolated Enterprise ready; credentials were not logged.");
    }
}
