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
}
