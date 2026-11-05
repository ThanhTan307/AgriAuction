using System.Text.Json.Serialization;
namespace AgriAuction.Application.Identity;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterAccountRequest(string Email,string Password,string DisplayName,string Role);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangePasswordRequest(string CurrentPassword,string NewPassword);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewAccountRequest(long ExpectedVersion,string Outcome,string Reason);
public sealed record AccountApprovalView(Guid Id,string Email,string DisplayName,string Role,string? ApprovalState,long ApprovalVersion,long RestrictionVersion=1,DateTimeOffset? BiddingSuspendedUntil=null);
public sealed record AccountReviewView(Guid ActorId,string Outcome,string Reason,long Version,DateTimeOffset OccurredAt);
public interface IAccountWorkflow
{
    Task<AccountApprovalView> RegisterAsync(RegisterAccountRequest request,CancellationToken ct);
    Task ChangePasswordAsync(Guid actor,ChangePasswordRequest request,CancellationToken ct);
    Task<IReadOnlyList<AccountApprovalView>> ListAsync(Guid actor,CancellationToken ct);
    Task<IReadOnlyList<AccountReviewView>> HistoryAsync(Guid id,Guid actor,CancellationToken ct);
    Task<AccountApprovalView> ReviewAsync(Guid id,Guid actor,ReviewAccountRequest request,CancellationToken ct);
}
