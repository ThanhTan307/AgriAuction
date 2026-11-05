using System.Security.Claims;
using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Realtime;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriAuction.Api.Modules.Realtime;

[ApiController, Authorize(Roles = Roles.Administrator), Route("api/v1/admin/auctions/{id:guid}/room")]
public sealed class RoomAdministrationController(IRoomService rooms, IHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Configure(Guid id, ConfigureRoomRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        return StatusCode(201, await rooms.ConfigureAsync(id, request, SessionAuthentication.Actor(User), ct));
    }
    [HttpPost("start")]
    public Task<RoomView> Start(Guid id, StartRoomRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment); return rooms.StartAsync(id, request.ExpectedVersion, SessionAuthentication.Actor(User), ct);
    }
}
[ApiController, Authorize, Route("api/v1/auctions/{id:guid}/room")]
public sealed class RoomController(IRoomService rooms, IHostEnvironment environment) : ControllerBase
{
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;
    [HttpGet]
    public Task<RoomView> Get(Guid id, CancellationToken ct)
    {
        SimulationPolicy.Require(environment); return rooms.SnapshotAsync(id, SessionAuthentication.Actor(User), Role, ct);
    }
    [HttpGet("history")]
    public Task<IReadOnlyList<RoomHistoryItem>> History(Guid id, CancellationToken ct)
    {
        SimulationPolicy.Require(environment); return rooms.HistoryAsync(id, SessionAuthentication.Actor(User), Role, ct);
    }
    [HttpGet("bids")]
    public Task<BidPage> Bids(Guid id,long? beforeVersion,int pageSize=100,CancellationToken ct=default)
    {SimulationPolicy.Require(environment);return rooms.BidsAsync(id,SessionAuthentication.Actor(User),Role,beforeVersion,pageSize,ct);}
    [HttpPost("bids"), Authorize(Roles = Roles.Enterprise)]
    public async Task<IActionResult> Bid(Guid id, PlaceBidRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) || key == Guid.Empty)
            throw new RuleViolation(new() { ["idempotencyKey"] = ["Cần đúng một Idempotency-Key dạng UUID."] });
        return StatusCode(201, await rooms.BidAsync(id, key, request, SessionAuthentication.Actor(User), ct));
    }
}
