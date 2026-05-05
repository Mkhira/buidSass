using BackendApi.Modules.B2B.Authorization;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Admin.PublishQuoteVersion;

public static class PublishQuoteVersionEndpoint
{
    public static IEndpointRouteBuilder MapPublishQuoteVersionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/publish", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] PublishQuoteVersionRequest? body,
        HttpContext context,
        PublishQuoteVersionHandler handler,
        CancellationToken ct)
    {
        var user = context.User;
        if (!user.HasClaim("permission", B2BPermissions.QuotesAuthor)
            && !user.HasClaim("permissions", B2BPermissions.QuotesAuthor))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idem)
            || idem.Count == 0 || string.IsNullOrWhiteSpace(idem[0]))
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Idempotency-Key header is required.");
        }

        body ??= new PublishQuoteVersionRequest(null);

        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(sub, out var actorId))
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        var result = await handler.HandleAsync(id, actorId, body, ct);
        if (result.IsSuccess)
        {
            return Results.Ok(result.Response);
        }
        return B2BResponseFactory.Problem(context, result.StatusCode, result.ReasonCode!.Value,
            "Publish rejected.");
    }
}
