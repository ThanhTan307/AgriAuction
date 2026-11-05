using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Lots;

namespace AgriAuction.Domain.Realtime;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record SimulationBidRules
{
    public const decimal MaxAmount = 9999999999999999.9999m;
    public decimal StartingPrice { get; init; }
    public decimal MinimumIncrement { get; init; }
    public string Currency { get; init; } = "";
    public string PriceBasis { get; init; } = "";
    public string FirstBidRule { get; init; } = "";
    public string IncrementRule { get; init; } = "";
    public string TieRule { get; init; } = "";
    public string ExtensionRule { get; init; } = "";
    public int? ExtensionWindowSeconds { get; init; }
    public int? ExtensionSeconds { get; init; }
    public int? MaxExtensions { get; init; }
    public string CloseRule { get; init; } = "";
    public static bool ValidAmount(decimal value) => value > 0 && value <= MaxAmount && decimal.Round(value, 4) == value;
    public SimulationBidRules Validated()
    {
        var errors = new Dictionary<string, string[]>();
        var result = this with { Currency = (Currency ?? "").Trim().ToUpperInvariant(), PriceBasis = (PriceBasis ?? "").Trim() };
        if (!ValidAmount(StartingPrice)) errors["startingPrice"] = ["Giá Simulation phải dương, tối đa 16 chữ số nguyên và 4 chữ số thập phân."];
        if (!ValidAmount(MinimumIncrement)) errors["minimumIncrement"] = ["Bước Simulation phải dương và đúng giới hạn lưu trữ."];
        if (!Regex.IsMatch(result.Currency, "^[A-Z]{3}$")) errors["currency"] = ["Nhập rõ mã tiền giả định ba chữ cái."];
        if (result.PriceBasis.Length is < 1 or > 80 || result.PriceBasis.Any(char.IsControl)) errors["priceBasis"] = ["Nhập rõ cơ sở tính giá Simulation, tối đa 80 ký tự."];
        if (FirstBidRule != "AtLeastStartingPrice") errors["firstBidRule"] = ["Cần chọn AtLeastStartingPrice (chỉ Simulation)."];
        if (IncrementRule is not "MinimumIncrease" and not "StartingPriceGrid") errors["incrementRule"] = ["Chọn MinimumIncrease hoặc StartingPriceGrid."];
        if (TieRule != "FirstCommittedWins") errors["tieRule"] = ["Cần chọn FirstCommittedWins; giá bằng giá đã commit bị từ chối."];
        if (CloseRule != "AtOrAfterDeadline") errors["closeRule"] = ["Cần chọn AtOrAfterDeadline theo giờ database."];
        if (ExtensionRule is not "Disabled" and not "FromBidTime" and not "AddToDeadline") errors["extensionRule"] = ["Chọn rõ công thức gia hạn Simulation."];
        if (ExtensionWindowSeconds is null or < 0 or > 86400 || ExtensionSeconds is null or < 0 or > 86400 || MaxExtensions is null or < 0 or > 1000)
            errors["extensionSeconds"] = ["Nhập đủ window/seconds 0–86400 và cap 0–1000 (giới hạn tài nguyên demo)."];
        if (ExtensionRule == "Disabled" && (ExtensionWindowSeconds != 0 || ExtensionSeconds != 0 || MaxExtensions != 0)) errors["extensionRule"] = ["Disabled yêu cầu cả ba tham số bằng 0 tường minh."];
        if (ExtensionRule != "Disabled" && (ExtensionWindowSeconds <= 0 || ExtensionSeconds <= 0 || MaxExtensions <= 0)) errors["extensionRule"] = ["Gia hạn bật cần window, seconds và cap dương."];
        if (errors.Count > 0) throw new RuleViolation(errors);
        return result;
    }
}

