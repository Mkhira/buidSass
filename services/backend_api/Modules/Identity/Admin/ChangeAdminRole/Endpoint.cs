using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace BackendApi.Modules.Identity.Admin.ChangeAdminRole;

public static class ChangeAdminRoleEndpoint
{
    public static IEndpointRouteBuilder MapChangeAdminRoleEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapMethods("/accounts/{accountId:guid}/role", ["PATCH"], HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.role.change")
            .RequireStepUp();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid accountId,
        ChangeAdminRoleRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new ChangeAdminRoleRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_request",
                "Invalid role change request",
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

        var result = await ChangeAdminRoleHandler.HandleAsync(
            accountId,
            request,
            actorId,
            dbContext,
            auditEventPublisher,
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

        return Results.NoContent();
    }
}
