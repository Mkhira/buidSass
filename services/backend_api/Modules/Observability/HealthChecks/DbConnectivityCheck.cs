using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.Modules.Observability.HealthChecks;

public sealed class DbConnectivityCheck(AppDbContext dbContext, DbConnectivityProbeGate gate) : IHealthCheck
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(100);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // The gate serializes in-flight probes so that a stuck connector (Npgsql can keep
        // one busy for ~15s after we abandon CanConnectAsync via the WhenAny timeout)
        // doesn't accumulate under repeated probing. The gate is host-scoped (DI singleton),
        // not process-wide, so multiple test hosts in one process don't share state.
        var probeGate = gate.Semaphore;
        if (!await probeGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Another probe is still mid-flight (or hung). Don't pile on the connection pool.
            return HealthCheckResult.Unhealthy("database probe already in flight");
        }

        // Hard wall-clock timeout — Npgsql's connect-handshake doesn't always honor
        // cancellation promptly when the server is paused/unreachable, so we race
        // CanConnectAsync against a Task.Delay and detach the underlying task on timeout.
        // The /health endpoint must return inside its caller-side deadline regardless of
        // the driver's internal connect behavior.
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Deadline);

        var connectTask = dbContext.Database.CanConnectAsync(timeoutCts.Token);
        var timeoutTask = Task.Delay(Deadline, CancellationToken.None);
        var winner = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

        if (winner == timeoutTask)
        {
            // Hold the gate until the abandoned connect task actually finishes — otherwise
            // releasing now would let the next probe pile a fresh stuck connector on top of
            // this one and exhaust the pool. The continuation also disposes the linked CTS
            // and observes any thrown exception.
            _ = connectTask.ContinueWith(static (t, state) =>
            {
                _ = t.Exception;
                var (g, cts) = ((SemaphoreSlim, CancellationTokenSource))state!;
                cts.Dispose();
                g.Release();
            }, (probeGate, timeoutCts), TaskScheduler.Default);
            return HealthCheckResult.Unhealthy("database connectivity check timed out");
        }

        // Normal completion — release the gate and dispose the CTS now.
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
        finally
        {
            timeoutCts.Dispose();
            probeGate.Release();
        }
    }
}