public sealed class SimulationRoom
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AuctionId { get; private set; }
    public Guid ConfiguredBy { get; private set; }
    public Guid? StartedBy { get; private set; }
    public string Mode { get; private set; } = "Simulation";
    public string State { get; private set; } = "Prepared";
    public long Version { get; private set; } = 1;
    public SimulationBidRules Rules { get; private set; } = new();
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset InitialEndsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset ConfiguredAt { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? LeadingBidId { get; private set; }
    public Guid? LeadingEnterpriseId { get; private set; }
    public decimal? CurrentPrice { get; private set; }
    public int BidCount { get; private set; }
    public int ExtensionCount { get; private set; }
    private SimulationRoom() { }
    public static SimulationRoom Configure(Guid auctionId, Guid admin, SimulationBidRules rules, DateTimeOffset starts, DateTimeOffset ends, DateTimeOffset now)
    {
        if (starts >= ends || ends <= now) throw new WorkflowConflict("ROOM_SCHEDULE_INVALID", "Lịch Phase 3 không còn hợp lệ để cấu hình phòng.");
        return new() { AuctionId = auctionId, ConfiguredBy = admin, Rules = rules.Validated(), StartsAt = starts, InitialEndsAt = ends, EndsAt = ends, ConfiguredAt = now };
    }
    public void Expect(long expected)
    {
        if (expected < 1) throw new RuleViolation(new() { ["expectedVersion"] = ["Phiên bản phải dương."] });
        if (expected != Version) throw new WorkflowConflict("VERSION_CONFLICT", "Phòng đã thay đổi. Hãy tải lại.");
    }
    public void Start(Guid admin, DateTimeOffset now)
    {
        if (State != "Prepared" || now < StartsAt || now >= EndsAt) throw new WorkflowConflict("ROOM_CANNOT_START", "Cần phòng Prepared và thời gian trong lịch Phase 3. Không tự đổi lịch.");
        State = "Open"; StartedBy = admin; OpenedAt = now; Version++;
    }
    public decimal MinimumBid => CurrentPrice.HasValue ? CurrentPrice.Value + Rules.MinimumIncrement : Rules.StartingPrice;
    public SimulationBid Accept(Guid enterprise, decimal amount, DateTimeOffset now)
    {
        if (State != "Open" || now < StartsAt || now >= EndsAt) throw new WorkflowConflict("BIDDING_CLOSED", "Phòng chưa mở hoặc đã hết hạn theo giờ database.");
        if (!SimulationBidRules.ValidAmount(amount)) throw new RuleViolation(new() { ["amount"] = ["Giá phải dương, tối đa 16 chữ số nguyên và 4 chữ số thập phân."] });
        if (amount < MinimumBid) throw new WorkflowConflict("BID_TOO_LOW", "Giá thấp hơn mức tối thiểu mới nhất; giá đồng mức không vượt lệnh đã commit.");
        if (Rules.IncrementRule == "StartingPriceGrid" && (amount - Rules.StartingPrice) % Rules.MinimumIncrement != 0)
            throw new RuleViolation(new() { ["amount"] = ["Giá phải nằm trên lưới startingPrice + n × minimumIncrement."] });
        var previousEnd = EndsAt;
        if (Rules.ExtensionRule != "Disabled" && ExtensionCount < Rules.MaxExtensions && (EndsAt - now).TotalSeconds <= Rules.ExtensionWindowSeconds)
        {
            DateTimeOffset next;
            try { next = Rules.ExtensionRule == "AddToDeadline" ? EndsAt.AddSeconds(Rules.ExtensionSeconds!.Value) : now.AddSeconds(Rules.ExtensionSeconds!.Value); }
            catch (ArgumentOutOfRangeException) { throw new WorkflowConflict("SIMULATION_TIME_LIMIT", "Giới hạn lưu trữ thời gian, không thể nhận lệnh gia hạn."); }
            if (next > EndsAt) { EndsAt = next; ExtensionCount++; }
        }
        Version++; BidCount++; CurrentPrice = amount;
        var bid = new SimulationBid { RoomId = Id, EnterpriseId = enterprise, Amount = amount, RoomVersion = Version, AcceptedAt = now, PreviousEndsAt = previousEnd, EndsAt = EndsAt, Extended = EndsAt > previousEnd };
        LeadingBidId = bid.Id; LeadingEnterpriseId = enterprise;
        return bid;
    }
    public SimulationResult? CloseIfDue(DateTimeOffset now)
    {
        if (State != "Open" || now < EndsAt) return null;
        State = LeadingBidId.HasValue ? "ClosedWithWinner" : "ClosedNoWinner"; ClosedAt = now; Version++;
        return new() { RoomId = Id, WinningBidId = LeadingBidId, WinnerId = LeadingEnterpriseId, Amount = CurrentPrice, ClosedAt = now, RoomVersion = Version };
    }
}
public sealed class SimulationBid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid EnterpriseId { get; set; }
    public decimal Amount { get; set; }
    public long RoomVersion { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset PreviousEndsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool Extended { get; set; }
}
public sealed class SimulationResult
{
    public Guid RoomId { get; set; }
    public Guid? WinningBidId { get; set; }
    public Guid? WinnerId { get; set; }
    public decimal? Amount { get; set; }
    public string Mode { get; set; } = "Simulation";
    public DateTimeOffset ClosedAt { get; set; }
    public long RoomVersion { get; set; }
}
public sealed class SimulationBidCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public Guid RequestId { get; set; }
    public Guid RoomId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
}
public sealed class SimulationRoomEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid AuctionId { get; set; }
    public long Version { get; set; }
    public string Kind { get; set; } = "";
    public Guid? ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Details { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
}
