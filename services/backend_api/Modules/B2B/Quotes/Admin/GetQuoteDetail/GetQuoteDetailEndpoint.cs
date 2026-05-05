using BackendApi.Modules.B2B.Authorization;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Admin.GetQuoteDetail;

public static class GetQuoteDetailEndpoint
{
    public static IEndpointRouteBuilder MapGetQuoteDetailEndpoint(this IEndpointRouteBuilder builder)
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
        GetQuoteDetailHandler handler,
        CancellationToken ct)
    {
        var user = context.User;
        var hasPermission =
            user.HasClaim("permission", B2BPermissions.QuotesAuthor)
            || user.HasClaim("permissions", B2BPermissions.QuotesAuthor)
            || user.HasClaim("permission", B2BPermissions.QuotesReview)
            || user.HasClaim("permissions", B2BPermissions.QuotesReview);
        if (!hasPermission)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var resp = await handler.HandleAsync(id, ct);
        if (resp is null)
        {
            return B2BResponseFactory.Problem(context, 404,
                QuoteReasonCode.QuoteNotFound,
                "Quote not found.");
        }
        return Results.Ok(resp);
    }
}
