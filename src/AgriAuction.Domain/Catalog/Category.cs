using System.Text.RegularExpressions;

namespace AgriAuction.Domain.Catalog;

public sealed class RuleViolation(Dictionary<string, string[]> errors) : Exception("Dữ liệu không hợp lệ.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public sealed class Category
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool Active { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Category() { }

    public static Category Create(string? code, string? name, bool active)
    {
        var values = Validate(code, name);
        return new Category { Code = values.Code, Name = values.Name, Active = active };
    }

    public void Update(string? code, string? name, bool active)
    {
        var values = Validate(code, name);
        Code = values.Code;
        Name = values.Name;
        Active = active;
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static (string Code, string Name) Validate(string? code, string? name)
    {
        var normalizedCode = (code ?? "").Trim().ToUpperInvariant();
        var normalizedName = (name ?? "").Trim();
        var errors = new Dictionary<string, string[]>();
        if (!Regex.IsMatch(normalizedCode, "^[A-Z0-9][A-Z0-9_-]{0,31}$", RegexOptions.CultureInvariant))
            errors["code"] = ["Mã gồm 1–32 ký tự chữ Latin, số, dấu gạch ngang hoặc gạch dưới."];
        if (normalizedName.Length is < 1 or > 120)
            errors["name"] = ["Tên danh mục phải có từ 1 đến 120 ký tự."];
        if (errors.Count > 0) throw new RuleViolation(errors);
        return (normalizedCode, normalizedName);
    }
}
