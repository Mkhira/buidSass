using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.Modules.Observability.HealthChecks;

public sealed class DbConnectivityCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(400));

            var connected = await dbContext.Database.CanConnectAsync(timeoutCts.Token);
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
