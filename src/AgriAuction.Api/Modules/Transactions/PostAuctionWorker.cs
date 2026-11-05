using AgriAuction.Application.Transactions;

namespace AgriAuction.Api.Modules.Transactions;

public sealed class PostAuctionWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<PostAuctionWorker> logger) : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return;
        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IPostAuctionProcessor>();
        await processor.RecoverAsync(ct); await processor.ProcessAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Post-auction maintenance failed ({Type}); committed source retained.", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
