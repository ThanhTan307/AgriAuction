using System.Data;
using AgriAuction.Application.Lots;
using AgriAuction.Application.Reporting;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Reporting;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AgriAuction.Infrastructure.Reporting;

public sealed class ReportingService(AppDbContext db) : IReportingService
{
    public const string DefinitionVersion = "simulation-dashboard-v1";
    private async Task Authorize(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.Role == Roles.Administrator && u.IsEnabled, ct)) throw new PermissionDenied();
    }
    private static ReportRange Range(ReportPeriod p) => new(p.From, p.Through, p.FromUtc, p.ToUtc, ReportPeriod.Zone);
    private static object[] Parameters(ReportPeriod p) => [new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = p.FromUtc }, new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = p.ToUtc }];
    private async Task<DateTimeOffset> Begin(Guid actor, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '5s'", ct);
        await Authorize(actor, ct);
        return await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    }
    public async Task<ReportSummary> SummaryAsync(ReportQuery query, Guid actor, CancellationToken ct)
    {
        var period = ReportPeriod.Create(query.From, query.Through, query.TimeZone);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var now = await Begin(actor, ct);
        var metrics = await db.Database.SqlQueryRaw<MetricData>(ReportingSql.Metrics, Parameters(period)).ToListAsync(ct);
        var totals = await db.Database.SqlQueryRaw<TotalData>(ReportingSql.Totals, Parameters(period)).ToListAsync(ct);
        if (totals.Select(t => (t.Currency, t.PriceBasis)).Distinct().Count() > ReportRules.MaxValueGroups || totals.Count > 100)
            throw ReportPeriod.Invalid("from", "Kỳ có hơn 50 nhóm currency/priceBasis; hãy thu hẹp khoảng ngày. Không tự gộp hoặc bỏ nhóm.");
        var daily = await db.Database.SqlQueryRaw<DailyData>(ReportingSql.Daily, Parameters(period)).ToListAsync(ct);
        var byGroup = daily.GroupBy(d => (d.Source, d.Currency, d.PriceBasis)).ToDictionary(g => g.Key, g => g.ToDictionary(d => d.Date));
        var values = totals.Select(t =>
        {
            byGroup.TryGetValue((t.Source, t.Currency, t.PriceBasis), out var days);
            return new ValueGroup(t.Source, t.Currency, t.PriceBasis, t.Records, t.Total,
                period.Dates().Select(date => days is not null && days.TryGetValue(date, out var d) ? new ValueDay(date, d.Records, d.Total) : new ValueDay(date, 0, "0.0000")).ToList());
        }).ToList();
        var winner = metrics.Where(m => m.Dimension == "Results" && m.State == "ClosedWithWinner").Sum(m => m.Count);
        var noWinner = metrics.Where(m => m.Dimension == "Results" && m.State == "ClosedNoWinner").Sum(m => m.Count);
        await tx.CommitAsync(ct);
        return new(DefinitionVersion, "Simulation", Range(period), now, metrics.Select(m => new ReportMetric(m.Dimension, m.State, m.Role, m.Count)).ToList(),
            winner, noWinner, ReportRules.WinnerRate(winner, noWinner), null, "UnavailablePolicyLocked", values);
    }
    public async Task<ReportPage> DetailsAsync(ReportQuery query, string dataset, string? state, int page, int pageSize, Guid actor, CancellationToken ct)
    {
        ReportRules.Detail(dataset, state, page, pageSize); var period = ReportPeriod.Create(query.From, query.Through, query.TimeZone);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var now = await Begin(actor, ct);
        // SQL fragments come only from the validated static dataset allowlist.
        var filterColumn = dataset == "pending" ? "kind" : "state";
        var source = "WITH detail AS (" + ReportingSql.Detail(dataset) + ") ";
        var predicate = " WHERE (@state IS NULL OR " + filterColumn + " = @state)";
        object[] WithState() => [.. Parameters(period), new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = state is null ? DBNull.Value : state }];
        var countSql = source + "SELECT count(*) AS \"Value\" FROM detail" + predicate;
        var rowsSql = source + """
            SELECT id AS "Id",kind AS "Kind",title AS "Title",state AS "State",occurred_at AS "OccurredAt",
                   amount AS "Amount",currency AS "Currency",price_basis AS "PriceBasis" FROM detail
            """ + predicate + " ORDER BY occurred_at DESC,id DESC OFFSET @skip LIMIT @take";
        var count = await db.Database.SqlQueryRaw<long>(countSql, WithState()).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<DetailData>(rowsSql, [.. WithState(), new NpgsqlParameter("skip", (page - 1) * pageSize), new NpgsqlParameter("take", pageSize)]).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(DefinitionVersion, "Simulation", Range(period), now, dataset, state, count, page, pageSize,
            rows.Select(r => new DetailRow(r.Id, r.Kind, r.Title, r.State, r.OccurredAt, r.Amount, r.Currency, r.PriceBasis)).ToList());
    }
}

public sealed class MetricData
{
    public string Dimension { get; set; } = "";
    public string State { get; set; } = "";
    public string? Role { get; set; }
    public long Count { get; set; }
}
public sealed class TotalData
{
    public string Source { get; set; } = "";
    public string Currency { get; set; } = "";
    public string PriceBasis { get; set; } = "";
    public long Records { get; set; }
    public string Total { get; set; } = "";
}
public sealed class DailyData
{
    public string Source { get; set; } = "";
    public string Currency { get; set; } = "";
    public string PriceBasis { get; set; } = "";
    public DateOnly Date { get; set; }
    public long Records { get; set; }
    public string Total { get; set; } = "";
}
public sealed class DetailData
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? PriceBasis { get; set; }
}
