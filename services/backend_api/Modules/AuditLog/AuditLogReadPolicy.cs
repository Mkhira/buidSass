using Microsoft.AspNetCore.Authorization;

namespace BackendApi.Modules.AuditLog;

public static class AuditLogReadPolicy
{
    // TODO(spec-005): Re-enable this policy when /audit-log HTTP read endpoint is introduced.
    public const string Name = "AuditLogReadPolicy";

    public static AuthorizationPolicy Build()
    {
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole("AR", "AW", "AS")
            .Build();
    }
}
