using AgriAuction.Api.Configuration;
using Xunit;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.IntegrationTests;

// Separate opt-in fixture: never derive from the legacy fixture or use its connection fallback.
// P0 cannot initialize a host, connect, migrate, seed or start a worker, even with Approved JSON.
public sealed class CompletionPostgresFixture : IAsyncLifetime
{
    public string Connection { get; private set; } = "";
    public AppDbContext Create() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
    public async Task InitializeAsync()
    {
        var target = CompletionTestGuard.ReadExplicit(
            Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONFIG"));
        Connection = Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONNECTION") ?? "";
        CompletionTestGuard.ValidateConnection(target, Connection, Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_ENVIRONMENT") ?? "");
        if (target.Kind != "it" || target.ApprovalStatus != "Approved") throw new CompletionTargetRejected("COMPLETION_TARGET_PENDING");
        await CompletionLifecycle.VerifyAsync(target, Connection, false);
        await using var db = Create();
        await db.Database.OpenConnectionAsync();
        await using (var cmd = db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = "CREATE SCHEMA IF NOT EXISTS \"" + target.Schema + "\"";
            await cmd.ExecuteNonQueryAsync();
        }
        await CompletionLifecycle.VerifyAsync(target, Connection, true);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask; // No DROP/DELETE or shared-process cleanup.
}
