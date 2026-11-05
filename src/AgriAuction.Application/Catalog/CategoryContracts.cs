using AgriAuction.Domain.Catalog;

namespace AgriAuction.Application.Catalog;

public sealed record CategoryView(Guid Id, string Code, string Name, bool Active, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static CategoryView From(Category c) => new(c.Id, c.Code, c.Name, c.Active, c.Version, c.CreatedAt, c.UpdatedAt);
}
public sealed record CategoryPage(IReadOnlyList<CategoryView> Items, int TotalCount, int AllCount, int ActiveCount, int InactiveCount, int Page, int PageSize);
public sealed record CreateCategoryRequest(string? Code, string? Name, bool? Active);
public sealed record UpdateCategoryRequest(string? Code, string? Name, bool? Active, long? ExpectedVersion);
public sealed class ResourceMissing : Exception;
public sealed class VersionConflict : Exception;

public interface ICategoryService
{
    Task<CategoryPage> ListAsync(string? query, int page, int pageSize, bool admin, CancellationToken ct);
    Task<CategoryView> GetAsync(Guid id, bool admin, CancellationToken ct);
    Task<CategoryView> CreateAsync(CreateCategoryRequest request, Guid actorId, CancellationToken ct);
    Task<CategoryView> UpdateAsync(Guid id, UpdateCategoryRequest request, Guid actorId, CancellationToken ct);
}
