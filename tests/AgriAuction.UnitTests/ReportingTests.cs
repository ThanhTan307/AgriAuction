using AgriAuction.Application.Reporting;
using AgriAuction.Domain.Reporting;
using AgriAuction.Domain.Catalog;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class ReportingTests
{
    [Fact] public void LocalDateUsesVietnamMidnightAndExclusiveNextDay()
    {
        var p = ReportPeriod.Create(new(2026, 10, 25), new(2026, 10, 25), ReportPeriod.Zone);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T17:00:00Z"), p.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T17:00:00Z"), p.ToUtc); Assert.Single(p.Dates());
    }
    [Fact] public void LeapYearHas366ExplicitDays()
    {
        var p = ReportPeriod.Create(new(2024, 1, 1), new(2024, 12, 31), ReportPeriod.Zone);
        Assert.Equal(366, p.Dates().Count()); Assert.Contains(new DateOnly(2024, 2, 29), p.Dates());
    }
    [Fact] public void InvalidWindowDoesNotScanUnboundedData()
    {
        foreach (var dates in new[] { (new DateOnly(1999, 1, 1), new DateOnly(1999, 1, 2)), (new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1)), (new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)), (new DateOnly(2100, 1, 1), new DateOnly(2100, 1, 1)) })
            Assert.Throws<RuleViolation>(() => ReportPeriod.Create(dates.Item1, dates.Item2, ReportPeriod.Zone));
    }
    [Theory] [InlineData("UTC")] [InlineData("SE Asia Standard Time")] [InlineData("Asia/Saigon")] [InlineData("")]
    public void OnlyCanonicalTimeZoneAccepted(string zone) => Assert.Throws<RuleViolation>(() => ReportPeriod.Create(new(2026, 1, 1), new(2026, 1, 1), zone));
    [Theory] [InlineData(1, 2, 33.33)] [InlineData(2, 1, 66.67)] [InlineData(0, 1, 0)] [InlineData(1, 0, 100)]
    public void RateUsesClosedResultsRatherThanTransactions(long winner, long none, decimal expected) => Assert.Equal(expected, ReportRules.WinnerRate(winner, none));
    [Fact] public void UndefinedRateIsNotZeroSuccess() { Assert.Null(ReportRules.WinnerRate(0, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => ReportRules.WinnerRate(-1, 0)); }
    [Fact] public void RateDoesNotOverflowLongAddition() => Assert.Equal(50m, ReportRules.WinnerRate(long.MaxValue, long.MaxValue));
    [Theory] [InlineData("lots", "PendingReview")] [InlineData("results", "ClosedWithWinner")] [InlineData("pending", "ComplaintReview")] [InlineData("transactions", "AwaitingPolicy")]
    public void WhitelistedDatasetStatePairs(string dataset, string state) => ReportRules.Detail(dataset, state, 1, 100);
    [Theory] [InlineData("users; DROP TABLE users", "Enabled")] [InlineData("lots", "Paid")] [InlineData("results", "Scheduled")]
    public void WrongDatasetOrStateRejected(string dataset, string state) => Assert.Throws<RuleViolation>(() => ReportRules.Detail(dataset, state, 1, 20));
    [Theory] [InlineData(0, 20)] [InlineData(1, 101)] [InlineData(1000001, 20)]
    public void PaginationBounded(int page, int size) => Assert.Throws<RuleViolation>(() => ReportRules.Detail("results", null, page, size));
    [Theory] [InlineData("=1+1")] [InlineData("  +SUM(1,2)")] [InlineData("\t@SUM(1)")] [InlineData("\uFEFF-1")] [InlineData("\r\n=HYPERLINK(\"x\")")]
    public void CsvPrefixesFormulaLikeText(string input) => Assert.StartsWith("\"'", ReportCsv.Cell(input));
    [Fact] public void CsvQuotesAndFlattensControls() { Assert.Equal("\"a,\"\"b\"\" c\"", ReportCsv.Cell("a,\"b\"\nc")); Assert.Equal("\"\"", ReportCsv.Cell(null)); }
}
