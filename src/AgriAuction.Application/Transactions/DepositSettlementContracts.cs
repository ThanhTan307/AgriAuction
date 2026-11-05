using System.Text.Json.Serialization;
namespace AgriAuction.Application.Transactions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DepositDispositionRequest(string? Reason = null, string? EvidenceReference = null, Guid? DecisionId = null);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString)]
public sealed record DepositDispositionReceipt(Guid DepositId, Guid RecordId, string Kind, decimal Amount, string Currency, DateTimeOffset OccurredAt);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString)]
public sealed record SettlementCandidate(Guid DepositId, Guid AuctionId, Guid EnterpriseId, decimal Amount, string Currency, string Title, string? ResultState, string? TradeState, bool IsWinner, string? SettlementKind,
    string EnterpriseName, DateTimeOffset? ClosedAt, DateTimeOffset? CompletedAt, DateTimeOffset? EligibleAt);
public interface IDepositSettlementService
{
    Task<IReadOnlyList<SettlementCandidate>> ListAsync(Guid actor, CancellationToken ct);
    Task<DepositDispositionReceipt> ExecuteAsync(Guid id, string action, DepositDispositionRequest request, Guid key, Guid actor, CancellationToken ct);
}
