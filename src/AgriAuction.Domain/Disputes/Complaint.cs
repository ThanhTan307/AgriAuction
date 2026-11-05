using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;

namespace AgriAuction.Domain.Disputes;

public static class ComplaintStates
{
    public const string Open = "Open";
    public const string UnderReview = "UnderReview";
    public const string MediationPending = "MediationPending";
    public const string DecisionRecorded = "DecisionRecorded";
    public static readonly string[] All = [Open, UnderReview, MediationPending, DecisionRecorded];
}

// Technical Simulation workflow, never a legal ruling or proof of completed delivery/payment.
public sealed class Complaint
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TransactionId { get; private set; }
    public Guid ComplainantId { get; private set; }
    public Guid RespondentId { get; private set; }
    public string Scope { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Mode { get; private set; } = "Simulation";
    public string State { get; private set; } = ComplaintStates.Open;
    public string? Outcome { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? PolicyProfile { get; private set; }
    public Guid? ReasonId { get; private set; }
    public DateTimeOffset? ComplaintDeadline { get; private set; }
    public DateTimeOffset? ResponseDueAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    private Complaint() { }
    public void UseCompletionPolicy(Guid reasonId,DateTimeOffset? completedAt,DateTimeOffset now)
    {
        if(reasonId==Guid.Empty||completedAt is null||completedAt>now||now>completedAt.Value.AddDays(7))throw new WorkflowConflict("COMPLAINT_WINDOW_CLOSED","Chỉ khiếu nại trong 7 ngày từ CompletedAt.");
        PolicyProfile="D35-Simulation-v1";ReasonId=reasonId;ComplaintDeadline=completedAt.Value.AddDays(7);ResponseDueAt=now.AddDays(3);
    }
    public void ExpireResponse(DateTimeOffset now)
    {
        if(PolicyProfile is null||State!=ComplaintStates.Open||RespondedAt is not null||ResponseDueAt is null||now<ResponseDueAt)throw new WorkflowConflict("RESPONSE_NOT_DUE","Chưa đến hạn chuyển hàng đợi.");
        State=ComplaintStates.UnderReview;Advance(now);
    }

    public static string Text(string? value, string field, int max)
    {
        var text = value?.Trim() ?? "";
        if (text.Length < 1 || text.Length > max || text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw new RuleViolation(new() { [field] = [$"Nhập từ 1 đến {max} ký tự hợp lệ."] });
        return text;
    }
    public static void EvidenceCount(int count)
    {
        if (count is < 1 or > 3) throw new RuleViolation(new() { ["files"] = ["Cần 1–3 ảnh JPEG/PNG (giới hạn tài nguyên Simulation)."] });
    }
    public static Complaint Create(Guid transactionId, Guid complainant, Guid respondent, string? scope,
        string title, string? reason, string? description, int evidenceCount, DateTimeOffset now)
    {
        if (transactionId == Guid.Empty || complainant == Guid.Empty || respondent == Guid.Empty || complainant == respondent)
            throw new WorkflowConflict("INVALID_COMPLAINT_PARTIES", "Hồ sơ cần hai bên liên quan khác nhau.");
        if (scope is not "AuctionRecord" and not "TransactionRecord")
            throw new RuleViolation(new() { ["scope"] = ["Chọn nội dung liên quan phiên hoặc hồ sơ giao dịch Simulation."] });
        EvidenceCount(evidenceCount);
        return new() { TransactionId = transactionId, ComplainantId = complainant, RespondentId = respondent,
            Scope = scope, Title = Text(title, "title", 200), Reason = Text(reason, "reason", 200),
            Description = Text(description, "description", 4000), CreatedAt = now, UpdatedAt = now };
    }
    public void Expect(long expectedVersion)
    {
        if (Version != expectedVersion) throw new WorkflowConflict("VERSION_CONFLICT", "Hồ sơ đã thay đổi. Hãy tải lại và đối chiếu lịch sử.");
    }
    public void RequireActive()
    {
        if (State == ComplaintStates.DecisionRecorded) throw new WorkflowConflict("COMPLAINT_CLOSED", "Đã ghi quyết định Simulation; không thêm hoặc sửa nội dung hồ sơ.");
    }
    private void Advance(DateTimeOffset now) { Version++; UpdatedAt = now; }
    public void Respond(Guid actor, long expectedVersion, int evidenceCount, DateTimeOffset now)
    {
        Expect(expectedVersion); RequireActive(); EvidenceCount(evidenceCount);
        if (actor != ComplainantId && actor != RespondentId) throw new WorkflowConflict("COMPLAINT_PARTICIPANT_REQUIRED", "Chỉ hai bên hồ sơ được giải trình.");
        if(PolicyProfile is not null&&actor==RespondentId)
        {
            if(now>=ResponseDueAt)throw new WorkflowConflict("RESPONSE_WINDOW_CLOSED","Đã hết hạn giải trình 3 ngày.");
            RespondedAt??=now;
        }
        Advance(now);
    }
    public void StartReview(long expectedVersion, DateTimeOffset now)
    {
        Expect(expectedVersion);
        if (State != ComplaintStates.Open) throw new WorkflowConflict("COMPLAINT_REVIEW_STATE", "Chỉ bắt đầu xem xét hồ sơ đang mở.");
        State = ComplaintStates.UnderReview; Advance(now);
    }
    public void Comment(long expectedVersion, DateTimeOffset now) { Expect(expectedVersion); RequireActive(); Advance(now); }
    public void Decide(long expectedVersion, string? outcome, bool confirmedSimulation, DateTimeOffset now)
    {
        Expect(expectedVersion);
        if (!confirmedSimulation) throw new RuleViolation(new() { ["confirmedSimulation"] = ["Phải xác nhận đây chỉ là quyết định Simulation, không có tác động tiền/chế tài."] });
        if (State != ComplaintStates.UnderReview) throw new WorkflowConflict("COMPLAINT_REVIEW_REQUIRED", "Cần bắt đầu xem xét trước khi ghi quyết định Simulation.");
        if (outcome is not "Accept" and not "Reject" and not "Mediate")
            throw new RuleViolation(new() { ["outcome"] = ["Chọn Accept, Reject hoặc Mediate cho Simulation."] });
        Outcome = outcome;
        State = outcome == "Mediate" ? ComplaintStates.MediationPending : ComplaintStates.DecisionRecorded;
        Advance(now);
    }
}

// Append-only narrative/history. Only delivery metadata is updated by the outbox worker.
public sealed class ComplaintEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ComplaintId { get; init; }
    public Guid? ActorId { get; init; }
    public string ActorRole { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
    public string State { get; init; } = "";
    public string? Outcome { get; init; }
    public long Version { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid[] RecipientIds { get; init; } = [];
    public DateTimeOffset? NotifiedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastErrorType { get; set; }
}

public sealed class ComplaintEvidence
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ComplaintId { get; init; }
    public Guid EntryId { get; init; }
    public Guid UploadedBy { get; init; }
    public string ServerFileName { get; init; } = Guid.NewGuid().ToString("N") + ".png";
    public byte[] Data { get; init; } = [];
    public string Sha256 { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
}

public sealed class ComplaintCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ActorId { get; init; }
    public Guid RequestId { get; init; }
    public Guid ComplaintId { get; init; }
    public string PayloadHash { get; init; } = "";
    public string ReceiptJson { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}
