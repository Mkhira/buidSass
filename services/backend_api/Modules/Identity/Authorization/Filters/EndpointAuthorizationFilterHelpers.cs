using System.Security.Claims;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Primitives;

namespace BackendApi.Modules.Identity.Authorization.Filters;

internal static class EndpointAuthorizationFilterHelpers
{
    public static Guid? ResolveAccountId(ClaimsPrincipal principal)
    {
        var accountIdRaw = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(accountIdRaw, out var parsedAccountId) ? parsedAccountId : null;
    }

    public static SurfaceKind ResolveSurface(ClaimsPrincipal principal)
    {
        return string.Equals(principal.FindFirstValue("surface"), "admin", StringComparison.OrdinalIgnoreCase)
            ? SurfaceKind.Admin
            : SurfaceKind.Customer;
    }

    public static Guid ResolveCorrelationId(HttpContext context)
    {
        return Guid.TryParse(AdminIdentityResponseFactory.ResolveCorrelationId(context), out var parsed)
            ? parsed
            : Guid.NewGuid();
    }

    public static IResult BuildDenyResult(HttpContext context, AuthorizationPolicyDecision decision, bool stepUpFailure)
    {
        var isAdmin = ResolveSurface(context.User) == SurfaceKind.Admin;
        if (stepUpFailure)
        {
            return isAdmin
                ? AdminIdentityResponseFactory.Problem(
                    context,
                    StatusCodes.Status412PreconditionFailed,
                    "identity.step_up.required",
                    "Step-up authentication required",
                    "A recent MFA step-up is required for this operation.")
                : CustomerIdentityResponseFactory.Problem(
                    context,
                    StatusCodes.Status412PreconditionFailed,
                    "identity.step_up.required",
                    "Step-up authentication required",
                    "A recent MFA step-up is required for this operation.");
        }

        return isAdmin
            ? AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status403Forbidden,
                decision.ReasonCode,
                "Authorization denied",
                "Permission check failed.")
            : CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status403Forbidden,
                decision.ReasonCode,
                "Authorization denied",
                "Permission check failed.");
    }
}
