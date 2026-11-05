using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace AgriAuction.Api.Configuration;

public static class ProjectSettings
{
    public static string SourceRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgriAuction.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Không tìm thấy thư mục dự án. Hãy chạy từ AgriAuction hoặc cấu hình working directory.");
    }

    public static string ReadLocal(string file, string key)
    {
        var path = Path.Combine(SourceRoot(), ".local", "product", file);
        if (!File.Exists(path)) return "";
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
    }

    public static string Connection(IConfiguration configuration, bool allowLocal)
    {
        var raw = configuration.GetConnectionString("AgriAuction");
        if (string.IsNullOrWhiteSpace(raw) && allowLocal) raw = ReadLocal("database.json", "connectionString");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Chưa cấu hình ConnectionStrings__AgriAuction hoặc .local/product/database.json.");
        var schema = Schema(configuration);
        return new NpgsqlConnectionStringBuilder(raw) { SearchPath = schema, IncludeErrorDetail = false }.ConnectionString;
    }

    public static string Schema(IConfiguration configuration)
    {
        var schema = configuration["Database:Schema"] ?? "agriauction_phase1";
        if (!Regex.IsMatch(schema, "^[a-z][a-z0-9_]{0,62}$") || schema is "public" or "pg_catalog" or "information_schema")
            throw new InvalidOperationException("Database:Schema phải là schema ứng dụng riêng, không phải schema hệ thống/public.");
        return schema;
    }
}
