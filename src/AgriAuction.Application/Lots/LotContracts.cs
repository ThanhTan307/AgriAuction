using System.Text.Json.Serialization;
using AgriAuction.Domain.Lots;

namespace AgriAuction.Application.Lots;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLotRequest(LotContent Content);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLotRequest(LotContent Content, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LotVersionRequest(long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LotDecisionRequest(long ExpectedVersion, Guid ReviewedRevisionId, string? Outcome, string? Reason, IReadOnlyList<Guid>? ChecklistItems = null);
public sealed record ImageView(Guid Id, string Url, int Width, int Height, DateTimeOffset UploadedAt);
public sealed record ReviewView(Guid Id, Guid RevisionId, Guid AdminId, string AdminName, string Outcome, string Reason, DateTimeOffset ReviewedAt);
public sealed record RevisionView(Guid Id, int Number, LotContent Content, DateTimeOffset SubmittedAt, IReadOnlyList<ImageView> Images);
public sealed record LotView(Guid Id, Guid FarmerId, string FarmerName, string CategoryName, string State, long Version, Guid? CurrentRevisionId,
    LotContent Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt,
    IReadOnlyList<ImageView> Images, IReadOnlyList<RevisionView> Revisions, IReadOnlyList<ReviewView> Reviews);
public sealed record LotListItem(Guid Id, string Title, string FarmerName, string State, long Version, DateTimeOffset? SubmittedAt, DateTimeOffset UpdatedAt);
public sealed record LotPage(IReadOnlyList<LotListItem> Items, int Total, int Page, int PageSize);
public sealed record ImagePayload(byte[] Data, int Width, int Height);
public sealed class PermissionDenied : Exception { }

public interface ILotService
{
    Task<LotPage> ListAsync(Guid actorId, bool admin, string? query, int page, int pageSize, CancellationToken ct);
    Task<LotView> GetAsync(Guid id, Guid actorId, bool admin, CancellationToken ct);
    Task<LotView> CreateAsync(CreateLotRequest request, Guid actorId, CancellationToken ct);
    Task<LotView> UpdateAsync(Guid id, UpdateLotRequest request, Guid actorId, CancellationToken ct);
    Task<LotView> SubmitAsync(Guid id, long expectedVersion, Guid actorId, CancellationToken ct);
    Task<LotView> DecideAsync(Guid id, LotDecisionRequest request, Guid actorId, CancellationToken ct);
    Task<LotView> AddImageAsync(Guid id, long expectedVersion, Guid actorId, ImagePayload image, CancellationToken ct);
    Task<LotView> RemoveImageAsync(Guid id, Guid imageId, long expectedVersion, Guid actorId, CancellationToken ct);
    Task<byte[]> ReadImageAsync(Guid imageId, Guid actorId, bool admin, CancellationToken ct);
}

public interface IImageProcessor
{
    Task<ImagePayload> DecodeAsync(Stream input, long length, string fileName, string contentType, CancellationToken ct);
}

public static class ImageLimits
{
    // Resource limits only; not an inspection/quality standard.
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPixels = 16_000_000;
    public const int MaxDimension = 6000;
    public const int MaxImagesPerLot = 10;
}
