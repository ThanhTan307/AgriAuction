using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriAuction.Api.Configuration;

public static class GvhdDemoBootstrap
{
    public static void ValidateTarget(string environment, string connectionString, string schema)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (environment is not ("Development" or "Testing") || connection.Host != "127.0.0.1"
            || connection.Port != 55432 || connection.Database != "agri_gvhd_demo"
            || connection.Username != "agri_gvhd_demo" || schema != "agri_demo")
            throw new InvalidOperationException("GVHD bootstrap requires the dedicated local demo database/role in Development or Testing.");
    }

    public static async Task RunAsync(AppDbContext db, IPasswordHasher<Account> hasher,
        ICategoryService catalog, IConfiguration config, CancellationToken ct = default)
    {
        var specs = new[] { ("Administrator", Roles.Administrator, "Quản trị hệ thống"),
            ("Farmer", Roles.Farmer, "Nông dân Minh An (Demo)"),
            ("Enterprise", Roles.Enterprise, "Doanh nghiệp An Phú (Demo)"),
            ("Enterprise2", Roles.Enterprise, "Doanh nghiệp Bình Minh (Demo)") };
        var credentials = specs.Select(s => (s.Item1, Role: s.Item2, Name: s.Item3,
            Email: config[$"Gvhd:{s.Item1}:Email"]?.Trim() ?? "",
            Password: config[$"Gvhd:{s.Item1}:Password"] ?? "")).ToArray();
        if (credentials.Any(c => !System.Net.Mail.MailAddress.TryCreate(c.Email, out _) || c.Password.Length < 20)
            || credentials.Select(c => c.Email.ToUpperInvariant()).Distinct().Count() != 4)
            throw new InvalidOperationException("Provide four distinct private generated GVHD identities; credentials are never printed.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        Guid administrator = default;
        foreach (var spec in credentials)
        {
            var normalized = spec.Email.ToUpperInvariant();
            var account = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (account is null)
            {
                account = new Account { Email = spec.Email, NormalizedEmail = normalized, Role = spec.Role, DisplayName = spec.Name };
                account.PasswordHash = hasher.HashPassword(account, spec.Password);
                db.Users.Add(account);
            }
            else if (account.Role != spec.Role || !account.IsEnabled
                || hasher.VerifyHashedPassword(account, account.PasswordHash, spec.Password) == PasswordVerificationResult.Failed)
                throw new InvalidOperationException("Existing GVHD identity differs; refusing to overwrite credentials, role or account status.");
            else if (account.DisplayName != spec.Name)
                account.DisplayName = spec.Name;
            if (spec.Role == Roles.Administrator) administrator = account.Id;
        }
        await db.SaveChangesAsync(ct);
        foreach (var (code, name) in new[] { ("XOAI", "Xoài"), ("SAU_RIENG", "Sầu riêng"), ("CA_PHE", "Cà phê"), ("THANH_LONG", "Thanh long") })
            if (!await db.Categories.AnyAsync(c => c.Code == code, ct))
                await catalog.CreateAsync(new(code, name, true), administrator, ct);
        await transaction.CommitAsync(ct);
        Console.WriteLine("GVHD background identities/categories ready. Existing rows were preserved; no auction, lot or bid was seeded.");
    }
}
