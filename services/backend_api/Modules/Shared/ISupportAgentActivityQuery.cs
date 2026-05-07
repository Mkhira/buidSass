namespace BackendApi.Modules.Shared;

/// <summary>
/// Cross-module read contract for "is this agent currently active in identity?"
/// — consumed by the Support module's <c>OrphanedAssignmentReclaimWorker</c>
/// to detect tickets whose assigned agent has been offboarded.
///
/// <para>The owning module (spec 004 Identity) provides the production binding;
/// at module-load time the Support module registers a fallback that treats
/// every agent as active so the worker is a no-op until Identity wires its
/// real implementation. This mirrors the loose-coupling pattern used by the
/// linked-entity read contracts.</para>
/// </summary>
public interface ISupportAgentActivityQuery
{
    Task<bool> IsAgentActiveAsync(Guid agentId, CancellationToken ct);
}

/// <summary>
/// Default fallback — always returns true so reclaim never fires until
/// spec 004's real binding lands.
/// </summary>
public sealed class NullSupportAgentActivityQuery : ISupportAgentActivityQuery
{
    public Task<bool> IsAgentActiveAsync(Guid agentId, CancellationToken ct) =>
        Task.FromResult(true);
}
