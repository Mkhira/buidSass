using BackendApi.Modules.B2B.Authorization;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Admin.AuthorQuoteDraft;

public static class AuthorQuoteDraftEndpoint
{
    public static IEndpointRouteBuilder MapAuthorQuoteDraftEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/draft", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] AuthorQuoteDraftRequest? body,
        HttpContext context,
        AuthorQuoteDraftHandler handler,
        IValidator<AuthorQuoteDraftRequest> validator,
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

        body ??= new AuthorQuoteDraftRequest(null, null, null, null, null);

        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{first.ErrorCode}",
                title = "Quote draft validation failed.",
                status = 400,
                detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)),
                instance = context.Request.Path.ToString(),
                reasonCode = first.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }

        var actorId = ResolveActorId(context);
        if (actorId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        var result = await handler.HandleAsync(id, actorId.Value, body, ct);
        if (result.IsSuccess)
        {
            return Results.Ok(result.Response);
        }
        return B2BResponseFactory.Problem(context, result.StatusCode, result.ReasonCode!.Value,
            "Author draft rejected.");
    }

    private static Guid? ResolveActorId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
