using AgriAuction.Application.Disputes;
namespace AgriAuction.Api.Modules.Transactions;
public sealed class CompletionMaintenanceWorker(IServiceScopeFactory scopes,ILogger<CompletionMaintenanceWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(2));
        while(await timer.WaitForNextTickAsync(ct))
        {
            try{await using var scope=scopes.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<ICompletionMaintenance>().AdvanceDeadlinesAsync(ct);await scope.ServiceProvider.GetRequiredService<NotificationRealtime>().DispatchAsync(ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Completion maintenance retries after {ErrorType}",e.GetType().Name);}
        }
    }
}
