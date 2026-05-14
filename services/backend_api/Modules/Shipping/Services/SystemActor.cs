namespace BackendApi.Modules.Shipping.Services;

/// <summary>
/// Stable sentinel actor ids used in audit rows produced by automated paths
/// (provider webhooks, hosted workers). Audit-log readers can join these
/// uuids against a system-actor lookup table when the dashboard surfaces
/// the row.
/// </summary>
public static class SystemActor
{
    /// <summary>Actor id used when a provider webhook drives the transition.</summary>
    public static readonly Guid WebhookSystemActorId =
        new("00000000-0000-0000-0000-000000005026");

    /// <summary>Actor id used by Shipping hosted workers (label dispatch, SLA monitor, etc.).</summary>
    public static readonly Guid WorkerSystemActorId =
        new("00000000-0000-0000-0000-000000005027");
}
