using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Customer.SetLocale;

public static class SetLocaleEndpoint
{
    public static IEndpointRouteBuilder MapSetLocaleEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapMethods("/locale", ["PATCH"], HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" })
            .RequirePermission("identity.customer.self");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SetLocaleRequest request,
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validator = new SetLocaleRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.register.invalid_request",
                "Invalid locale request",
                validation.Errors.First().ErrorMessage);
        }

        var updated = await SetLocaleHandler.HandleAsync(context.User, request, dbContext, cancellationToken);
        if (!updated)
        {
            return CustomerIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "identity.common.denied",
                "Unauthorized",
                "Authentication is required.");
        }

        return Results.NoContent();
    }
}
