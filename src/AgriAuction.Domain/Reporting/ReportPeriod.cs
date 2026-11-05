using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Disputes;

namespace AgriAuction.Domain.Reporting;

public sealed record ReportPeriod(DateOnly From, DateOnly Through, DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public const string Zone = "Asia/Ho_Chi_Minh";
    public const int MaxDays = 366;
    public static ReportPeriod Create(DateOnly from, DateOnly through, string? timeZone)
    {
        if (timeZone != Zone) throw Invalid("timeZone", "Báo cáo chỉ dùng Asia/Ho_Chi_Minh.");
        if (from.Year < 2000 || through.Year > 2099 || from > through || through.DayNumber - from.DayNumber >= MaxDays)
            throw Invalid("from", "Chọn khoảng ngày hợp lệ từ 2000–2099, tối đa 366 ngày (gồm cả ngày kết thúc).");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        DateTimeOffset Utc(DateOnly day) => new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone));
        return new(from, through, Utc(from), Utc(through.AddDays(1)));
    }
    public IEnumerable<DateOnly> Dates() => Enumerable.Range(0, Through.DayNumber - From.DayNumber + 1).Select(From.AddDays);
    public static RuleViolation Invalid(string field, string text) => new(new() { [field] = [text] });
}

public static class ReportRules
{
    public const int MaxValueGroups = 50;
    public static decimal? WinnerRate(long withWinner, long noWinner)
    {
        if (withWinner < 0 || noWinner < 0) throw new ArgumentOutOfRangeException(nameof(withWinner));
        var closed = (decimal)withWinner + noWinner;
        return closed == 0 ? null : decimal.Round(withWinner * 100m / closed, 2, MidpointRounding.AwayFromZero);
    }
    public static readonly IReadOnlyDictionary<string, string[]> States = new Dictionary<string, string[]>
    {
        ["lots"] = [LotStates.Draft, LotStates.PendingReview, LotStates.Approved, LotStates.Rejected],
        ["auctions"] = ["Draft", "Scheduled"],
        ["rooms"] = ["Prepared", "Open", "ClosedWithWinner", "ClosedNoWinner"],
        ["results"] = ["ClosedWithWinner", "ClosedNoWinner"],
        ["transactions"] = ["AwaitingPolicy","InProgress","Completed"],
        ["complaints"] = ComplaintStates.All,
        ["pending"] = ["LotReview", "DepositConfirmation", "ComplaintReview", "MediationFollowUp"]
    };
    public static void Detail(string dataset, string? state, int page, int size)
    {
        if (!States.TryGetValue(dataset, out var states)) throw ReportPeriod.Invalid("dataset", "Loại báo cáo không hợp lệ.");
        if (state is not null && !states.Contains(state)) throw ReportPeriod.Invalid("state", "Trạng thái không phù hợp loại báo cáo.");
        if (page is < 1 or > 1000000 || size is < 1 or > 100) throw ReportPeriod.Invalid("page", "page 1–1000000, pageSize 1–100.");
    }
}
