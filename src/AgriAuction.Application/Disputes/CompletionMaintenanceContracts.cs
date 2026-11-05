namespace AgriAuction.Application.Disputes;
public interface ICompletionMaintenance { Task<int> AdvanceDeadlinesAsync(CancellationToken ct); }
