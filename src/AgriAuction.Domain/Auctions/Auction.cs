using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;

namespace AgriAuction.Domain.Auctions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record SimulationTerms
{
    public DateTimeOffset RegistrationOpensAt { get; init; }
    public DateTimeOffset RegistrationClosesAt { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public decimal DepositAmount { get; init; }
    public string Currency { get; init; } = "";
    public SimulationTerms Validated()
    {
        var errors = new Dictionary<string, string[]>();
        var value = this with { Currency = (Currency ?? "").Trim().ToUpperInvariant(), RegistrationOpensAt = RegistrationOpensAt.ToUniversalTime(),
            RegistrationClosesAt = RegistrationClosesAt.ToUniversalTime(), StartsAt = StartsAt.ToUniversalTime(), EndsAt = EndsAt.ToUniversalTime() };
        if (RegistrationOpensAt == default || RegistrationClosesAt == default || StartsAt == default || EndsAt == default ||
            RegistrationOpensAt >= RegistrationClosesAt || RegistrationClosesAt > StartsAt || StartsAt >= EndsAt)
            errors["registrationClosesAt"] = ["Cần đủ lịch: mở đăng ký < hết hạn đăng ký ≤ bắt đầu < kết thúc (UTC)."];
        if (DepositAmount <= 0 || DepositAmount > 9999999999999999.9999m || decimal.Round(DepositAmount, 4) != DepositAmount)
            errors["depositAmount"] = ["Số cọc giả định phải dương; giới hạn lưu trữ 16 chữ số nguyên và 4 chữ số thập phân."];
        if (!Regex.IsMatch(value.Currency, "^[A-Z]{3}$")) errors["currency"] = ["Nhập rõ mã tiền giả định gồm 3 chữ cái."];
        if (errors.Count != 0) throw new RuleViolation(errors);
        return value;
    }
}

public sealed class Auction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid LotId { get; private set; }
    public Guid RevisionId { get; private set; }
    public Guid FarmerId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public string State { get; private set; } = "Draft";
    public bool IsSimulation { get; private set; } = true;
    public string? PolicyProfile { get; private set; }
    public long Version { get; private set; } = 1;
    public SimulationTerms Terms { get; private set; } = new();
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    private Auction() { }
    public void UseCompletionProfile(string profile)
    {
        if (State != "Draft" || PolicyProfile is not null || profile != "D35-Simulation-v1" || Terms.Currency != "VND")
            throw new WorkflowConflict("PROFILE_CONFLICT", "Profile chỉ gắn một lần cho phiên nháp VND mới.");
        PolicyProfile = profile;
    }
    public static Auction Prepare(Lot lot, Guid admin, SimulationTerms terms, DateTimeOffset now)
    {
        if (lot.State != LotStates.Approved || lot.CurrentRevisionId is null)
            throw new WorkflowConflict("LOT_NOT_APPROVED", "Chỉ lô đã duyệt mới được chuẩn bị phiên.");
        var valid = terms.Validated();
        if (valid.StartsAt <= now || valid.RegistrationClosesAt <= now)
            throw new RuleViolation(new() { ["startsAt"] = ["Lịch bắt đầu và hạn đăng ký phải ở tương lai."] });
        return new() { LotId = lot.Id, RevisionId = lot.CurrentRevisionId.Value, FarmerId = lot.FarmerId, CreatedBy = admin, Terms = valid, CreatedAt = now };
    }
    public void Expect(long version)
    {
        if (version < 1) throw new RuleViolation(new() { ["expectedVersion"] = ["Phiên bản phải dương."] });
        if (Version != version) throw new WorkflowConflict("VERSION_CONFLICT", "Phiên đã thay đổi; hãy tải lại.");
    }
    public void Publish(Guid admin, DateTimeOffset now)
    {
        if (State != "Draft") throw new WorkflowConflict("AUCTION_STATE_CONFLICT", "Chỉ công bố bản nháp một lần.");
        if (Terms.RegistrationClosesAt <= now || Terms.StartsAt <= now)
            throw new WorkflowConflict("REGISTRATION_CLOSED", "Lịch đã hết hiệu lực. Không thể công bố.");
        State = "Scheduled"; PublishedBy = admin; PublishedAt = now; Version++;
    }
    public void RequireRegistrationWindow(DateTimeOffset now)
    {
        if (!IsSimulation || State != "Scheduled" || PublishedAt is null)
            throw new WorkflowConflict("AUCTION_NOT_PUBLISHED", "Phiên chưa được công bố Simulation.");
        if (now < Terms.RegistrationOpensAt || now >= Terms.RegistrationClosesAt)
            throw new WorkflowConflict("REGISTRATION_CLOSED", "Ngoài thời gian đăng ký/xác nhận mô phỏng của phiên.");
    }
}

public sealed class AuctionRegistration
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AuctionId { get; private set; }
    public Guid EnterpriseId { get; private set; }
    public string State { get; private set; } = "PendingDeposit";
    public long Version { get; private set; } = 1;
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset? EligibleAt { get; private set; }
    private AuctionRegistration() { }
    public static AuctionRegistration Register(Auction auction, Guid enterpriseId, DateTimeOffset now)
    {
        auction.RequireRegistrationWindow(now);
        if (enterpriseId == auction.FarmerId) throw new WorkflowConflict("OWNER_CANNOT_REGISTER", "Chủ lô không được đăng ký phiên của mình.");
        return new() { AuctionId = auction.Id, EnterpriseId = enterpriseId, RegisteredAt = now };
    }
    public void ConfirmSimulation(long expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion < 1) throw new RuleViolation(new() { ["expectedVersion"] = ["Phiên bản phải dương."] });
        if (Version != expectedVersion) throw new WorkflowConflict("VERSION_CONFLICT", "Đăng ký đã thay đổi; hãy tải lại.");
        if (State != "PendingDeposit") throw new WorkflowConflict("SIMULATION_ALREADY_CONFIRMED", "Đăng ký đã được xác nhận mô phỏng.");
        State = "SimulationEligible"; EligibleAt = now; Version++;
    }
}

// These records represent a simulation only. There is no wallet or real balance.
public sealed class SimulationDeposit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RegistrationId { get; set; }
    public string Mode { get; set; } = "Simulation";
    public string State { get; set; } = "AwaitingSimulation";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
}
public sealed class RegistrationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RegistrationId { get; set; }
    public string State { get; set; } = "";
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class SimulationLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepositId { get; set; }
    public Guid ActorId { get; set; }
    public string Kind { get; set; } = "SimulatedConfirmation";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Provider { get; set; } = "Simulation";
    public string Reference { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class DepositCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public Guid RequestId { get; set; }
    public Guid RegistrationId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
}
