namespace BackendApi.Modules.Observability.HealthChecks;

/// <summary>
/// Host-scoped gate that serializes in-flight DB connectivity probes. Registered as a
/// singleton in <see cref="Modules.Shared.ModuleRegistrationExtensions.AddObservabilityModule"/>
/// so the gate's lifetime tracks the DI container — process-wide statics would let probes
/// from one test host interfere with another when multiple WebApplicationFactory instances
/// live in the same process.
/// </summary>
public sealed class DbConnectivityProbeGate
{
    public SemaphoreSlim Semaphore { get; } = new(initialCount: 1, maxCount: 1);
}
