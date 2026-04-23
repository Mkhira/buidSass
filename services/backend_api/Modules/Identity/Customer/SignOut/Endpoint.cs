using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.SignOut;

public static class SignOutEndpoint
{
    public static IEndpointRouteBuilder MapSignOutEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/sign-out", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .RequirePermission("identity.customer.self");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SignOutRequest? request,
        HttpContext context,
        IdentityDbContext dbContext,
        IdentityTokenSecretHasher tokenSecretHasher,
        IRefreshTokenRevocationStore revocationStore,
        CancellationToken cancellationToken)
    {
        var payload = request ?? new SignOutRequest(null);

        var validator = new SignOutRequestValidator();
        var validation = await validator.ValidateAsync(payload, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.sign_out.invalid_request",
                "Invalid sign-out request",
                validation.Errors.First().ErrorMessage);
        }

        await SignOutHandler.HandleAsync(
            context.User,
            payload,
            dbContext,
            tokenSecretHasher,
            revocationStore,
            cancellationToken);
        return Results.NoContent();
    }
}
