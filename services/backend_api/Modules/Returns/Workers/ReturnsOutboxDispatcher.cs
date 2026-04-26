using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Returns.Workers;

/// <summary>
/// FR-008 / FR-010 / Phase H. Polling shell around <see cref="ReturnsOutboxDispatchService"/>.
/// The actual per-tick logic lives there so integration tests (J7, J8) can drive a single
/// tick without spinning up the loop. Production: every 5 s, claim up to 100 entries; on
/// transient failure the entry is left undispatched and re-tried with backoff capped at 5 min.
/// </summary>
public sealed class ReturnsOutboxDispatcher(
    IServiceProvider services,
    ILogger<ReturnsOutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("returns.outbox_dispatcher.started interval={Interval}s",
            PollInterval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int dispatched;
                await using (var scope = services.CreateAsyncScope())
                {
                    var svc = scope.ServiceProvider.GetRequiredService<ReturnsOutboxDispatchService>();
                    dispatched = await svc.DispatchOnceAsync(stoppingToken);
                }
                if (dispatched == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "returns.outbox_dispatcher.error");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }
}
