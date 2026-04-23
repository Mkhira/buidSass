using System.Security.Claims;
using BackendApi.Modules.Identity.Authorization;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.Common;

public static class AdminStepUpProtectedEndpoint
{
    public static IEndpointRouteBuilder MapAdminStepUpProtectedEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/_test/step-up-protected", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        PolicyEvaluator policyEvaluator,
        IAuthorizationAuditEmitter authorizationAuditEmitter,
        CancellationToken cancellationToken)
    {
        const string permission = "identity.admin.session.manage";
        var decision = policyEvaluator.Evaluate(context.User, permission, requiresStepUp: true);

        var accountId = ResolveAccountId(context.User);
        var correlation = Guid.TryParse(AdminIdentityResponseFactory.ResolveCorrelationId(context), out var parsed)
            ? parsed
            : Guid.NewGuid();

        await authorizationAuditEmitter.EmitDecisionAsync(
            new AuthorizationAuditDecision(
                accountId,
                SurfaceKind.Admin,
                permission,
                decision.IsAllowed ? "allow" : "deny",
                decision.ReasonCode,
                correlation),
            cancellationToken);

        if (!decision.IsAllowed)
        {
            if (string.Equals(decision.ReasonCode, "mfa_not_satisfied", StringComparison.Ordinal))
            {
                return AdminIdentityResponseFactory.Problem(
                    context,
                    StatusCodes.Status412PreconditionFailed,
                    "identity.step_up.required",
                    "Step-up authentication required",
                    "A recent MFA step-up is required for this operation.");
            }

            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status403Forbidden,
                decision.ReasonCode,
                "Authorization denied",
                "Permission check failed.");
        }

        return Results.Ok(new { ok = true });
    }

    private static Guid? ResolveAccountId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
