using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;

namespace AgriAuction.Api.Configuration;

// Opt-in test tooling only. Program/legacy bootstrap/production behavior is unchanged.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompletionTestTarget
{
    [JsonRequired] public int Version { get; init; }
    [JsonRequired] public string Environment { get; init; } = "";
    [JsonRequired] public string Host { get; init; } = "";
    [JsonRequired] public int Port { get; init; }
    [JsonRequired] public string Database { get; init; } = "";
    [JsonRequired] public string Role { get; init; } = "";
    [JsonRequired] public string Kind { get; init; } = "";
    [JsonRequired] public string RunId { get; init; } = "";
    [JsonRequired] public string Schema { get; init; } = "";
    [JsonRequired] public string BaseUrl { get; init; } = "";
    [JsonRequired] public string SourceRoot { get; init; } = "";
    [JsonRequired] public string ApprovalStatus { get; init; } = "";
    [JsonRequired] public string? ApprovedBy { get; init; }
    [JsonRequired] public string? ApprovalReference { get; init; }
    [JsonRequired] public bool WorkersEnabled { get; init; }
}

public sealed class CompletionTargetRejected(string code) : InvalidOperationException(code);

public static class CompletionTestGuard
{
    public const string DatabaseName = "agri_web_completion_test";
    public const string BaseUrl = "http://127.0.0.1:5083";
    private static CompletionTargetRejected Reject(string code) => new(code);

    public static CompletionTestTarget ReadExplicit(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw Reject("COMPLETION_CONFIG_REQUIRED");
        try
        {
            var target = JsonSerializer.Deserialize<CompletionTestTarget>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (target is null) throw Reject("COMPLETION_CONFIG_INVALID");
            ValidateTarget(target);
            return target;
        }
        catch (CompletionTargetRejected) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Do not expose file contents, connection strings or parser messages.
            throw Reject("COMPLETION_CONFIG_INVALID");
        }
    }

    public static void ValidateTarget(CompletionTestTarget target)
    {
        if (target.Version is not (1 or 2) || target.Environment != "Testing" || target.Host != "127.0.0.1"
            || target.Port != 55432 || target.Database != DatabaseName || target.Role != DatabaseName
            || target.BaseUrl != BaseUrl || (target.Version == 1 && target.WorkersEnabled) || (target.Version == 2 && target.ApprovalStatus != "Approved"))
            throw Reject("COMPLETION_TARGET_REJECTED");
        if (target.Kind is not ("it" or "e2e") || !Regex.IsMatch(target.RunId ?? "", "^[0-9a-f]{32}$")
            || target.RunId == new string('0', 32)
            || target.Schema != $"agri_wc_{target.Kind}_{target.RunId}")
            throw Reject("COMPLETION_SCHEMA_REJECTED");
        // Portable relative path is deliberately bound to this run; no arbitrary source/HTML root.
        if (target.SourceRoot != $".local/web-completion/source/{target.RunId}")
            throw Reject("COMPLETION_SOURCE_REJECTED");
        if (target.ApprovalStatus == "Pending")
        {
            if (target.ApprovedBy is not null || target.ApprovalReference is not null)
                throw Reject("COMPLETION_APPROVAL_INVALID");
        }
        else if (target.ApprovalStatus != "Approved" || string.IsNullOrWhiteSpace(target.ApprovedBy)
            || string.IsNullOrWhiteSpace(target.ApprovalReference))
            throw Reject("COMPLETION_APPROVAL_INVALID");
    }

    public static void ValidateConnection(CompletionTestTarget target, string? raw, string actualEnvironment)
    {
        ValidateTarget(target);
        if (actualEnvironment != target.Environment) throw Reject("COMPLETION_ENVIRONMENT_REJECTED");
        if (string.IsNullOrWhiteSpace(raw)) throw Reject("COMPLETION_CONNECTION_REQUIRED");
        try
        {
            // Small canonical allowlist, no Options, multi-host, alias, service or extra search_path.
            var allowed = new HashSet<string>(["Host", "Port", "Database", "Username", "Password", "Search Path"], StringComparer.OrdinalIgnoreCase);
            var parts = new DbConnectionStringBuilder { ConnectionString = raw };
            foreach (string key in parts.Keys)
                if (!allowed.Contains(key)) throw Reject("COMPLETION_CONNECTION_REJECTED");
            if (allowed.Any(key => !parts.ContainsKey(key))) throw Reject("COMPLETION_CONNECTION_REJECTED");
            var connection = new NpgsqlConnectionStringBuilder(raw);
            if (connection.Host != target.Host || connection.Port != target.Port || connection.Database != target.Database
                || connection.Username != target.Role || connection.SearchPath != target.Schema
                || string.IsNullOrWhiteSpace(connection.Password))
                throw Reject("COMPLETION_CONNECTION_REJECTED");
        }
        catch (CompletionTargetRejected) { throw; }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            throw Reject("COMPLETION_CONNECTION_REJECTED");
        }
    }

    public static void BeforeDatabaseAccess(CompletionTestTarget target, string? raw, string actualEnvironment)
    {
        ValidateConnection(target, raw, actualEnvironment);
        if (target.ApprovalStatus != "Approved") throw Reject("COMPLETION_TARGET_PENDING");
        // P0 prepares the boundary only. Editing JSON/env/CLI cannot release writes.
        // A later authorized task must implement live identity verification + lifecycle here.
        throw Reject("COMPLETION_P0_EXECUTION_CLOSED");
    }
}
