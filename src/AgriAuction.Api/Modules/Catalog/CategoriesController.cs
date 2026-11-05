using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriAuction.Api.Modules.Catalog;

[ApiController, Authorize]
public sealed class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet("api/v1/catalog/categories")]
    public Task<CategoryPage> Read(string? query, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => service.ListAsync(query, page, pageSize, false, ct);

    [HttpGet("api/v1/catalog/categories/{id:guid}")]
    public Task<CategoryView> ReadOne(Guid id, CancellationToken ct) => service.GetAsync(id, false, ct);

    [Authorize(Roles = Roles.Administrator), HttpGet("api/v1/admin/catalog/categories")]
    public Task<CategoryPage> AdminList(string? query, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => service.ListAsync(query, page, pageSize, true, ct);

    [Authorize(Roles = Roles.Administrator), HttpGet("api/v1/admin/catalog/categories/{id:guid}")]
    public Task<CategoryView> AdminGet(Guid id, CancellationToken ct) => service.GetAsync(id, true, ct);

    [Authorize(Roles = Roles.Administrator), HttpPost("api/v1/admin/catalog/categories")]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var category = await service.CreateAsync(request, SessionAuthentication.Actor(User), ct);
        return Created($"/api/v1/admin/catalog/categories/{category.Id}", category);
    }

    [Authorize(Roles = Roles.Administrator), HttpPatch("api/v1/admin/catalog/categories/{id:guid}")]
    public Task<CategoryView> Update(Guid id, UpdateCategoryRequest request, CancellationToken ct)
        => service.UpdateAsync(id, request, SessionAuthentication.Actor(User), ct);
}
