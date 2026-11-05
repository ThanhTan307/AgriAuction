using System.Text.Json;
using System.Text.Json.Nodes;
using AgriAuction.Api.Configuration;
using Xunit;

namespace AgriAuction.IntegrationTests;

// Pure guard tests in this assembly; no PostgresFixture, TestHost or database is constructed.
public sealed class CompletionTargetGuardTests
{
    private const string Run = "00000000000000000000000000000001";
    private static CompletionTestTarget Target(string kind = "e2e") => new()
    {
        Version = 1, Environment = "Testing", Host = "127.0.0.1", Port = 55432,
        Database = CompletionTestGuard.DatabaseName, Role = CompletionTestGuard.DatabaseName,
        Kind = kind, RunId = Run, Schema = $"agri_wc_{kind}_{Run}", BaseUrl = CompletionTestGuard.BaseUrl,
        SourceRoot = $".local/web-completion/source/{Run}", ApprovalStatus = "Pending", WorkersEnabled = false
    };
    private static string Connection(CompletionTestTarget t) => $"Host={t.Host};Port={t.Port};Database={t.Database};Username={t.Role};Password=fixture-only-not-a-real-secret;Search Path={t.Schema}";

    [Theory]
    [InlineData("it")]
    [InlineData("e2e")]
    public void ProposedTargetCanBeValidatedWithoutOpeningAConnection(string kind)
    {
        var t = Target(kind);
        CompletionTestGuard.ValidateTarget(t);
        CompletionTestGuard.ValidateConnection(t, Connection(t), "Testing");
    }

    public static TheoryData<string, CompletionTestTarget> InvalidTargets => new()
    {
        { "old-db", Target() with { Database = "agri_phase1_test" } },
        { "demo-db", Target() with { Database = "agri_gvhd_demo" } },
        { "verify-db", Target() with { Database = "agri_gvhd_verify" } },
        { "wrong-role", Target() with { Role = "postgres" } },
        { "shared-role", Target() with { Role = "agri_gvhd_demo" } },
        { "system-port", Target() with { Port = 5432 } },
        { "remote", Target() with { Host = "remote.invalid" } },
        { "hostname-alias", Target() with { Host = "localhost" } },
        { "multiple-hosts", Target() with { Host = "127.0.0.1,remote.invalid" } },
        { "development", Target() with { Environment = "Development" } },
        { "production", Target() with { Environment = "Production" } },
        { "public", Target() with { Schema = "public" } },
        { "demo-schema", Target() with { Schema = "agri_demo" } },
        { "extra-schema", Target() with { Schema = Target().Schema + ",agri_demo" } },
        { "other-run", Target() with { Schema = "agri_wc_e2e_11111111111111111111111111111111" } },
        { "empty-run", Target() with { RunId = new string('0', 32) } },
        { "run-injection", Target() with { RunId = "../agri_demo" } },
        { "kind", Target() with { Kind = "legacy" } },
        { "demo-http", Target() with { BaseUrl = "http://127.0.0.1:5081" } },
        { "old-http", Target() with { BaseUrl = "http://localhost:5080" } },
        { "source-parent", Target() with { SourceRoot = ".." } },
        { "source-original", Target() with { SourceRoot = "." } },
        { "workers", Target() with { WorkersEnabled = true } },
        { "unversioned", Target() with { Version = 0 } },
        { "approval-without-record", Target() with { ApprovalStatus = "Approved" } },
        { "pending-with-record", Target() with { ApprovedBy = "someone" } }
    };

    [Theory, MemberData(nameof(InvalidTargets))]
    public void WrongTargetsAreRejectedBeforeTheDatabaseBoundary(string scenario, CompletionTestTarget target)
    {
        var entered = false;
        Assert.Throws<CompletionTargetRejected>(() =>
        {
            CompletionTestGuard.BeforeDatabaseAccess(target, Connection(target), "Testing");
            entered = true; // This represents constructing/opening the DB/host, not a fake DB test.
        });
        Assert.False(entered, scenario);
    }

