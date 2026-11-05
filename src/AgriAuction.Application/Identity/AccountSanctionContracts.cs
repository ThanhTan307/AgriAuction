namespace AgriAuction.Application.Identity;
public sealed record AccountSanctionRequest(Guid DecisionEntryId,long ExpectedVersion,string Kind,string Reason,DateTimeOffset? Until);
public sealed record AccountSanctionView(Guid Id,Guid AccountId,string Kind,string Reason,DateTimeOffset? Until,long Version,DateTimeOffset OccurredAt);
public interface IAccountSanctionService
{
    Task<IReadOnlyList<AccountSanctionView>> ListAsync(Guid account,Guid actor,CancellationToken ct);
    Task<AccountSanctionView> ApplyAsync(Guid account,AccountSanctionRequest request,Guid key,Guid actor,CancellationToken ct);
}
