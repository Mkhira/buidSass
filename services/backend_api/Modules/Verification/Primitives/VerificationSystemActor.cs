namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Stable identity for the verification module's automated/system-driven
/// audit events. The platform <c>AuditEventPublisher</c> rejects
/// <c>Guid.Empty</c> as an actor id (Principle 25 — every audit row must be
/// attributable), so worker- and lifecycle-driven transitions need a
/// reserved-but-deterministic actor id.
///
/// <para>The actor role on the audit row remains <c>"system"</c>; this
/// constant just gives the row a non-empty GUID handle that operators can
/// recognize when filtering audit queries.</para>
/// </summary>
public static class VerificationSystemActor
{
    /// <summary>
    /// Reserved system actor id for verification-module automation
    /// (lifecycle handler + workers). Stable across deploys; recognizable in
    /// audit queries.
    /// </summary>
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000020001");
}
