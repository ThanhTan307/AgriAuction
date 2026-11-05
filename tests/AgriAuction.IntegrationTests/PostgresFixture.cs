using System.Data.Common;
using System.Text.Json;
using AgriAuction.Api.Configuration;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AgriAuction.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresFixture.Connection()))
            Skip = "NOT RUN: chưa cấu hình PostgreSQL test. Đặt AGRIAUCTION_TEST_CONNECTION hoặc .local/database.json; test cần schema mới riêng.";
    }
}

public sealed class FaultInterceptor : DbCommandInterceptor
{
    private int pending;
    public void FailNextRead() => Interlocked.Exchange(ref pending, 1);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref pending, 0) == 1) throw new NpgsqlException("Injected test connection failure");
        return ValueTask.FromResult(result);
    }
}

public sealed class TestHost(FaultInterceptor fault) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services => services.AddDbContext<AppDbContext>(options => options.AddInterceptors(fault)));
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public static string Connection()
    {
        var raw = Environment.GetEnvironmentVariable("AGRIAUCTION_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(raw)) return raw;
        return ProjectSettings.ReadLocal("database.json", "connectionString");
    }

    public TestHost Host { get; private set; } = null!;
    public FaultInterceptor Fault { get; } = new();
    public string Schema { get; } = "agri_test_" + Guid.NewGuid().ToString("N");
    public string Password { get; } = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public Dictionary<string, Account> Accounts { get; } = [];
    public Account OtherFarmer { get; private set; } = null!;
    private readonly Dictionary<string, string?> previous = [];

    public async Task InitializeAsync()
    {
        var raw = Connection();
        if (string.IsNullOrWhiteSpace(raw)) return;
        Set("ConnectionStrings__AgriAuction", raw);
        Set("Database__Schema", Schema);
        Set("Auth__SigningKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)));
        Host = new TestHost(Fault);
        await using var scope = Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using (var connection = new NpgsqlConnection(raw))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE SCHEMA \"" + Schema + "\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        await db.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>();
        foreach (var role in Roles.All)
        {
            var email = role.ToLowerInvariant() + "@test.invalid";
            var account = new Account { Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = "Test " + role, Role = role };
            account.PasswordHash = hasher.HashPassword(account, Password);
            Accounts.Add(role, account);
            db.Users.Add(account);
        }
        OtherFarmer = new Account { Email = "other-farmer@test.invalid", NormalizedEmail = "OTHER-FARMER@TEST.INVALID", DisplayName = "Test Farmer khác", Role = Roles.Farmer };
        OtherFarmer.PasswordHash = hasher.HashPassword(OtherFarmer, Password);
        db.Users.Add(OtherFarmer);
        await db.SaveChangesAsync();
        Console.WriteLine("PostgreSQL test schema: " + Schema + " (kept for inspection; existing schemas untouched)");
    }

    private void Set(string key, string value)
    {
        previous[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    public async Task DisposeAsync()
    {
        if (Host is not null) await Host.DisposeAsync();
        foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value);
        // No DROP/DELETE: leave this uniquely named test schema for review.
    }
}
