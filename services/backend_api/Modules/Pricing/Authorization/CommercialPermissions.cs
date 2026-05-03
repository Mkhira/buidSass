namespace BackendApi.Modules.Pricing.Authorization;

/// <summary>
/// Spec 007-b commercial-authoring permission constants (T039 / FR roles table).
/// Used by [RequirePermission(...)] attributes once admin endpoints land.
/// </summary>
public static class CommercialPermissions
{
    public const string Operator = "commercial.operator";
    public const string B2BAuthoring = "commercial.b2b_authoring";
    public const string Approver = "commercial.approver";
    public const string ThresholdAdmin = "commercial.threshold_admin";

    public static readonly IReadOnlyList<string> AllCommercialPermissions =
    [
        Operator,
        B2BAuthoring,
        Approver,
        ThresholdAdmin,
    ];


    /// <summary>
    /// Canonical "system actor" GUID for Pricing module seed/bootstrap rows that have
    /// no human author (e.g. the initial commercial_thresholds rows shipped via the
    /// 007-b foundation migration). Suffix <c>007</c> is namespaced to spec 007-b,
    /// matching the per-module/per-worker numbering already used elsewhere in the
    /// codebase: Identity admin seed (<c>...001</c>), Catalog ScheduledPublishWorker
    /// (<c>...002</c>), Catalog MediaVariantWorker (<c>...003</c>).
    ///
    /// Principle 25 (audit trail / accountability): seeded rows MUST be attributable
    /// to an identifiable actor rather than a zero GUID, so audit queries can always
    /// resolve "who set this value" — even when the answer is "the system, at install".
    /// </summary>
    public static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000007");
}
