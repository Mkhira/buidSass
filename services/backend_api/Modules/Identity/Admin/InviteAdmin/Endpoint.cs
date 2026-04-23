using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace BackendApi.Modules.Identity.Admin.InviteAdmin;

public static class InviteAdminEndpoint
{
    public static IEndpointRouteBuilder MapInviteAdminEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/invitations", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.invite")
            .RequireStepUp();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        InviteAdminRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        IIdentityEmailDispatcher emailDispatcher,
        CancellationToken cancellationToken)
    {
        var validator = new InviteAdminRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_request",
                "Invalid invitation request",
                validation.Errors.First().ErrorMessage);
        }

        var actorIdRaw = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(actorIdRaw, out var actorId))
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var result = await InviteAdminHandler.HandleAsync(
            request,
            actorId,
            AdminIdentityResponseFactory.ResolveCorrelationId(context),
            dbContext,
            emailDispatcher,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.Accepted(value: new { invitationId = result.InvitationId });
    }
}
