using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.Modules.Observability.HealthChecks;

public sealed class StorageReachabilityCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "storage");
            Directory.CreateDirectory(root);
            return Task.FromResult(HealthCheckResult.Healthy("storage reachable"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("storage unreachable", ex));
        }
    }
}
