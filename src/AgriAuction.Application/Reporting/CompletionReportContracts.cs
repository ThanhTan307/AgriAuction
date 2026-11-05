namespace AgriAuction.Application.Reporting;
public sealed record CompletionReport(ReportRange Range,string Mode,string SimulatedRevenue,string Currency,long CompletedInPeriod,long WinningAuctions,long CompletedWinningCohort,decimal? SuccessRatePercent);
public interface ICompletionReportService{Task<CompletionReport> GetAsync(ReportQuery query,Guid actor,string role,CancellationToken ct);}
