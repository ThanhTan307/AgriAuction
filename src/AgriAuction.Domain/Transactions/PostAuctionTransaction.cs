using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;

namespace AgriAuction.Domain.Transactions;

// This is a record of a Simulation result, never a payment or delivery receipt.
public sealed class PostAuctionTransaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RoomId { get; private set; }
    public Guid AuctionId { get; private set; }
    public Guid LotId { get; private set; }
    public Guid RevisionId { get; private set; }
    public Guid FarmerId { get; private set; }
    public Guid EnterpriseId { get; private set; }
    public Guid WinningBidId { get; private set; }
    public long ResultVersion { get; private set; }
    public decimal WinningPrice { get; private set; }
    public string Currency { get; private set; } = "";
    public string PriceBasis { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Mode { get; private set; } = "Simulation";
    public string State { get; private set; } = "AwaitingPolicy";
    public string PaymentState { get; private set; } = "PolicyLocked";
    public string DeliveryState { get; private set; } = "PolicyLocked";
    public string? PolicyProfile { get; private set; }
    public DateTimeOffset? PaymentRequestedAt { get; private set; }
    public DateTimeOffset? PaymentConfirmedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? DisputeState { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTimeOffset ClosedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    private PostAuctionTransaction() { }
    public void RecordDispute(string state)
    {
        if(PolicyProfile!="D35-Simulation-v1"||State!="Completed"||state is not ("Open" or "Accepted" or "Rejected" or "MediationPending"))throw new WorkflowConflict("TRADE_DISPUTE_CONFLICT","Hồ sơ không thể ghi trạng thái tranh chấp này.");
        DisputeState=state;Version++;
    }
    public void ActivateCompletion(string profile)
    {
        if (profile != "D35-Simulation-v1" || PolicyProfile is not null || Version != 1 || State != "AwaitingPolicy"
            || Currency != "VND" || PriceBasis != "whole lot simulation")
            throw new WorkflowConflict("PROFILE_PRICE_REJECTED", "Không được mở lifecycle cho hồ sơ legacy hoặc khác cơ sở giá.");
        PolicyProfile = profile; State = "InProgress"; PaymentState = "AwaitingRequest"; DeliveryState = "AwaitingDelivery";
    }

    public void Apply(string action, Guid actor, string role, long expectedVersion, DateTimeOffset now)
    {
        if (PolicyProfile != "D35-Simulation-v1") throw new WorkflowConflict("POLICY_LOCKED", "Hồ sơ legacy chưa có profile đã duyệt.");
        var allowed = action switch
        {
            "RequestPayment" or "Receive" => role == "Enterprise" && actor == EnterpriseId,
            "ConfirmPayment" or "ResolveDeliveryIssue" => role == "Administrator",
            "Deliver" or "Complete" => role == "Farmer" && actor == FarmerId,
            "ReportDeliveryIssue" => (role == "Farmer" && actor == FarmerId) || (role == "Enterprise" && actor == EnterpriseId),
            _ => false
        };
        if (!allowed) throw new WorkflowConflict("TRANSACTION_ACTION_FORBIDDEN", "Tài khoản không có quyền thực hiện thao tác.");
        if (expectedVersion != Version) throw new WorkflowConflict("VERSION_CONFLICT", "Hồ sơ đã thay đổi; hãy tải lại.");
        if (State != "InProgress") throw new WorkflowConflict("TRANSACTION_STATE_CONFLICT", "Hồ sơ chưa thể thực hiện thao tác.");
        switch (action)
        {
            case "RequestPayment" when PaymentState == "AwaitingRequest": PaymentState = "AwaitingConfirmation"; PaymentRequestedAt = now; break;
            case "ConfirmPayment" when PaymentState == "AwaitingConfirmation": PaymentState = "SimulatedConfirmed"; PaymentConfirmedAt = now; break;
            case "Deliver" when DeliveryState == "AwaitingDelivery": DeliveryState = "Delivered"; DeliveredAt = now; break;
            case "Receive" when DeliveryState == "Delivered": DeliveryState = "Received"; ReceivedAt = now; break;
            case "ReportDeliveryIssue" when DeliveryState != "IssueReported": DeliveryState = "IssueReported"; break;
            case "ResolveDeliveryIssue" when DeliveryState == "IssueReported": DeliveryState = "AwaitingDelivery"; DeliveredAt = null; ReceivedAt = null; break;
            case "Complete" when PaymentState == "SimulatedConfirmed" && DeliveryState == "Received": State = "Completed"; CompletedAt = now; break;
            default: throw new WorkflowConflict("TRANSACTION_STATE_CONFLICT", "Chưa đủ điều kiện hoặc thao tác đã được thực hiện.");
        }
        Version++;
    }
    public IReadOnlyList<string> AllowedActions(Guid actor,string role)
    {
        if(PolicyProfile!="D35-Simulation-v1"||State!="InProgress")return [];
        var actions=new List<string>();
        if(role=="Enterprise"&&actor==EnterpriseId){if(PaymentState=="AwaitingRequest")actions.Add("RequestPayment");if(DeliveryState=="Delivered")actions.Add("Receive");}
        if(role=="Farmer"&&actor==FarmerId){if(DeliveryState=="AwaitingDelivery")actions.Add("Deliver");if(DeliveryState=="Received"&&PaymentState=="SimulatedConfirmed")actions.Add("Complete");}
        if(role=="Administrator"){if(PaymentState=="AwaitingConfirmation")actions.Add("ConfirmPayment");if(DeliveryState=="IssueReported")actions.Add("ResolveDeliveryIssue");}
        else if(((role=="Farmer"&&actor==FarmerId)||(role=="Enterprise"&&actor==EnterpriseId))&&DeliveryState!="IssueReported")actions.Add("ReportDeliveryIssue");
        return actions;
    }

    public static PostAuctionTransaction FromResult(SimulationRoom room, SimulationResult result, SimulationBid bid,
        Guid lotId, Guid revisionId, Guid farmerId, string title, string snapshotJson, DateTimeOffset now)
    {
        if (room.Mode != "Simulation" || result.Mode != "Simulation" || room.State != "ClosedWithWinner"
            || result.RoomId != room.Id || result.RoomVersion != room.Version || result.ClosedAt != room.ClosedAt
            || result.ClosedAt < room.EndsAt || result.ClosedAt > now || result.WinningBidId != room.LeadingBidId
            || result.WinnerId != room.LeadingEnterpriseId || result.Amount != room.CurrentPrice
            || bid.Id != result.WinningBidId || bid.RoomId != room.Id || bid.EnterpriseId != result.WinnerId
            || bid.Amount != result.Amount || bid.RoomVersion != result.RoomVersion - 1 || bid.AcceptedAt >= bid.PreviousEndsAt
            || bid.EnterpriseId == farmerId || lotId == Guid.Empty || revisionId == Guid.Empty || farmerId == Guid.Empty
            || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(snapshotJson))
            throw new WorkflowConflict("INVALID_CLOSED_RESULT", "Kết quả Simulation chưa hợp lệ để tạo hồ sơ giao dịch.");
        return new()
        {
            RoomId = room.Id, AuctionId = room.AuctionId, LotId = lotId, RevisionId = revisionId, FarmerId = farmerId,
            EnterpriseId = bid.EnterpriseId, WinningBidId = bid.Id, ResultVersion = result.RoomVersion,
            WinningPrice = bid.Amount, Currency = room.Rules.Currency, PriceBasis = room.Rules.PriceBasis,
            Title = title, SnapshotJson = snapshotJson, ClosedAt = result.ClosedAt, CreatedAt = now
        };
    }
}

public sealed class TransactionHistory
{
    public Guid? ActorId { get; set; }
    public string? Note { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public string Kind { get; set; } = "CreatedFromSimulationResult";
    public string State { get; set; } = "AwaitingPolicy";
    public long Version { get; set; } = 1;
    public DateTimeOffset OccurredAt { get; set; }
}

// Transactional outbox: source is an immutable committed SimulationResult.
public sealed class PostAuctionEvent
{
    public Guid RoomId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorType { get; set; }
}
