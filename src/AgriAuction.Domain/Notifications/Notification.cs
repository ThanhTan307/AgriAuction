namespace AgriAuction.Domain.Notifications;

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientId { get; set; }
    public Guid EventId { get; set; }
    public Guid AuctionId { get; set; }
    public Guid? TransactionId { get; set; }
    public Guid? ComplaintId { get; set; }
    public Guid? PostAuctionSourceId { get; private set; }
    public Guid? ComplaintSourceId { get; private set; }
    public Guid? TransactionSourceId { get; private set; }
    public Guid? DepositSourceId { get; private set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Mode { get; set; } = "Simulation";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? SignaledAt { get; set; }
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
