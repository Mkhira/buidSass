using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.Me;

public static class AdminMeEndpoint
{
    public static IEndpointRouteBuilder MapAdminMeEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/me", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" });

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var response = await AdminMeHandler.HandleAsync(context.User, dbContext, cancellationToken);
        if (response is null)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        return Results.Ok(response);
    }
}
