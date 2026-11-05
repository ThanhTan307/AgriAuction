namespace AgriAuction.Domain.Transactions;

public sealed class TransactionCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid ActorId { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
