using System.Security.Claims;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.RevokeInvitation;

public static class RevokeInvitationEndpoint
{
    public static IEndpointRouteBuilder MapRevokeInvitationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapDelete("/invitations/{invitationId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.invitation.revoke")
            .RequireStepUp();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid invitationId,
        HttpContext context,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var request = new RevokeInvitationRequest(invitationId);
        var validator = new RevokeInvitationRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_request",
                "Invalid invitation revoke request",
                validation.Errors.First().ErrorMessage);
        }

        var actorRaw = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorAccountId = Guid.TryParse(actorRaw, out var parsed) ? parsed : Guid.Empty;

        await RevokeInvitationHandler.HandleAsync(request, actorAccountId, dbContext, auditEventPublisher, cancellationToken);
        return Results.NoContent();
    }
}
