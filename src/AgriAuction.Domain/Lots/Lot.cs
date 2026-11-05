using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgriAuction.Domain.Catalog;

namespace AgriAuction.Domain.Lots;

public sealed class WorkflowConflict(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record LotContent
{
    public Guid CategoryId { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Location { get; init; } = "";
    public decimal Quantity { get; init; }
    public string QuantityUnit { get; init; } = "";
    public decimal Weight { get; init; }
    public string WeightUnit { get; init; } = "";
    public decimal ProposedStartingPrice { get; init; }
    public decimal ProposedIncrement { get; init; }
    public string Currency { get; init; } = "";
    public string PriceBasis { get; init; } = "";
    public DateTimeOffset? ProposedStartsAt { get; init; }
    public DateTimeOffset? ProposedEndsAt { get; init; }
    public string QualityNotes { get; init; } = "";

    public LotContent Validated()
    {
        var errors = new Dictionary<string, string[]>();
        string Text(string? value, string field, int max, bool required = true)
        {
            var text = (value ?? "").Trim();
            if ((required && text.Length == 0) || text.Length > max || text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
                errors[field] = [$"Trường này {(required ? "bắt buộc, " : "")}tối đa {max} ký tự hợp lệ."];
            return text;
        }
        var result = this with
        {
            Title = Text(Title, "title", 160), Description = Text(Description, "description", 4000),
            Location = Text(Location, "location", 250), QuantityUnit = Text(QuantityUnit, "quantityUnit", 40),
            WeightUnit = Text(WeightUnit, "weightUnit", 40), PriceBasis = Text(PriceBasis, "priceBasis", 80),
            Currency = (Currency ?? "").Trim().ToUpperInvariant(), QualityNotes = Text(QualityNotes, "qualityNotes", 4000, false),
            ProposedStartsAt = ProposedStartsAt?.ToUniversalTime(), ProposedEndsAt = ProposedEndsAt?.ToUniversalTime()
        };
        if (CategoryId == Guid.Empty) errors["categoryId"] = ["Vui lòng chọn danh mục đang sử dụng."];
        if (Quantity <= 0 || Quantity > 999999999999999.999m || decimal.Round(Quantity, 3) != Quantity)
            errors["quantity"] = ["Số lượng phải dương, tối đa 15 chữ số nguyên và 3 chữ số thập phân."];
        if (Weight <= 0 || Weight > 999999999999999.999m || decimal.Round(Weight, 3) != Weight)
            errors["weight"] = ["Khối lượng phải dương, tối đa 15 chữ số nguyên và 3 chữ số thập phân."];
        foreach (var (field, value) in new[] { ("proposedStartingPrice", ProposedStartingPrice), ("proposedIncrement", ProposedIncrement) })
            if (value <= 0 || value > 9999999999999999.9999m || decimal.Round(value, 4) != value)
                errors[field] = ["Giá đề xuất phải dương, tối đa 16 chữ số nguyên và 4 chữ số thập phân (giới hạn lưu trữ)."];
        if (!Regex.IsMatch(result.Currency, "^[A-Z]{3}$")) errors["currency"] = ["Nhập mã tiền gồm 3 chữ cái; đây là thông tin đề xuất."];
        if (ProposedStartsAt is null || ProposedEndsAt is null || ProposedStartsAt >= ProposedEndsAt)
            errors["proposedEndsAt"] = ["Cần thời gian đề xuất đầy đủ, thời điểm kết thúc sau thời điểm bắt đầu."];
        if (errors.Count > 0) throw new RuleViolation(errors);
        return result;
    }
}

public static class LotStates
{
    public const string Draft = nameof(Draft), PendingReview = nameof(PendingReview), Approved = nameof(Approved), Rejected = nameof(Rejected);
}

public sealed class Lot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FarmerId { get; private set; }
    public LotContent Content { get; private set; } = new();
    public string State { get; private set; } = LotStates.Draft;
    public long Version { get; private set; } = 1;
    public int SubmissionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; private set; }
    private Lot() { }
    public static Lot Create(Guid farmerId, LotContent content) => new() { FarmerId = farmerId, Content = content.Validated() };
    public void Expect(long version)
    {
        if (version < 1) throw new RuleViolation(new() { ["expectedVersion"] = ["Phiên bản phải lớn hơn 0."] });
        if (Version != version) throw new WorkflowConflict("VERSION_CONFLICT", "Lô đã thay đổi. Hãy tải lại dữ liệu.");
    }
    public void RequireEditable()
    {
        if (State is not LotStates.Draft and not LotStates.Rejected)
            throw new WorkflowConflict("LOT_NOT_EDITABLE", "Chỉ được sửa lô nháp hoặc bị từ chối. Hồ sơ đang duyệt/đã duyệt được giữ nguyên.");
    }
    public void Update(LotContent content)
    {
        RequireEditable();
        var validated = content.Validated();
        Content = validated;
        State = LotStates.Draft;
        Touch();
    }
    public void ImagesChanged() { RequireEditable(); State = LotStates.Draft; Touch(); }
    public LotRevision Submit(int imageCount)
    {
        RequireEditable();
        Content = Content.Validated();
        if (imageCount < 1) throw new RuleViolation(new() { ["images"] = ["Cần ít nhất một ảnh lô hợp lệ trước khi gửi duyệt."] });
        var revision = new LotRevision { LotId = Id, Number = SubmissionNumber + 1, Content = Content with { } };
        CurrentRevisionId = revision.Id;
        SubmissionNumber++;
        State = LotStates.PendingReview;
        SubmittedAt = DateTimeOffset.UtcNow;
        Touch();
        return revision;
    }
    public void Decide(Guid revisionId, string? outcome, string? reason)
    {
        if (State != LotStates.PendingReview || CurrentRevisionId != revisionId)
            throw new WorkflowConflict("REVIEW_STATE_CONFLICT", "Chỉ quyết định trên đúng phiên bản đang chờ duyệt.");
        if (outcome is not "Approve" and not "Reject") throw new RuleViolation(new() { ["outcome"] = ["Chỉ chấp nhận Approve hoặc Reject."] });
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new RuleViolation(new() { ["reason"] = ["Cần ghi nhận xét/lý do từ 1 đến 2000 ký tự cho quyết định."] });
        State = outcome == "Approve" ? LotStates.Approved : LotStates.Rejected;
        Touch();
    }
    private void Touch() { Version++; UpdatedAt = DateTimeOffset.UtcNow; }
}

public sealed class LotRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LotId { get; set; }
    public int Number { get; set; }
    public LotContent Content { get; set; } = new();
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LotImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LotId { get; set; }
    public Guid UploadedBy { get; set; }
    public byte[] Data { get; set; } = [];
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsRemoved { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RevisionImage
{
    public Guid RevisionId { get; set; }
    public Guid ImageId { get; set; }
}

public sealed class LotReview
{
    public string? ChecklistJson { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LotId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid AdminId { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
}
