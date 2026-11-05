using AgriAuction.Application.Realtime;

namespace AgriAuction.Api.Modules.Realtime;

public sealed class SimulationAuctionWorker(IServiceScopeFactory scopes, ILogger<SimulationAuctionWorker> logger) : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRoomService>().CloseDueBatchAsync(ct);
        await scope.ServiceProvider.GetRequiredService<IRoomOutbox>().DispatchAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Simulation maintenance failed ({Type}); database state retained for retry.", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
