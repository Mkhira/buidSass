namespace BackendApi.Modules.Support.Lead.OverrideSlaTargets;

/// <summary>
/// Lead-issued SLA target override per spec 023 US4 + FR-026 + FR-039.
/// Recomputes the per-ticket SLA snapshot + due timestamps and clears any
/// breach acknowledgment whose new deadline lies in the future.
/// </summary>
/// <param name="TicketId">The ticket whose SLA targets are being overridden.</param>
/// <param name="LeadActorId">The lead performing the override (audit only).</param>
/// <param name="NewFirstResponseTargetMinutes">Replacement first-response target minutes (must be ≥ 1).</param>
/// <param name="NewResolutionTargetMinutes">
/// Replacement resolution target minutes (must be &gt; <paramref name="NewFirstResponseTargetMinutes"/>).
/// </param>
/// <param name="JustificationNote">FR-026 mandatory justification (≥ 10 chars).</param>
public sealed record OverrideSlaTargetsCommand(
    Guid TicketId,
    Guid LeadActorId,
    int NewFirstResponseTargetMinutes,
    int NewResolutionTargetMinutes,
    string JustificationNote);

public sealed record OverrideSlaTargetsResult(
    bool Success,
    int? PriorFirstResponseTargetMinutes,
    int? PriorResolutionTargetMinutes,
    int? NewFirstResponseTargetMinutes,
    int? NewResolutionTargetMinutes,
    DateTimeOffset? NewFirstResponseDueUtc,
    DateTimeOffset? NewResolutionDueUtc,
    string? ReasonCode,
    string? Detail);
