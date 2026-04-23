using System.Security.Claims;
using BackendApi.Modules.Identity.Authorization;
using BackendApi.Modules.Identity.Primitives;

namespace BackendApi.Modules.Identity.Admin.Common;

public static class AdminSuperAdminGuard
{
    public static async Task<(bool IsAllowed, Guid? ActorId, IResult? DenyResult)> RequireAdminPrivilegedAsync(
        HttpContext context,
        PolicyEvaluator policyEvaluator,
        IAuthorizationAuditEmitter authorizationAuditEmitter,
        string permissionCode,
        bool requireStepUp,
        CancellationToken cancellationToken)
    {
        var decision = await policyEvaluator.EvaluateAsync(
            context.User,
            permissionCode,
            requiresStepUp: requireStepUp,
            cancellationToken: cancellationToken);

        var actorIdRaw = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorId = Guid.TryParse(actorIdRaw, out var parsedActor) ? parsedActor : (Guid?)null;

        var correlationId = Guid.TryParse(AdminIdentityResponseFactory.ResolveCorrelationId(context), out var parsedCorrelation)
            ? parsedCorrelation
            : Guid.NewGuid();

        await authorizationAuditEmitter.EmitDecisionAsync(
            new AuthorizationAuditDecision(
                actorId,
                SurfaceKind.Admin,
                decision.PermissionCode,
                decision.IsAllowed ? "allow" : "deny",
                decision.ReasonCode,
                correlationId),
            cancellationToken);

        if (!decision.IsAllowed)
        {
            var deny = string.Equals(decision.ReasonCode, "mfa_not_satisfied", StringComparison.Ordinal)
                ? AdminIdentityResponseFactory.Problem(
                    context,
                    StatusCodes.Status412PreconditionFailed,
                    "identity.step_up.required",
                    "Step-up authentication required",
                    "A recent MFA step-up is required for this operation.")
                : AdminIdentityResponseFactory.Problem(
                    context,
                    StatusCodes.Status403Forbidden,
                    decision.ReasonCode,
                    "Authorization denied",
                    "Permission check failed.");

            return (false, actorId, deny);
        }

        if (actorId is null)
        {
            return (
                false,
                actorId,
                AdminIdentityResponseFactory.Problem(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "identity.common.denied",
                    "Unauthorized",
                    "Authentication is required."));
        }

        return (true, actorId, null);
    }

    public static Task<(bool IsAllowed, Guid? ActorId, IResult? DenyResult)> AuthorizeAsync(
        HttpContext context,
        PolicyEvaluator policyEvaluator,
        IAuthorizationAuditEmitter authorizationAuditEmitter,
        CancellationToken cancellationToken) =>
        RequireAdminPrivilegedAsync(
            context,
            policyEvaluator,
            authorizationAuditEmitter,
            permissionCode: "identity.admin.session.manage",
            requireStepUp: true,
            cancellationToken);
}
