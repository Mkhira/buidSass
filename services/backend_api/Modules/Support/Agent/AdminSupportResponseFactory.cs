namespace BackendApi.Modules.Support.Agent;

using BackendApi.Modules.Support.Authorization;
using BackendApi.Modules.Support.Customer;

/// <summary>
/// Admin-side response shaping for the Support module. Mirrors
/// <c>AdminReviewsResponseFactory</c> in spec 022.
/// </summary>
public static class AdminSupportResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string? detail = null,
        IDictionary<string, object?>? extensions = null)
        => SupportResponseFactory.Problem(context, statusCode, reasonCode, title, detail, extensions);

    public static Guid? ResolveActorId(HttpContext context) =>
        SupportResponseFactory.ResolveCustomerId(context);

    public static bool HasAgentPermission(HttpContext context) =>
        HasPermissionClaim(context, SupportPermissions.SupportAgent);

    public static bool HasLeadPermission(HttpContext context) =>
        HasPermissionClaim(context, SupportPermissions.SupportLead);

    public static bool HasSuperAdmin(HttpContext context) =>
        HasPermissionClaim(context, SupportPermissions.SuperAdmin);

    public static bool HasFinanceViewer(HttpContext context) =>
        HasPermissionClaim(context, SupportPermissions.FinanceViewer);

    /// <summary>True when user has agent OR lead OR super_admin permission.</summary>
    public static bool HasAgentLevelAccess(HttpContext context) =>
        HasAgentPermission(context) || HasLeadPermission(context) || HasSuperAdmin(context);

    /// <summary>
    /// Permission claim is exposed as either <c>permission</c>, <c>permissions</c>,
    /// or <c>perm</c> (matches Reviews + Verification conventions).
    /// </summary>
    public static bool HasPermissionClaim(HttpContext context, string permission) =>
        context.User.HasClaim("permission", permission)
        || context.User.HasClaim("permissions", permission)
        || context.User.HasClaim("perm", permission);
}
