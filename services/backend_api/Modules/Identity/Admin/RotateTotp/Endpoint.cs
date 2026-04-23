using System.Security.Claims;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.RotateTotp;

public static class RotateTotpEndpoint
{
    public static IEndpointRouteBuilder MapRotateTotpEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/mfa/totp/rotate", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.mfa.reset")
            .RequireStepUp();
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        RotateTotpRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new RotateTotpRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.mfa.rotate.invalid_request",
                "Invalid rotate request",
                validation.Errors.First().ErrorMessage);
        }

        var actorRaw = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorAccountId = Guid.TryParse(actorRaw, out var parsed) ? parsed : (Guid?)null;

        var ok = await RotateTotpHandler.HandleAsync(
            request.FactorId,
            actorAccountId,
            dbContext,
            auditEventPublisher,
            cancellationToken);
        return ok ? Results.NoContent() : Results.NotFound();
    }
}
