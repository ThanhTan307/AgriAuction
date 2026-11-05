using AgriAuction.Domain.Catalog;

namespace AgriAuction.Domain.Identity;

public static class ProfileRules
{
    public static string DisplayName(string? value)
    {
        var name = (value ?? "").Trim();
        if (name.Length is < 1 or > 120 || name.Any(char.IsControl))
            throw new RuleViolation(new() { ["displayName"] = ["Tên hiển thị cần 1–120 ký tự và không có ký tự điều khiển."] });
        return name;
    }
}
