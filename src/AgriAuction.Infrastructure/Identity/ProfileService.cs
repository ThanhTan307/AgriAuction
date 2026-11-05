using System.Text.Json;
using AgriAuction.Application.Catalog;
using AgriAuction.Application.Identity;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Identity;

public sealed class ProfileService(AppDbContext db) : IProfileService
{
    public Task<ProfileView> GetAsync(Guid actorId, CancellationToken ct) => db.Users.AsNoTracking().Where(x => x.Id == actorId)
        .Select(x => new ProfileView(x.Id, x.Email, x.DisplayName, x.Role, x.ProfileVersion)).SingleAsync(ct);
    public async Task<ProfileView> UpdateAsync(Guid actorId, UpdateProfileRequest request, CancellationToken ct)
    {
        var name = ProfileRules.DisplayName(request.DisplayName);
        if (request.ExpectedVersion < 1) throw new RuleViolation(new() { ["expectedVersion"] = ["Cần phiên bản hợp lệ."] });
        var account = await db.Users.SingleAsync(x => x.Id == actorId, ct);
        if (account.ProfileVersion != request.ExpectedVersion) throw new VersionConflict();
        var before = JsonSerializer.Serialize(new { account.DisplayName, account.ProfileVersion });
        account.DisplayName = name;
        account.ProfileVersion++;
        db.AuditLogs.Add(new() { ActorId = actorId, EntityId = actorId, Action = "ProfileUpdated", Before = before,
            After = JsonSerializer.Serialize(new { account.DisplayName, account.ProfileVersion }) });
        await db.SaveChangesAsync(ct);
        return new(account.Id, account.Email, account.DisplayName, account.Role, account.ProfileVersion);
    }
}
