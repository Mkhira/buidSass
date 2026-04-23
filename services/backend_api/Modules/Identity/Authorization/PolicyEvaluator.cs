using System.Security.Claims;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BackendApi.Modules.Identity.Authorization;

public sealed class PolicyEvaluator(
    IdentityDbContext dbContext,
    IMemoryCache memoryCache)
{
    private readonly IdentityDbContext _dbContext = dbContext;
    private readonly IMemoryCache _memoryCache = memoryCache;

    public async Task<AuthorizationPolicyDecision> EvaluateAsync(
        ClaimsPrincipal user,
        string permissionCode,
        string? requiredMarketCode = null,
        bool requiresStepUp = false,
        CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return AuthorizationPolicyDecision.Deny(permissionCode, "role_missing");
        }

        var accountId = ResolveAccountId(user);
        if (accountId is null)
        {
            return AuthorizationPolicyDecision.Deny(permissionCode, "role_missing");
        }

        var surface = user.FindFirst("surface")?.Value;
        var permissions = await ResolvePermissionsAsync(accountId.Value, surface, cancellationToken);

        if (!permissions.Contains(permissionCode))
        {
            return AuthorizationPolicyDecision.Deny(permissionCode, "role_missing");
        }

        if (!string.IsNullOrWhiteSpace(requiredMarketCode))
        {
            var currentMarket = user.FindFirst("market_code")?.Value;
            if (!string.Equals(currentMarket, requiredMarketCode, StringComparison.OrdinalIgnoreCase))
            {
                return AuthorizationPolicyDecision.Deny(permissionCode, "market_mismatch");
            }
        }

        if (requiresStepUp)
        {
            var stepUpValidUntilRaw = user.FindFirst("step_up_valid_until")?.Value;
            if (!DateTimeOffset.TryParse(stepUpValidUntilRaw, out var stepUpValidUntil)
                || stepUpValidUntil <= DateTimeOffset.UtcNow)
            {
                return AuthorizationPolicyDecision.Deny(permissionCode, "mfa_not_satisfied");
            }
        }

        return AuthorizationPolicyDecision.Allow(permissionCode);
    }

    public AuthorizationPolicyDecision Evaluate(
        ClaimsPrincipal user,
        string permissionCode,
        string? requiredMarketCode = null,
        bool requiresStepUp = false)
    {
        return EvaluateAsync(
                user,
                permissionCode,
                requiredMarketCode,
                requiresStepUp,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public bool AllowSamplingStrategy() => Random.Shared.NextDouble() <= 0.01d;

    private async Task<HashSet<string>> ResolvePermissionsAsync(
        Guid accountId,
        string? surface,
        CancellationToken cancellationToken)
    {
        var accountVersion = await _dbContext.Accounts
            .IgnoreQueryFilters()
            .Where(x => x.Id == accountId)
            .Select(x => x.PermissionVersion)
            .SingleOrDefaultAsync(cancellationToken);

        var cacheKey = $"identity.permission-set:{accountId:N}:{accountVersion}";
        var cached = await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);

            var resolved = await (
                    from accountRole in _dbContext.AccountRoles
                    join rolePermission in _dbContext.RolePermissions on accountRole.RoleId equals rolePermission.RoleId
                    join permission in _dbContext.Permissions on rolePermission.PermissionId equals permission.Id
                    where accountRole.AccountId == accountId
                    select permission.Code)
                .Distinct()
                .ToListAsync(cancellationToken);

            var permissionSet = new HashSet<string>(resolved, StringComparer.OrdinalIgnoreCase);
            if (string.Equals(surface, "customer", StringComparison.OrdinalIgnoreCase))
            {
                permissionSet.Add("identity.customer.self");
            }

            return permissionSet;
        });

        return cached ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Guid? ResolveAccountId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}

public sealed record AuthorizationPolicyDecision(
    bool IsAllowed,
    string PermissionCode,
    string ReasonCode)
{
    public static AuthorizationPolicyDecision Allow(string permissionCode) =>
        new(true, permissionCode, "ok");

    public static AuthorizationPolicyDecision Deny(string permissionCode, string reasonCode) =>
        new(false, permissionCode, reasonCode);
}
