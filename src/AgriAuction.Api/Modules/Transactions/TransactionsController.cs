using System.Security.Claims;
using System.Text.Json.Serialization;
using AgriAuction.Api.Modules.Auctions;
using AgriAuction.Api.Modules.Identity;
using AgriAuction.Application.Transactions;
using AgriAuction.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriAuction.Api.Modules.Transactions;

[ApiController, Authorize, Route("api/v1/transactions")]
public sealed class TransactionsController(ITransactionService transactions, ITransactionLifecycle lifecycle, IHostEnvironment environment) : ControllerBase
{
    [HttpPost("{id:guid}/actions/{command}")]
    public Task<TransactionReceipt> Act(Guid id, string command, TransactionActionRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        if (!Guid.TryParse(Request.Headers["Idempotency-Key"], out var key)) key = Guid.Empty;
        return lifecycle.ExecuteAsync(id, command, request, key, SessionAuthentication.Actor(User), User.FindFirstValue(ClaimTypes.Role)!, ct);
    }
    [HttpGet]
    public Task<TransactionPage> List(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        SimulationPolicy.Require(environment);
        return transactions.ListAsync(SessionAuthentication.Actor(User), User.FindFirstValue(ClaimTypes.Role)!, page, pageSize, ct);
    }
    [HttpGet("{id:guid}")]
    public Task<TransactionDetail> Get(Guid id, CancellationToken ct)
    {
        SimulationPolicy.Require(environment);
        return transactions.GetAsync(id, SessionAuthentication.Actor(User), User.FindFirstValue(ClaimTypes.Role)!, ct);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarkNotificationReadRequest;

[ApiController, Authorize, Route("api/v1/me/notifications")]
public sealed class NotificationsController(INotificationService notifications, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public Task<NotificationPage> List(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        SimulationPolicy.Require(environment); return notifications.ListAsync(SessionAuthentication.Actor(User), page, pageSize, ct);
    }
    [HttpGet("unread-count")]
    public async Task<object> Count(CancellationToken ct)
    {
        SimulationPolicy.Require(environment); return new { unreadCount = await notifications.UnreadAsync(SessionAuthentication.Actor(User), ct) };
    }
    [HttpPost("{id:guid}/read")]
    public Task<NotificationView> Read(Guid id, MarkNotificationReadRequest request, CancellationToken ct)
    {
        SimulationPolicy.Require(environment); return notifications.ReadAsync(id, SessionAuthentication.Actor(User), ct);
    }
}
