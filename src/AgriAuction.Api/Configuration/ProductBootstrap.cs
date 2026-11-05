using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Api.Configuration;

internal static class ProductBootstrap
{
    private static readonly (string Kind, string[] Names)[] WorkflowDefaults =
    [
        ("Inspection", [
            "Đối chiếu mô tả lô với hình ảnh và thông tin kê khai",
            "Kiểm tra thông tin nguồn gốc, vùng trồng và thời gian thu hoạch",
            "Đối chiếu sản lượng, quy cách và tình trạng hàng theo hồ sơ"
        ]),
        ("ComplaintReason", [
            "Chất lượng hàng hóa không đúng mô tả",
            "Số lượng hoặc khối lượng giao nhận không đúng",
            "Giao hàng chậm hoặc không đúng thời gian",
            "Thông tin hoặc chứng từ hàng hóa không khớp",
            "Vấn đề thanh toán hoặc hoàn cọc"
        ])
    ];

    public static async Task EnsureWorkflowDefaultsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var admin = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Administrator && u.IsEnabled)
            .OrderBy(u => u.Id)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (admin is null) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(7360021)", ct);
        foreach (var (kind, names) in WorkflowDefaults)
        {
            if (await db.WorkflowCatalogItems.AnyAsync(i => i.Kind == kind, ct)) continue;
            foreach (var name in names)
            {
                var item = new WorkflowCatalogItem { Kind = kind, Name = name, Active = true };
                db.WorkflowCatalogItems.Add(item);
                db.AuditLogs.Add(new() {
                    ActorId = admin.Value,
                    EntityId = item.Id,
                    Action = "WorkflowCatalogDefaultSeeded",
                    After = System.Text.Json.JsonSerializer.Serialize(new { item.Id, item.Kind, item.Name, item.Active, item.Version })
                });
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public static async Task RunAsync(AppDbContext db, IPasswordHasher<Account> hasher,
        ICategoryService catalog, IConfiguration config, CancellationToken ct = default)
    {
        var specs = new[]
        {
            ("Administrator", Roles.Administrator, "Quản trị hệ thống"),
            ("Farmer", Roles.Farmer, "Nông dân Minh An"),
            ("Enterprise", Roles.Enterprise, "Doanh nghiệp An Phú"),
            ("Enterprise2", Roles.Enterprise, "Doanh nghiệp Bình Minh")
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var accounts = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var (key, role, displayName) in specs)
        {
            var email = config[$"ProductSeed:{key}:Email"]?.Trim() ?? "";
            var password = config[$"ProductSeed:{key}:Password"] ?? "";
            if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || password.Length < 12)
                throw new InvalidOperationException("Thiếu thông tin tài khoản khởi tạo trong cấu hình cục bộ.");

            var normalized = email.ToUpperInvariant();
            var account = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (account is null)
            {
                account = new Account { Email = email, NormalizedEmail = normalized, Role = role, DisplayName = displayName };
                account.PasswordHash = hasher.HashPassword(account, password);
                db.Users.Add(account);
            }
            else if (account.Role != role || !account.IsEnabled
                || hasher.VerifyHashedPassword(account, account.PasswordHash, password) == PasswordVerificationResult.Failed)
                throw new InvalidOperationException($"Tài khoản {email} đã tồn tại nhưng không khớp cấu hình cục bộ.");
            else if (account.DisplayName != displayName)
                account.DisplayName = displayName;
            if (role != Roles.Administrator && account.ApprovalState is null)
                account.ApprovalState = "Approved";
            accounts[key] = account;
        }

        await db.SaveChangesAsync(ct);
        foreach (var (code, name) in new[]
        {
            ("XOAI", "Xoài"), ("SAU_RIENG", "Sầu riêng"),
            ("CA_PHE", "Cà phê"), ("THANH_LONG", "Thanh long")
        })
            if (!await db.Categories.AnyAsync(c => c.Code == code, ct))
                await catalog.CreateAsync(new(code, name, true), accounts["Administrator"].Id, ct);

        await transaction.CommitAsync(ct);
        Console.WriteLine("Tài khoản và danh mục khởi tạo đã sẵn sàng; dữ liệu hiện có không bị ghi đè.");
    }
}
