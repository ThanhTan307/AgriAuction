using AgriAuction.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriAuction.Api.Modules.Identity;

[ApiController, Authorize, Route("api/v1/me/profile")]
public sealed class ProfileController(IProfileService profiles) : ControllerBase
{
    [HttpGet]
    public Task<ProfileView> Get(CancellationToken ct) => profiles.GetAsync(SessionAuthentication.Actor(User), ct);
    [HttpPatch]
    public Task<ProfileView> Update(UpdateProfileRequest request, CancellationToken ct)
        => profiles.UpdateAsync(SessionAuthentication.Actor(User), request, ct);
}
