using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.Modules.Observability.HealthChecks;

public sealed class DbConnectivityCheck(AppDbContext dbContext) : IHealthCheck
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(100);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Hard wall-clock timeout — Npgsql's connect-handshake doesn't always honor cancellation
        // promptly when the server is paused/unreachable, so we race CanConnectAsync against a
        // Task.Delay and detach the underlying task on timeout. The /health endpoint must return
        // inside its caller-side deadline regardless of the driver's internal connect behavior.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Deadline);

        var connectTask = dbContext.Database.CanConnectAsync(timeoutCts.Token);
        var timeoutTask = Task.Delay(Deadline, CancellationToken.None);
        var winner = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

        if (winner == timeoutTask)
        {
            _ = connectTask.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
            return HealthCheckResult.Unhealthy("database connectivity check timed out");
        }

        try
        {
            var connected = await connectTask.ConfigureAwait(false);
            return connected
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("database connectivity check timed out");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database connectivity check failed", ex);
        }
    }
}
