using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.Me;

public static class CustomerMeEndpoint
{
    public static IEndpointRouteBuilder MapCustomerMeEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/me", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .RequirePermission("identity.customer.self");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var response = await CustomerMeHandler.HandleAsync(context.User, dbContext, cancellationToken);
        if (response is null)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        return Results.Ok(response);
    }
}
