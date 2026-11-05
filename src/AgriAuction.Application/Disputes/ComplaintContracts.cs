using System.Text.Json.Serialization;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Transactions;

namespace AgriAuction.Application.Disputes;

public sealed record CreateComplaintRequest(Guid TransactionId, string? Scope, string? Reason, string? Description, Guid? ReasonId=null);
public sealed record ComplaintResponseRequest(long ExpectedVersion, string? Text);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ComplaintReviewRequest(long ExpectedVersion, string? Text);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ComplaintDecisionRequest(long ExpectedVersion, string? Outcome, string? Text, bool ConfirmedSimulation);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ComplaintReceipt(Guid ComplaintId, Guid EntryId, long Version, string State);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ComplaintView(Guid Id, Guid TransactionId, Guid ComplainantId, Guid RespondentId, string Title,
    string Scope, string Reason, string Description, string Mode, string State, string? Outcome, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ComplaintDeadline=null,DateTimeOffset? ResponseDueAt=null,DateTimeOffset? RespondedAt=null);
public sealed record EvidenceView(Guid Id, Guid EntryId, Guid UploadedBy, string Url, string ServerFileName, int Width, int Height, string Sha256, DateTimeOffset UploadedAt);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ComplaintEntryView(Guid Id, Guid? ActorId, string ActorRole, string Kind, string Text, string State,
    string? Outcome, long Version, DateTimeOffset OccurredAt, IReadOnlyList<EvidenceView> Evidence);
public sealed record ComplaintPage(IReadOnlyList<ComplaintView> Items, int TotalCount, int Page, int PageSize);
public sealed record ComplaintEntryPage(IReadOnlyList<ComplaintEntryView> Items, int TotalCount, int Page, int PageSize);
public sealed record ComplaintDetail(ComplaintView Complaint, TransactionDetail Transaction, ComplaintEntryPage Entries);
public sealed record EvidenceContent(byte[] Data, string ServerFileName);

public interface IComplaintService
{
    Task AuthorizeTargetAsync(Guid id, Guid actor, string role, CancellationToken ct);
    Task AuthorizeCaseAsync(Guid id, Guid actor, string role, CancellationToken ct);
    Task<ComplaintPage> ListAsync(Guid actor, string role, string? state, string? scope, string? query, int page, int pageSize, CancellationToken ct);
    Task<ComplaintDetail> GetAsync(Guid id, Guid actor, string role, CancellationToken ct);
    Task<ComplaintEntryPage> EntriesAsync(Guid id, Guid actor, string role, int page, int pageSize, CancellationToken ct);
    Task<EvidenceContent> EvidenceAsync(Guid id, Guid actor, string role, CancellationToken ct);
    Task<ComplaintReceipt> CreateAsync(CreateComplaintRequest request, IReadOnlyList<ImagePayload> images, Guid key, Guid actor, string role, CancellationToken ct);
    Task<ComplaintReceipt> RespondAsync(Guid id, ComplaintResponseRequest request, IReadOnlyList<ImagePayload> images, Guid key, Guid actor, string role, CancellationToken ct);
    Task<ComplaintReceipt> ReviewAsync(Guid id, ComplaintReviewRequest request, Guid key, Guid actor, string role, CancellationToken ct);
    Task<ComplaintReceipt> CommentAsync(Guid id, ComplaintReviewRequest request, Guid key, Guid actor, string role, CancellationToken ct);
    Task<ComplaintReceipt> DecideAsync(Guid id, ComplaintDecisionRequest request, Guid key, Guid actor, string role, CancellationToken ct);
}
public interface IComplaintOutbox { Task<int> DispatchAsync(CancellationToken ct); }
