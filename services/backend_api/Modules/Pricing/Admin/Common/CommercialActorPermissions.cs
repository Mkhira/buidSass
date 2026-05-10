using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Pricing.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Common;

/// <summary>
/// Resolves which commercial-authoring permissions the calling actor holds.
///
/// The Admin JWT (spec 004) does not embed the full permission set as claims —
/// permissions are evaluated server-side by <see cref="Identity.Authorization.PolicyEvaluator"/>
/// via <see cref="IdentityDbContext"/>. For 007-b's chord-style RBAC checks
/// (e.g. <c>commercial.approver</c> required for promote-to-shared in §6.2,
/// <c>super_admin</c> required to view personal profiles owned by another
/// actor) we mirror the same DB read here. Per-request caching via
/// <see cref="HttpContext.Items"/> avoids repeating the JOIN inside a single
/// request.
/// </summary>
public sealed class CommercialActorPermissions
{
    private readonly IdentityDbContext _identity;

    public CommercialActorPermissions(IdentityDbContext identity)
    {
        _identity = identity;
    }

    private const string CacheKey = "commercial.actor_permissions";

    public async Task<IReadOnlySet<string>> ResolveAsync(HttpContext context, CancellationToken ct)
    {
        if (context.Items.TryGetValue(CacheKey, out var cached) && cached is HashSet<string> set)
        {
            return set;
        }

        var accountId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        if (accountId == Guid.Empty)
        {
            var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            context.Items[CacheKey] = empty;
            return empty;
        }

        var permissions = await (
                from accountRole in _identity.AccountRoles
                join rolePermission in _identity.RolePermissions on accountRole.RoleId equals rolePermission.RoleId
                join permission in _identity.Permissions on rolePermission.PermissionId equals permission.Id
                where accountRole.AccountId == accountId
                select permission.Code)
            .Distinct()
            .ToListAsync(ct);

        var result = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        context.Items[CacheKey] = result;
        return result;
    }

    public async Task<bool> HasSuperAdminAsync(HttpContext context, CancellationToken ct)
    {
        var perms = await ResolveAsync(context, ct);
        return perms.Contains("super_admin");
    }

    public async Task<bool> HasApproverOrSuperAdminAsync(HttpContext context, CancellationToken ct)
    {
        var perms = await ResolveAsync(context, ct);
        return perms.Contains("super_admin") || perms.Contains(CommercialPermissions.Approver);
    }
}
