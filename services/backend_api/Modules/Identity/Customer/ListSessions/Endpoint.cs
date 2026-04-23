using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.ListSessions;

public static class ListSessionsEndpoint
{
    public static IEndpointRouteBuilder MapListSessionsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/sessions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .RequirePermission("identity.customer.self");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!(Guid.TryParse(context.User.FindFirst("sub")?.Value, out _)
              || Guid.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out _)))
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        var response = await ListSessionsHandler.HandleAsync(context.User, dbContext, cancellationToken);
        return Results.Ok(response);
    }
}
