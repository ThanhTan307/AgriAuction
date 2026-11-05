using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Reporting;
using AgriAuction.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriAuction.Api.Modules.Reporting;

[ApiController, Authorize(Roles = Roles.Administrator), Route("api/v1/admin/reports")]
public sealed class ReportsController(IReportingService reports, IHostEnvironment environment) : ControllerBase
{
    private void Gate() => SimulationPolicy.Require(environment);
    [HttpGet("dashboard")]
    public Task<ReportSummary> Summary([FromQuery] ReportQuery query, CancellationToken ct)
    { Gate(); return reports.SummaryAsync(query, SessionAuthentication.Actor(User), ct); }
    [HttpGet("details")]
    public Task<ReportPage> Detail([FromQuery] ReportQuery query, string dataset = "results", string? state = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    { Gate(); return reports.DetailsAsync(query, dataset, state, page, pageSize, SessionAuthentication.Actor(User), ct); }
    [HttpGet("details.csv")]
    public async Task<IActionResult> Csv([FromQuery] ReportQuery query, string dataset = "results", string? state = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        Gate(); var data = await reports.DetailsAsync(query, dataset, state, page, pageSize, SessionAuthentication.Actor(User), ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(ReportCsv.Export(data), "text/csv; charset=utf-8", $"simulation-{dataset}-{query.From:yyyyMMdd}-{query.Through:yyyyMMdd}-page-{page}.csv");
    }
}
