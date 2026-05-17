using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T047 — Hangfire-substitute worker that consumes replay jobs queued by the
/// admin <c>POST /webhook-replay</c> endpoint. v1 invokes the provider's
/// events-API inline in the request handler; this worker is the placeholder
/// for the future queue-driven path where replays exceeding 1k events fan out
/// across multiple background invocations.
/// </summary>
public sealed class WebhookReplayWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookReplayWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "WebhookReplayWorker iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    internal Task RunOnceAsync(CancellationToken ct)
    {
        // v1 path: nothing to do; replay runs inline in the admin handler.
        // Phase 1.5 will queue replays here so the admin POST returns 202 immediately.
        _ = scopeFactory;
        return Task.CompletedTask;
    }
}
