using System.Security.Claims;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace AgriAuction.Api.Modules.Transactions;

[Authorize]
public sealed class NotificationsHub(AppDbContext db):Hub
{
    public override async Task OnConnectedAsync()
    {
        if(!await SessionAuthentication.IsValid(Context.User,db,Context.ConnectionAborted)){Context.Abort();return;}
        await Groups.AddToGroupAsync(Context.ConnectionId,"inbox-session:"+Context.User!.FindFirstValue("sid"));await base.OnConnectedAsync();
    }
}
public sealed class NotificationRealtime(AppDbContext db,IHubContext<NotificationsHub> hub)
{
    public async Task DispatchAsync(CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var rows=await db.Notifications.FromSqlRaw("SELECT * FROM notifications WHERE signaled_at IS NULL ORDER BY created_at,id LIMIT 50 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        var now=await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        foreach(var n in rows)
        {
            var sessions=await db.Sessions.AsNoTracking().Where(s=>s.AccountId==n.RecipientId&&s.RevokedAt==null&&s.ExpiresAt>now&&s.Account.IsEnabled).Select(s=>s.Id).ToListAsync(ct);
            foreach(var sid in sessions)await hub.Clients.Group("inbox-session:"+sid).SendAsync("InboxChanged",new{notificationId=n.Id},ct);
            n.SignaledAt=now;
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
