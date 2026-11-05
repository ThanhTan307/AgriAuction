using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Application.Identity;
using AgriAuction.Application.Transactions;
using AgriAuction.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace AgriAuction.Api.Modules.Identity;

[ApiController,Route("api/v1")]
public sealed class AccountWorkflowController(IAccountWorkflow accounts,IAccountSanctionService sanctions,CompletionProfile profile,IHostEnvironment environment):ControllerBase
{
    private void Require(){SimulationPolicy.Require(environment);if(!profile.Enabled)throw new AgriAuction.Domain.Lots.WorkflowConflict("POLICY_LOCKED","Chức năng chỉ mở trong profile Completion.");}
    [AllowAnonymous,HttpPost("auth/register"),EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterAccountRequest request,CancellationToken ct){Require();return StatusCode(201,await accounts.RegisterAsync(request,ct));}
    [Authorize,HttpPost("me/change-password")]
    public async Task<IActionResult> Password(ChangePasswordRequest request,CancellationToken ct){Require();await accounts.ChangePasswordAsync(SessionAuthentication.Actor(User),request,ct);await HttpContext.SignOutAsync(SessionAuthentication.CookieScheme);return NoContent();}
    [Authorize(Roles=Roles.Administrator),HttpGet("admin/accounts")]
    public Task<IReadOnlyList<AccountApprovalView>> List(CancellationToken ct){Require();return accounts.ListAsync(SessionAuthentication.Actor(User),ct);}
    [Authorize(Roles=Roles.Administrator),HttpGet("admin/accounts/{id:guid}/history")]
    public Task<IReadOnlyList<AccountReviewView>> History(Guid id,CancellationToken ct){Require();return accounts.HistoryAsync(id,SessionAuthentication.Actor(User),ct);}
    [Authorize(Roles=Roles.Administrator),HttpPost("admin/accounts/{id:guid}/review")]
    public Task<AccountApprovalView> Review(Guid id,ReviewAccountRequest request,CancellationToken ct){Require();return accounts.ReviewAsync(id,SessionAuthentication.Actor(User),request,ct);}
    [Authorize(Roles=Roles.Administrator),HttpPost("admin/accounts/{id:guid}/sanctions")]
    public Task<AccountSanctionView> Sanction(Guid id,AccountSanctionRequest request,CancellationToken ct){Require();if(!Guid.TryParse(Request.Headers["Idempotency-Key"],out var key))key=Guid.Empty;return sanctions.ApplyAsync(id,request,key,SessionAuthentication.Actor(User),ct);}
    [Authorize(Roles=Roles.Administrator),HttpGet("admin/accounts/{id:guid}/sanctions")]
    public Task<IReadOnlyList<AccountSanctionView>> Sanctions(Guid id,CancellationToken ct){Require();return sanctions.ListAsync(id,SessionAuthentication.Actor(User),ct);}
}
