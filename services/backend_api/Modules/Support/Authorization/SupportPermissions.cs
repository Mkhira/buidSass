namespace BackendApi.Modules.Support.Authorization;

/// <summary>
/// RBAC permission constants owned by spec 023. Spec 004's
/// <c>[RequirePermission(...)]</c> attribute consumes these strings.
/// </summary>
public static class SupportPermissions
{
    public const string SupportAgent = "support.agent";
    public const string SupportLead = "support.lead";
    public const string SuperAdmin = "super_admin";
    public const string FinanceViewer = "viewer.finance";
    public const string ReviewModerator = "reviews.moderator";
    public const string B2BAccountManager = "b2b.account_manager";
}