    [Theory]
    [InlineData("Host=localhost")]
    [InlineData("Port=5432")]
    [InlineData("Database=agri_gvhd_demo")]
    [InlineData("Database=agri_phase1_test")]
    [InlineData("Username=postgres")]
    [InlineData("Search Path=agri_demo")]
    [InlineData("Search Path=public")]
    [InlineData("Search Path=agri_wc_e2e_00000000000000000000000000000001,agri_demo")]
    [InlineData("Options=-c search_path=agri_demo")]
    [InlineData("Include Error Detail=true")]
    public void ActualConnectionCannotOverrideTheManifest(string overridePart)
    {
        Assert.Throws<CompletionTargetRejected>(() => CompletionTestGuard.ValidateConnection(Target(), Connection(Target()) + ";" + overridePart, "Testing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a connection string")]
    [InlineData("Host=127.0.0.1")]
    [InlineData("Host=127.0.0.1;Port=55432;Database=agri_web_completion_test;Username=agri_web_completion_test;Password=sentinel")]
    public void MissingOrMalformedConnectionNeverUsesLegacyFallback(string? raw) =>
        Assert.Throws<CompletionTargetRejected>(() => CompletionTestGuard.ValidateConnection(Target(), raw, "Testing"));

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("")]
    public void ActualEnvironmentMustMatchTesting(string actual) =>
        Assert.Throws<CompletionTargetRejected>(() => CompletionTestGuard.ValidateConnection(Target(), Connection(Target()), actual));

    [Theory]
    [InlineData(false, "COMPLETION_TARGET_PENDING")]
    [InlineData(true, "COMPLETION_P0_EXECUTION_CLOSED")]
    public void EvenCorrectApprovedTargetDoesNotReleaseWritesInP0(bool approved, string expected)
    {
        var target = approved ? Target() with { ApprovalStatus = "Approved", ApprovedBy = "test actor", ApprovalReference = "unit fixture only" } : Target();
        var entered = false;
        var error = Assert.Throws<CompletionTargetRejected>(() => { CompletionTestGuard.BeforeDatabaseAccess(target, Connection(target), "Testing"); entered = true; });
        Assert.Equal(expected, error.Message);
        Assert.False(entered);
        Assert.DoesNotContain("fixture-only", error.ToString());
    }

    [Fact]
    public void RepositoryExampleIsPendingAndContainsNoRuntimeCredentials()
    {
        var t = CompletionTestGuard.ReadExplicit(Path.Combine(ProjectSettings.SourceRoot(), "config", "web-completion.example.json"));
        Assert.Equal("Pending", t.ApprovalStatus);
        Assert.Null(t.ApprovedBy);
    }

    [Fact]
    public void StrictJsonAndMissingPropertiesAreRejectedWithoutParserDetails()
    {
        var directory = Path.Combine(ProjectSettings.SourceRoot(), "TestResults", "web-completion-p0", "json-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        foreach (var mode in new[] { "unknown", "missing", "malformed" })
        {
            var document = JsonSerializer.SerializeToNode(Target(), options)!.AsObject();
            if (mode == "unknown") document["connectionString"] = "sentinel-private-value";
            if (mode == "missing") document.Remove("workersEnabled");
            var file = Path.Combine(directory, mode + ".json");
            File.WriteAllText(file, mode == "malformed" ? "sentinel-private-value" : document.ToJsonString());
            var error = Assert.Throws<CompletionTargetRejected>(() => CompletionTestGuard.ReadExplicit(file));
            Assert.DoesNotContain("sentinel", error.ToString());
        }
        Assert.Throws<CompletionTargetRejected>(() => CompletionTestGuard.ReadExplicit(null));
    }

    [Fact]
    public async Task NewFixtureWithoutExplicitConfigRefusesBeforeAnyHostOrDatabase()
    {
        var before = Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONFIG");
        try
        {
            Environment.SetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONFIG", null);
            var fixture = new CompletionPostgresFixture();
            var error = await Assert.ThrowsAsync<CompletionTargetRejected>(() => fixture.InitializeAsync());
            Assert.Equal("COMPLETION_CONFIG_REQUIRED", error.Message);
            await fixture.DisposeAsync();
        }
        finally { Environment.SetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONFIG", before); }
    }
}
