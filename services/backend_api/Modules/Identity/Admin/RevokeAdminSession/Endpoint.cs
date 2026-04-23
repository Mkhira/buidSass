using System.Security.Claims;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.RevokeAdminSession;

public static class RevokeAdminSessionEndpoint
{
    public static IEndpointRouteBuilder MapRevokeAdminSessionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapDelete("/accounts/{accountId:guid}/sessions/{sessionId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.session.revoke")
            .RequireStepUp();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid accountId,
        Guid sessionId,
        HttpContext context,
        IdentityDbContext dbContext,
        IRefreshTokenRevocationStore revocationStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var request = new RevokeAdminSessionRequest(accountId, sessionId);
        var validator = new RevokeAdminSessionRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.admin.sessions.invalid_request",
                "Invalid admin session revoke request",
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

        await RevokeAdminSessionHandler.HandleAsync(
            request,
            dbContext,
            revocationStore,
            actorAccountId,
            auditEventPublisher,
            cancellationToken);
        return Results.NoContent();
    }
}
