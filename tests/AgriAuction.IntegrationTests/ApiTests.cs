using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class ApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Catalog = "/api/v1/admin/catalog/categories";
    private static string Code() => "T-" + Guid.NewGuid().ToString("N")[..18].ToUpperInvariant();
    private HttpClient Client(bool cookies = false) => fixture.Host.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = cookies, BaseAddress = new Uri("http://localhost") });

    private async Task<HttpClient> Login(string role = Roles.Administrator)
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = fixture.Accounts[role].Email, password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<CategoryView> Create(HttpClient client, string? code = null)
    {
        var response = await client.PostAsJsonAsync(Catalog, new { code = code ?? Code(), name = "Dữ liệu kiểm thử", active = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryView>())!;
    }

    [PostgresFact]
    public async Task AnonymousRequestsAreUnauthorizedAndPageRedirectsToLogin()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Catalog)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Catalog, new { code = "NO", name = "Không", active = true })).StatusCode);
        var page = await client.GetAsync("/pages/admin/category-management.html");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/pages/login.html", page.Headers.Location!.ToString());
    }

    [PostgresFact]
    public async Task FarmerAndEnterpriseCannotAdministerCategories()
    {
        foreach (var role in new[] { Roles.Farmer, Roles.Enterprise })
        {
            using var client = await Login(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Catalog)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Catalog, new { code = Code(), name = "Không", active = true })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync(Catalog + "/" + Guid.NewGuid(), new { code = "NO", name = "Không", active = false, expectedVersion = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/catalog/categories")).StatusCode);
        }
    }

    [PostgresFact]
    public async Task BadPasswordIsRejectedAndStoredPasswordIsHashed()
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = fixture.Accounts[Roles.Administrator].Email, password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(x => x.Id == fixture.Accounts[Roles.Administrator].Id);
        Assert.NotEqual(fixture.Password, account.PasswordHash);
        Assert.DoesNotContain(fixture.Password, await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task CreateReadEditInactivatePersistsAndWritesAtomicAudit()
    {
        using var client = await Login();
        var created = await Create(client);
        var response = await client.PatchAsJsonAsync(Catalog + "/" + created.Id, new { code = created.Code, name = "Tên đã sửa", active = false, expectedVersion = created.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await client.GetFromJsonAsync<CategoryView>(Catalog + "/" + created.Id);
        Assert.Equal("Tên đã sửa", read!.Name);
        Assert.False(read.Active);
        Assert.Equal(2, read.Version);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Categories.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(read.Name, row.Name);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.EntityId == created.Id));
        using var enterprise = await Login(Roles.Enterprise);
        Assert.Equal(HttpStatusCode.NotFound, (await enterprise.GetAsync("/api/v1/catalog/categories/" + created.Id)).StatusCode);
    }

    [PostgresFact]
    public async Task InvalidFieldsAreReturnedAndDoNotWriteAnything()
    {
        using var client = await Login();
        var response = await client.PostAsJsonAsync(Catalog, new { code = "bad code", name = "", active = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("errors", out var errors), "Expected field errors, received: " + body.GetRawText());
        Assert.True(errors.TryGetProperty("code", out _));
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Catalog, new { code = Code(), name = "Valid" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Catalog + "?page=0")).StatusCode);
    }

    [PostgresFact]
    public async Task DuplicateCodeIs409AndDoesNotWritePartialAudit()
    {
        using var client = await Login();
        var category = await Create(client);
        var response = await client.PostAsJsonAsync(Catalog, new { code = category.Code.ToLowerInvariant(), name = "Duplicate", active = true });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("DUPLICATE_CODE", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Categories.CountAsync(x => x.Code == category.Code));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityId == category.Id));
    }

    [PostgresFact]
    public async Task StaleEditIs409AndCannotOverwriteNewerData()
    {
        using var client = await Login();
        var c = await Create(client);
        var first = await client.PatchAsJsonAsync(Catalog + "/" + c.Id, new { code = c.Code, name = "Mới", active = true, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await client.PatchAsJsonAsync(Catalog + "/" + c.Id, new { code = c.Code, name = "Cũ", active = false, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Mới", (await client.GetFromJsonAsync<CategoryView>(Catalog + "/" + c.Id))!.Name);
    }

    [PostgresFact]
    public async Task ConcurrentDuplicateCreatesAcceptOnlyOne()
    {
        using var first = await Login();
        using var second = await Login();
        var code = Code();
        var responses = await Task.WhenAll(first.PostAsJsonAsync(Catalog, new { code, name = "Một", active = true }), second.PostAsJsonAsync(Catalog, new { code, name = "Hai", active = true }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task LogoutRevokesExistingBearerToken()
    {
        using var client = await Login();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Catalog)).StatusCode);
    }

    [PostgresFact]
    public async Task BrowserCookieFlowRequiresCsrfAndServesTheRealPage()
    {
        using var client = Client(true);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=true", new { email = fixture.Accounts[Roles.Administrator].Email, password = fixture.Password })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pages/admin/category-management.html")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Catalog, new { code = Code(), name = "CSRF", active = true })).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        var c = await Create(client);
        var html = await client.GetStringAsync("/pages/admin/category-management.html");
        Assert.Contains("category-management.js", html);
        Assert.DoesNotContain("CAT-DUR-01", html);
        Assert.Equal(c.Code, (await client.GetFromJsonAsync<CategoryView>(Catalog + "/" + c.Id))!.Code);
    }

    [PostgresFact]
    public async Task DatabaseErrorsAre503WithoutFalseSuccessOrConnectionSecrets()
    {
        using var client = await Login();
        fixture.Fault.FailNextRead();
        var response = await client.GetAsync(Catalog);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("DATABASE_UNAVAILABLE", text);
        Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Injected test", text);
    }

    [PostgresFact]
    public async Task PostgresCheckConstraintActuallyRejectsInvalidCategory()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var bad = "has space";
        var name = "Bad";
        var now = DateTimeOffset.UtcNow;
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO categories (id, code, name, active, version, created_at, updated_at) VALUES ({id}, {bad}, {name}, true, 1, {now}, {now})"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.False(await db.Categories.AnyAsync(x => x.Id == id));
    }
}
