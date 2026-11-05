using System.Security.Claims;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Api.Modules.Identity;

public static class SessionAuthentication
{
    public const string CookieScheme = "WebCookie";
    public const string BearerScheme = "Bearer";
    public static Guid Actor(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static async Task<bool> IsValid(ClaimsPrincipal? principal, AppDbContext db, CancellationToken ct)
    {
        if (principal is null || !Guid.TryParse(principal.FindFirstValue("sid"), out var id)
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId)) return false;
        var role = principal.FindFirstValue(ClaimTypes.Role);
        var now = DateTimeOffset.UtcNow;
        return await db.Sessions.AsNoTracking().AnyAsync(x => x.Id == id && x.AccountId == accountId
            && x.RevokedAt == null && x.ExpiresAt > now && x.Account.IsEnabled && x.Account.Role == role, ct);
    }
}
