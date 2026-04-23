using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.RevokeSession;

public static class RevokeSessionEndpoint
{
    public static IEndpointRouteBuilder MapRevokeSessionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapDelete("/sessions/{sessionId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .RequirePermission("identity.customer.self");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        HttpContext context,
        IdentityDbContext dbContext,
        IRefreshTokenRevocationStore revocationStore,
        CancellationToken cancellationToken)
    {
        var request = new RevokeSessionRequest(sessionId);
        var validator = new RevokeSessionRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.session.revoke.invalid_request",
                "Invalid session revoke request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await RevokeSessionHandler.HandleAsync(
            context.User,
            request,
            dbContext,
            revocationStore,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        return Results.NoContent();
    }
}
