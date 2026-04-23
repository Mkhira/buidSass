using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Identity.Admin.ListAdminMfaFactors;

public static class ListAdminMfaFactorsEndpoint
{
    public static IEndpointRouteBuilder MapListAdminMfaFactorsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapGet("/accounts/{accountId:guid}/mfa/factors", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("identity.admin.mfa.reset")
            .RequireStepUp();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid accountId,
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var request = new ListAdminMfaFactorsRequest(accountId);
        var validator = new ListAdminMfaFactorsRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminIdentityResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "identity.mfa.factor_not_found",
                "Invalid MFA factors request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await ListAdminMfaFactorsHandler.HandleAsync(request, dbContext, cancellationToken);
        return Results.Ok(result);
    }
}
