using System.Security.Claims;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Application.Reporting;
using AgriAuction.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AgriAuction.Api.Modules.Transactions;
[ApiController,Authorize(Roles="Administrator,Farmer")]
public sealed class CompletionReportsController(ICompletionReportService reports,CompletionProfile profile,IHostEnvironment environment):ControllerBase
{
    [HttpGet("api/v1/reports/completion")]
    [HttpGet("api/v1/me/reports/completion")]
    public Task<CompletionReport> Get(DateOnly from,DateOnly through,CancellationToken ct)
    {
        SimulationPolicy.Require(environment);if(!profile.Enabled)throw new AgriAuction.Domain.Lots.WorkflowConflict("POLICY_LOCKED","Báo cáo Completion chưa mở.");
        return reports.GetAsync(new(from,through,"Asia/Ho_Chi_Minh"),SessionAuthentication.Actor(User),User.FindFirstValue(ClaimTypes.Role)!,ct);
    }
}
