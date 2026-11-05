using System.Text;
using System.Text.Json.Serialization;

namespace AgriAuction.Application.Reporting;

public sealed record ReportQuery(DateOnly From, DateOnly Through, string TimeZone);
public sealed record ReportRange(DateOnly From, DateOnly Through, DateTimeOffset FromUtc, DateTimeOffset ToExclusiveUtc, string TimeZone);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ReportMetric(string Dimension, string State, string? Role, long Count);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ValueDay(DateOnly Date, long Records, string Total);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ValueGroup(string Source, string Currency, string PriceBasis, long Records, string Total, IReadOnlyList<ValueDay> Days);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ReportSummary(string DefinitionVersion, string Mode, ReportRange Range, DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportMetric> Metrics, long WinningAuctions, long NoWinnerAuctions, decimal? WinnerRatePercent,
    string? ActualRevenue, string ActualRevenueState, IReadOnlyList<ValueGroup> Values);
public sealed record DetailRow(Guid Id, string Kind, string Title, string State, DateTimeOffset OccurredAt, string? Amount, string? Currency, string? PriceBasis);
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record ReportPage(string DefinitionVersion, string Mode, ReportRange Range, DateTimeOffset GeneratedAt, string Dataset,
    string? State, long TotalCount, int Page, int PageSize, IReadOnlyList<DetailRow> Items);

public interface IReportingService
{
    Task<ReportSummary> SummaryAsync(ReportQuery query, Guid actor, CancellationToken ct);
    Task<ReportPage> DetailsAsync(ReportQuery query, string dataset, string? state, int page, int pageSize, Guid actor, CancellationToken ct);
}

public static class ReportCsv
{
    public static string Cell(string? value)
    {
        // Flatten control characters before formula detection, quote every field.
        var cleaned = new string((value ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var leading = cleaned.TrimStart(' ', '\uFEFF');
        if (leading.Length > 0 && leading[0] is '=' or '+' or '-' or '@') cleaned = "'" + cleaned;
        return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
    }
    public static byte[] Export(ReportPage page)
    {
        var lines = new List<string> { "FromLocal,ThroughLocal,TimeZone,GeneratedAtUtc,Mode,Dataset,Id,Kind,Title,State,OccurredAtUtc,RecordedPrice,Currency,PriceBasis" };
        foreach (var row in page.Items)
            lines.Add(string.Join(',', new[] { page.Range.From.ToString("yyyy-MM-dd"), page.Range.Through.ToString("yyyy-MM-dd"), page.Range.TimeZone, page.GeneratedAt.ToString("O"), page.Mode, page.Dataset, row.Id.ToString(), row.Kind, row.Title, row.State, row.OccurredAt.ToString("O"), row.Amount, row.Currency, row.PriceBasis }.Select(Cell)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")];
    }
}
