using BackendApi.Modules.B2B.Primitives;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.WithdrawQuote;

/// <summary>
/// Spec 021 contract §2.5 — endpoint for
/// <c>POST /api/customer/quotes/{id}/withdraw</c>. State-transitioning POST,
/// so it requires:
/// <list type="bullet">
///   <item><c>CustomerJwt</c> auth scheme (401 otherwise).</item>
///   <item><c>Idempotency-Key</c> header (400 <c>quote.required_field_missing</c> otherwise) —
///         per contract §1. Replay storage is deferred to Cycle C-3 alongside SubmitAcceptance;
///         this Cycle enforces presence so future retries are safe by construction.</item>
///   <item>FluentValidation pre-flight on the optional body.</item>
/// </list>
///
/// Reason-code wire surface:
/// <list type="bullet">
///   <item>200 → success, returns <see cref="WithdrawQuoteResponse"/>.</item>
///   <item>400 <c>quote.required_field_missing</c> → missing <c>Idempotency-Key</c> header
///         OR validator failure on body.</item>
///   <item>404 <c>quote.not_found</c> → quote unknown OR caller has no read+write authority
///         (visibility-leak prevention; same path covers both branches).</item>
///   <item>409 <c>quote.invalid_state_for_action</c> → quote is in a terminal state, OR
///         optimistic-concurrency race lost.</item>
/// </list>
/// </summary>
public static class WithdrawQuoteEndpoint
{
    public static IEndpointRouteBuilder MapWithdrawQuoteEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/withdraw", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid id,
        [FromBody] WithdrawQuoteRequest? body,
        HttpContext context,
        [FromServices] WithdrawQuoteHandler handler,
        [FromServices] IValidator<WithdrawQuoteRequest> validator,
        CancellationToken ct)
    {
        // ---------- Idempotency-Key header gate (§1) ----------
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

        // Empty body is legitimate (§2.5 doesn't require a body); coerce null to a
        // default-shaped DTO so the validator + handler don't have to defend.
        body ??= new WithdrawQuoteRequest(Reason: null);

        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            var firstFailure = validation.Errors[0];
            var detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{firstFailure.ErrorCode}",
                title = "Withdraw quote validation failed.",
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
                409 => "Quote cannot be withdrawn in its current state.",
                _ => "Withdraw rejected.",
            });
    }
}
