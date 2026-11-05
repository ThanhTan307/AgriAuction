using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgriAuction.Api.Configuration;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = ProjectSettings.SourceRoot();
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "src", "AgriAuction.Api"))
            .AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();
        // Generating a migration does not open a DB connection or need credentials.
        // For database-update use the same explicit connection configuration as the host.
        var raw = config.GetConnectionString("AgriAuction");
        if (string.IsNullOrWhiteSpace(raw)) raw = ProjectSettings.ReadLocal("database.json", "connectionString");
        var connection = string.IsNullOrWhiteSpace(raw) ? "Host=localhost;Database=design_time_only" : ProjectSettings.Connection(config, true);
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }
}
