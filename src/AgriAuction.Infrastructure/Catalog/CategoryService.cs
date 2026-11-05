using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Catalog;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Catalog;

public sealed class CategoryService(AppDbContext db) : ICategoryService
{
    public async Task<CategoryPage> ListAsync(string? query, int page, int pageSize, bool admin, CancellationToken ct)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100 || query?.Length > 120)
            throw new RuleViolation(new() { ["query"] = ["Trang >= 1, kích thước trang 1–100, tìm kiếm tối đa 120 ký tự."] });
        var all = db.Categories.AsNoTracking().Where(x => admin || x.Active);
        var allCount = await all.CountAsync(ct);
        var activeCount = await all.CountAsync(x => x.Active, ct);
        var filtered = all;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            var upper = text.ToUpperInvariant();
            filtered = filtered.Where(x => x.Name.Contains(text) || x.Code.Contains(upper));
        }
        var count = await filtered.CountAsync(ct);
        var entities = await filtered.OrderBy(x => x.Code).Skip(checked((page - 1) * pageSize)).Take(pageSize).ToListAsync(ct);
        return new(entities.Select(CategoryView.From).ToList(), count, allCount, activeCount, allCount - activeCount, page, pageSize);
    }

    public async Task<CategoryView> GetAsync(Guid id, bool admin, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.Active), ct)
            ?? throw new ResourceMissing();
        return CategoryView.From(category);
    }

    public async Task<CategoryView> CreateAsync(CreateCategoryRequest request, Guid actorId, CancellationToken ct)
    {
        if (request.Active is null) throw new RuleViolation(new() { ["active"] = ["Vui lòng chọn trạng thái."] });
        var category = Category.Create(request.Code, request.Name, request.Active.Value);
        db.Categories.Add(category);
        db.AuditLogs.Add(new() { ActorId = actorId, EntityId = category.Id, Action = "CategoryCreated", After = JsonSerializer.Serialize(CategoryView.From(category)) });
        await db.SaveChangesAsync(ct);
        return CategoryView.From(category);
    }

    public async Task<CategoryView> UpdateAsync(Guid id, UpdateCategoryRequest request, Guid actorId, CancellationToken ct)
    {
        if (request.Active is null) throw new RuleViolation(new() { ["active"] = ["Vui lòng chọn trạng thái."] });
        if (request.ExpectedVersion is null or < 1) throw new RuleViolation(new() { ["expectedVersion"] = ["Thiếu phiên bản hợp lệ."] });
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissing();
        if (category.Version != request.ExpectedVersion) throw new VersionConflict();
        var before = JsonSerializer.Serialize(CategoryView.From(category));
        category.Update(request.Code, request.Name, request.Active.Value);
        db.AuditLogs.Add(new() { ActorId = actorId, EntityId = category.Id, Action = "CategoryUpdated", Before = before, After = JsonSerializer.Serialize(CategoryView.From(category)) });
        await db.SaveChangesAsync(ct);
        return CategoryView.From(category);
    }
}
