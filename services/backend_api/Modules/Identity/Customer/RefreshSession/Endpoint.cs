using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.RefreshSession;

public static class RefreshSessionEndpoint
{
    public static IEndpointRouteBuilder MapRefreshSessionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/session/refresh", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .RequirePermission("identity.customer.self");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        RefreshSessionRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        CustomerAuthSessionService authSessionService,
        IdentityTokenSecretHasher tokenSecretHasher,
        IdentityClientFingerprintHasher fingerprintHasher,
        IRefreshTokenRevocationStore revocationStore,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new RefreshSessionRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.refresh.invalid_request",
                "Invalid refresh request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await RefreshSessionHandler.HandleAsync(
            request,
            context,
            dbContext,
            authSessionService,
            tokenSecretHasher,
            fingerprintHasher,
            revocationStore,
            auditEventPublisher,
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

        return Results.Ok(result.Session);
    }
}
