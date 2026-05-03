namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 007-b cross-module hook (data-model §7).
/// Spec 010 checkout reads the in-flight grace window for a deactivated rule
/// from the deactivation event payload on the hot path; this provider is for
/// ad-hoc inspection only.
/// </summary>
public interface ICheckoutGraceWindowProvider
{
    /// <summary>
    /// Returns the configured in-flight grace window (seconds) for a deactivated rule,
    /// or <c>null</c> if no policy is configured for the rule's market.
    /// </summary>
    /// <param name="ruleKind">Either <c>"coupon"</c> or <c>"promotion"</c>.</param>
    Task<int?> GetGraceSecondsAsync(string ruleKind, Guid ruleId, CancellationToken ct);
}
