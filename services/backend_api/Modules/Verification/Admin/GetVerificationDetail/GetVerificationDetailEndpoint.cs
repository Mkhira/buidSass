using BackendApi.Modules.Verification.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Admin.GetVerificationDetail;

public static class GetVerificationDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetVerificationDetailEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        GetVerificationDetailHandler handler,
        CancellationToken ct)
    {
        if (!context.User.HasClaim("permission", VerificationPermissions.Review)
         && !context.User.HasClaim("permissions", VerificationPermissions.Review))
        {
            return AdminVerificationResponseFactory.Problem(
                context, 403,
                "verification.review_permission_required",
                "verification.review permission required.");
        }

        var reviewerMarkets = AdminVerificationResponseFactory.ResolveAssignedMarkets(context);
        var result = await handler.HandleAsync(id, reviewerMarkets, ct);
        if (!result.Exists)
        {
            return AdminVerificationResponseFactory.Problem(
                context, 404,
                "verification.not_found",
                "Verification not found.");
        }
        return Results.Ok(result.Response);
    }
}
