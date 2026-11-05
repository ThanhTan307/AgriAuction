namespace AgriAuction.Domain.Transactions;

public sealed class DepositViolationDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepositId { get; set; }
    public Guid ActorId { get; set; }
    public string Reason { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class DepositSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepositId { get; set; }
    public Guid ActorId { get; set; }
    public Guid? DecisionId { get; set; }
    public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class DepositSettlementCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public Guid RequestId { get; set; }
    public Guid DepositId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
