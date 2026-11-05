using System.Text.Json.Serialization;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;

namespace AgriAuction.Application.Transactions;

public sealed record TransactionSnapshot(LotContent ApprovedLot, string CategoryName, SimulationBidRules SimulationRules,
    DateTimeOffset StartedAt, DateTimeOffset EndsAt, DateTimeOffset BidAcceptedAt, string FarmerName, string EnterpriseName);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record TransactionView(Guid Id, Guid RoomId, Guid AuctionId, Guid LotId, Guid RevisionId, Guid FarmerId,
    Guid EnterpriseId, Guid WinningBidId, long ResultVersion, string Title, decimal WinningPrice, string Currency,
    string PriceBasis, string Mode, string State, string PaymentState, string DeliveryState, long Version,
    DateTimeOffset ClosedAt, DateTimeOffset CreatedAt, string? PolicyProfile = null, DateTimeOffset? CompletedAt = null,string? DisputeState=null);
public sealed record TransactionHistoryView(string Kind, string State, long Version, DateTimeOffset OccurredAt, Guid? ActorId = null, string? Note = null);
public sealed record TransactionDetail(TransactionView Transaction, TransactionSnapshot Snapshot, IReadOnlyList<TransactionHistoryView> History,IReadOnlyList<string>? AllowedActions=null);
public sealed record TransactionPage(IReadOnlyList<TransactionView> Items, int TotalCount, int Page, int PageSize);
public interface ITransactionService
{
    Task<TransactionPage> ListAsync(Guid actor, string role, int page, int pageSize, CancellationToken ct);
    Task<TransactionDetail> GetAsync(Guid id, Guid actor, string role, CancellationToken ct);
}
public interface IPostAuctionProcessor
{
    Task<int> RecoverAsync(CancellationToken ct);
    Task<int> ProcessAsync(CancellationToken ct);
}
