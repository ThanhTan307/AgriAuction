using System.Security.Claims;
using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Realtime;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgriAuction.Api.Modules.Realtime;

[Authorize]
public sealed class AuctionHub(IRoomService rooms, AppDbContext db, IHostEnvironment environment) : Hub
{
    public static string Group(Guid auctionId) => "simulation:" + auctionId.ToString("N");
    public override async Task OnConnectedAsync()
    {
        SimulationPolicy.Require(environment);
        if (!await SessionAuthentication.IsValid(Context.User, db, Context.ConnectionAborted)) { Context.Abort(); return; }
        await base.OnConnectedAsync();
    }
    public async Task JoinRoom(Guid auctionId)
    {
        if (!await SessionAuthentication.IsValid(Context.User, db, Context.ConnectionAborted)) { Context.Abort(); throw new HubException("SESSION_EXPIRED"); }
        try
        {
            await rooms.SnapshotAsync(auctionId, SessionAuthentication.Actor(Context.User!), Context.User!.FindFirstValue(ClaimTypes.Role)!, Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(auctionId), Context.ConnectionAborted);
        }
        catch (Exception) { throw new HubException("ROOM_ACCESS_DENIED"); }
    }
    public Task LeaveRoom(Guid auctionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(auctionId), Context.ConnectionAborted);
}
public sealed class SignalRRoomNotifier(IHubContext<AuctionHub> hub) : IRoomNotifier
{
    // No price/history/identity in a group message. Revalidate current session and
    // eligibility through the protected snapshot API even after session revocation.
    public Task NotifyAsync(RoomInvalidation change, CancellationToken ct)
        => hub.Clients.Group(AuctionHub.Group(change.AuctionId)).SendAsync("AuctionChanged", change, ct);
}
