using BackendApi.Modules.B2B.Primitives;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestRevision;

/// <summary>
/// Spec 021 contract §2.6 — endpoint for
/// <c>POST /api/customer/quotes/{id}/request-revision</c>.
///
/// Reason-code wire surface (matches contract §2.6 + §9):
/// <list type="bullet">
///   <item>200 → success, returns <see cref="RequestRevisionResponse"/>.</item>
///   <item>400 <c>quote.no_changes_provided</c> → comment missing entirely
///         (the buyer pinged the endpoint without saying what to change).</item>
///   <item>400 <c>quote.required_field_missing</c> → missing <c>Idempotency-Key</c>
///         header OR comment present-but-malformed (over-length / both locales empty).</item>
///   <item>404 <c>quote.not_found</c> → quote unknown OR caller has no read+write authority.</item>
///   <item>409 <c>quote.invalid_state_for_action</c> → quote not in <c>revised</c> state,
///         OR optimistic-concurrency race lost.</item>
/// </list>
/// </summary>
public static class RequestRevisionEndpoint
{
    public static IEndpointRouteBuilder MapRequestRevisionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/request-revision", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid id,
        [FromBody] RequestRevisionRequest? body,
        HttpContext context,
        [FromServices] RequestRevisionHandler handler,
        [FromServices] IValidator<RequestRevisionRequest> validator,
        CancellationToken ct)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idemKey)
            || idemKey.Count == 0
            || string.IsNullOrWhiteSpace(idemKey[0]))
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Idempotency-Key header is required.");
        }

        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        // Coerce a missing body to an empty request so the validator's
        // NotNull(Comment) → quote.no_changes_provided rule fires deterministically
        // (instead of a binding error).
        body ??= new RequestRevisionRequest(Comment: null);

        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            var firstFailure = validation.Errors[0];
            var detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{firstFailure.ErrorCode}",
                title = "Request-revision validation failed.",
                status = 400,
                detail,
                instance = context.Request.Path.ToString(),
                reasonCode = firstFailure.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }

        var result = await handler.HandleAsync(id, customerId.Value, body, ct);
        if (result.IsSuccess)
        {
            return Results.Ok(result.Response);
        }
        return B2BResponseFactory.Problem(context,
            result.StatusCode,
            result.ReasonCode!.Value,
            result.StatusCode switch
            {
                404 => "Quote not found.",
                409 => "Quote cannot be revised in its current state.",
                _ => "Request-revision rejected.",
            });
    }
}
