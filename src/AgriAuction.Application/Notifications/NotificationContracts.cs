namespace AgriAuction.Application.Notifications;

public sealed record NotificationView(Guid Id, Guid AuctionId, Guid? TransactionId, string Kind, string Title, string Message,
    string Mode, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, Guid? ComplaintId = null);
public sealed record NotificationPage(IReadOnlyList<NotificationView> Items, int TotalCount, int UnreadCount, int Page, int PageSize);
public interface INotificationService
{
    Task<NotificationPage> ListAsync(Guid actor, int page, int pageSize, CancellationToken ct);
    Task<int> UnreadAsync(Guid actor, CancellationToken ct);
    Task<NotificationView> ReadAsync(Guid id, Guid actor, CancellationToken ct);
}
