using System.Text.Json.Serialization;

namespace AgriAuction.Application.Transactions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed record TransactionActionRequest(long ExpectedVersion, string? Note = null);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record TransactionReceipt(Guid TransactionId, Guid CommandId, string Action, long Version, string State,
    string PaymentState, string DeliveryState, decimal Amount, string Currency, DateTimeOffset OccurredAt, DateTimeOffset? CompletedAt);
public interface ITransactionLifecycle
{
    Task<TransactionReceipt> ExecuteAsync(Guid id, string action, TransactionActionRequest request, Guid key, Guid actor, string role, CancellationToken ct);
}
