using System.Text.Json.Serialization;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Lots;
using AgriAuction.Application.Lots;

namespace AgriAuction.Application.Realtime;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigureRoomRequest(long ExpectedAuctionVersion, SimulationBidRules Rules);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed record StartRoomRequest(long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlaceBidRequest([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Amount);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record BidView(Guid Id, string Bidder, bool IsMine, decimal Amount, long Version, DateTimeOffset AcceptedAt, bool Extended);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record RoomView(Guid AuctionId, Guid RoomId, string Title, string Mode, string State, long Version, DateTimeOffset ServerTime,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal? CurrentPrice, decimal MinimumBid, string? Leader,
    bool CanBid, string Eligibility, int BidCount, int ExtensionCount, SimulationBidRules Rules, IReadOnlyList<BidView> Bids,
    SimulationResultView? Result, LotContent Content, string CategoryName, IReadOnlyList<ImageView> Images);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record SimulationResultView(string? Winner, Guid? WinningBidId, decimal? Amount, DateTimeOffset ClosedAt, long Version, string Mode);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record BidReceipt(Guid BidId, Guid AuctionId, long Version, decimal Amount, DateTimeOffset EndsAt, string Mode);
public sealed record RoomHistoryItem(Guid Id, string Kind, string Version, DateTimeOffset OccurredAt, Guid? ActorId);
public sealed record BidPage(IReadOnlyList<BidView> Items,string? NextBeforeVersion);
public interface IRoomService
{
    Task<RoomView> ConfigureAsync(Guid auctionId, ConfigureRoomRequest request, Guid actor, CancellationToken ct);
    Task<RoomView> StartAsync(Guid auctionId, long version, Guid actor, CancellationToken ct);
    Task<RoomView> SnapshotAsync(Guid auctionId, Guid actor, string role, CancellationToken ct);
    Task<IReadOnlyList<RoomHistoryItem>> HistoryAsync(Guid auctionId, Guid actor, string role, CancellationToken ct);
    Task<BidPage> BidsAsync(Guid auctionId,Guid actor,string role,long? beforeVersion,int pageSize,CancellationToken ct);
    Task<BidReceipt> BidAsync(Guid auctionId, Guid key, PlaceBidRequest request, Guid actor, CancellationToken ct);
    Task<bool> CloseDueAsync(Guid auctionId, CancellationToken ct);
    Task<int> CloseDueBatchAsync(CancellationToken ct);
}
public sealed record RoomInvalidation(Guid AuctionId, string Version, Guid EventId);
public interface IRoomNotifier { Task NotifyAsync(RoomInvalidation change, CancellationToken ct); }
public interface IRoomOutbox { Task<int> DispatchAsync(CancellationToken ct); }
