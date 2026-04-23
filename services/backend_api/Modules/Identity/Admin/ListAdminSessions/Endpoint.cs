using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.ListAdminSessions;

public static class ListAdminSessionsEndpoint
{
    public static IEndpointRouteBuilder MapListAdminSessionsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/accounts/{accountId:guid}/sessions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.session.manage")
            .RequireStepUp();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid accountId,
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var request = new ListAdminSessionsRequest(accountId);
        var validator = new ListAdminSessionsRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.admin.sessions.invalid_request",
                "Invalid admin sessions request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await ListAdminSessionsHandler.HandleAsync(context.User, request, dbContext, cancellationToken);
        return Results.Ok(result);
    }
}
