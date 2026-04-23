using System.Security.Claims;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.ResetAdminMfa;

public static class ResetAdminMfaEndpoint
{
    public static IEndpointRouteBuilder MapResetAdminMfaEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/accounts/mfa/reset", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.mfa.reset")
            .RequireStepUp();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        ResetAdminMfaRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new ResetAdminMfaRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.mfa.reset.invalid_request",
                "Invalid MFA reset request",
                validation.Errors.First().ErrorMessage);
        }

        var actorRaw = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(actorRaw, out var actorAccountId))
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        _ = await ResetAdminMfaHandler.HandleAsync(
            request.AccountId,
            actorAccountId,
            dbContext,
            auditEventPublisher,
            cancellationToken);
        return Results.NoContent();
    }
}
