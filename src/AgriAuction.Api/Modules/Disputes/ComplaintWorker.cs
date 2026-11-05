using AgriAuction.Application.Disputes;

namespace AgriAuction.Api.Modules.Disputes;

public sealed class ComplaintWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<ComplaintWorker> logger) : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IComplaintOutbox>().DispatchAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Complaint notification maintenance failed ({Type}); committed entries retained.", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
