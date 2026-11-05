using System.Text.Json.Serialization;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Lots;
using AgriAuction.Application.Lots;

namespace AgriAuction.Application.Auctions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareAuctionRequest(Guid LotId, long ExpectedLotVersion, SimulationTerms Terms);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishSimulationRequest(long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterAuctionRequest;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmSimulationRequest(long ExpectedVersion, string? Reason);
public sealed record AuctionView(Guid Id, Guid LotId, Guid RevisionId, Guid FarmerId, string FarmerName, string CategoryName,
    string State, bool IsSimulation, long Version, SimulationTerms Terms, LotContent Content, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt, bool RegistrationOpen, IReadOnlyList<ImageView> Images);
public sealed record AuctionPage(IReadOnlyList<AuctionView> Items, int Total, int Page, int PageSize);
public sealed record ApprovedLotOption(Guid Id, string Title, long Version, Guid RevisionId, string FarmerName);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record DepositView(Guid Id, string Mode, string State, decimal Amount, string Currency, DateTimeOffset? ConfirmedAt,
    Guid? ConfirmedBy, string? Reference, string? SettlementKind = null, DateTimeOffset? SettledAt = null);
public sealed record RegistrationEventView(Guid Id, string State, Guid ActorId, DateTimeOffset OccurredAt);
public sealed record RegistrationView(Guid Id, Guid AuctionId, string AuctionTitle, Guid EnterpriseId, string EnterpriseName,
    string State, long Version, DateTimeOffset RegisteredAt, DateTimeOffset? EligibleAt, DepositView Deposit,
    IReadOnlyList<RegistrationEventView> History);
public sealed record RegistrationPage(IReadOnlyList<RegistrationView> Items, int Total, int Page, int PageSize);
public sealed record SimulationConfirmation(Guid RegistrationId, Guid DepositId, Guid LedgerEntryId, string RegistrationState,
    string DepositState, long Version, string Mode, string Reference, DateTimeOffset ConfirmedAt);
public sealed record SimulationInstruction(Guid DepositId, decimal Amount, string Currency);
public sealed record SimulationReceipt(string Provider, string Reference);
// Pure adapter: no payment network and no database changes. A real provider will
// require a separate asynchronous reconciliation/outbox design, not this flow.
public interface IDepositSimulationProvider
{
    SimulationReceipt Confirm(SimulationInstruction instruction);
}
public interface IAuctionService
{
    Task<IReadOnlyList<ApprovedLotOption>> ApprovedLotsAsync(CancellationToken ct);
    Task<AuctionView> PrepareAsync(PrepareAuctionRequest request, Guid actor, CancellationToken ct);
    Task<AuctionView> PublishAsync(Guid id, long version, Guid actor, CancellationToken ct);
    Task<AuctionPage> ListAsync(bool admin, string? query, int page, int pageSize, CancellationToken ct,Guid? categoryId=null,string? location=null,string? state=null);
    Task<AuctionPage> OwnerAsync(Guid actor,int page,int pageSize,CancellationToken ct);
    Task<AuctionView> GetAsync(Guid id, bool admin, CancellationToken ct);
    Task<byte[]> ImageAsync(Guid id, Guid imageId, bool admin, CancellationToken ct);
    Task<RegistrationView> RegisterAsync(Guid id, Guid actor, CancellationToken ct);
    Task<RegistrationView?> MyRegistrationAsync(Guid auctionId, Guid actor, CancellationToken ct);
    Task<RegistrationPage> RegistrationsAsync(Guid actor, bool admin, int page, int pageSize, CancellationToken ct);
    Task<RegistrationView> RegistrationAsync(Guid id, Guid actor, bool admin, CancellationToken ct);
    Task<SimulationConfirmation> ConfirmAsync(Guid id, Guid requestId, ConfirmSimulationRequest request, Guid actor, CancellationToken ct);
}
