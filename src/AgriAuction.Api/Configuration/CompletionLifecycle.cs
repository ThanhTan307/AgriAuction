using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgriAuction.Api.Configuration;

// Authorized lifecycle, separate from the historical P0 preflight and legacy runners.
public static class CompletionLifecycle
{
    public static CompletionTestTarget Read(string environment, string raw)
    {
        var target = CompletionTestGuard.ReadExplicit(System.Environment.GetEnvironmentVariable("AGRIAUCTION_COMPLETION_CONFIG"));
        CompletionTestGuard.ValidateConnection(target, raw, environment);
        if (target.ApprovalStatus != "Approved") throw new CompletionTargetRejected("COMPLETION_TARGET_PENDING");
        foreach (var key in new[] { "Simulation:WorkerEnabled", "Simulation:PostAuctionWorkerEnabled", "Simulation:ComplaintWorkerEnabled" })
            if (System.Environment.GetEnvironmentVariable(key.Replace(":", "__")) != target.WorkersEnabled.ToString().ToLowerInvariant())
                throw new CompletionTargetRejected("COMPLETION_WORKER_REJECTED");
        var root = Path.GetFullPath(ProjectSettings.SourceRoot());
        var suffix = target.SourceRoot.Replace('/', Path.DirectorySeparatorChar);
        if (!root.EndsWith(Path.DirectorySeparatorChar + suffix, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(typeof(CompletionLifecycle).Assembly.Location).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new CompletionTargetRejected("COMPLETION_SOURCE_REJECTED");
        var manifestPath = Path.Combine(root, "source-manifest.json");
        if (!File.Exists(manifestPath)) throw new CompletionTargetRejected("COMPLETION_SOURCE_MANIFEST_REQUIRED");
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath))!;
        foreach (var (relative, expected) in manifest)
        {
            var file = Path.GetFullPath(Path.Combine(root, relative));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(file) || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)
                || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new CompletionTargetRejected("COMPLETION_SOURCE_HASH_REJECTED");
        }
        return target;
    }

    public static async Task VerifyAsync(CompletionTestTarget target, string raw, bool schemaRequired, CancellationToken ct = default)
    {
        CompletionTestGuard.ValidateConnection(target, raw, "Testing");
        await using var connection = new NpgsqlConnection(raw);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT current_database() = @db AND current_user = @role AND session_user = @role
              AND inet_server_addr() = '127.0.0.1'::inet AND inet_server_port() = 55432
              AND current_setting('search_path') = @schema
              AND NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole
              AND NOT r.rolreplication AND NOT r.rolbypassrls AND r.rolcanlogin
              AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
              AND pg_get_userbyid(d.datdba) = current_user
              AND has_database_privilege(current_user, current_database(), 'CONNECT,CREATE')
            FROM pg_roles r JOIN pg_database d ON d.datname = current_database() WHERE r.rolname = current_user
            """, connection);
        cmd.Parameters.AddWithValue("db", target.Database); cmd.Parameters.AddWithValue("role", target.Role); cmd.Parameters.AddWithValue("schema", target.Schema);
        if (await cmd.ExecuteScalarAsync(ct) is not true) throw new CompletionTargetRejected("COMPLETION_LIVE_IDENTITY_REJECTED");
        await using var schema = new NpgsqlCommand("SELECT pg_get_userbyid(nspowner) = current_user AND has_schema_privilege(current_user, oid, 'USAGE,CREATE') FROM pg_namespace WHERE nspname = @schema", connection);
        schema.Parameters.AddWithValue("schema", target.Schema);
        var owned = await schema.ExecuteScalarAsync(ct);
        if (owned is false || (schemaRequired && owned is not true)) throw new CompletionTargetRejected("COMPLETION_LIVE_SCHEMA_REJECTED");
    }
}
