using System.Text.Json.Serialization;

namespace AgriAuction.Application.Identity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProfileRequest(string? DisplayName, long ExpectedVersion);
public sealed record ProfileView(Guid Id, string Email, string DisplayName, string Role, long Version);
public interface IProfileService
{
    Task<ProfileView> GetAsync(Guid actorId, CancellationToken ct);
    Task<ProfileView> UpdateAsync(Guid actorId, UpdateProfileRequest request, CancellationToken ct);
}
