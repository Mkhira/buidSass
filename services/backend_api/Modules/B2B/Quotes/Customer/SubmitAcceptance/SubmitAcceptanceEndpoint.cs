using BackendApi.Modules.B2B.Primitives;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.SubmitAcceptance;

/// <summary>
/// Spec 021 contract §2.7 — endpoint for
/// <c>POST /api/customer/quotes/{id}/submit-acceptance</c>. State-transitioning POST,
/// so it requires:
/// <list type="bullet">
///   <item><c>CustomerJwt</c> auth scheme (401 otherwise).</item>
///   <item><c>Idempotency-Key</c> header — parsed as a Guid; spec.md §Edge Case
///         "PO number reuse" requires the SAME Idempotency-Key be sent on the
///         soft-warning resubmit so the handler / conversion handler recognize it
///         as a continuation.</item>
///   <item>FluentValidation pre-flight on the body (po_number length).</item>
/// </list>
///
/// Reason-code wire surface:
/// <list type="bullet">
///   <item>200 → committed-success body OR soft-warning body (shape disambiguates).</item>
///   <item>400 <c>quote.required_field_missing</c> → missing, non-Guid, or empty
///         Idempotency-Key OR validator failure on body.</item>
///   <item>400 <c>quote.po_required</c> → <c>company.po_required=true</c> and
///         neither body nor quote-of-record carries a PO (spec FR-009).</item>
///   <item>404 <c>quote.not_found</c> → quote unknown OR caller has no authority
///         (visibility-leak prevention).</item>
///   <item>409 <c>quote.invalid_state_for_action</c> | <c>quote.expired</c> |
///         <c>quote.no_approver_available</c> | <c>quote.po_already_used</c>.</item>
///   <item>422 <c>quote.eligibility_required</c> | <c>quote.market_mismatch</c> |
///         <c>quote.company_suspended</c>.</item>
/// </list>
/// </summary>
public static class SubmitAcceptanceEndpoint
{
    public static IEndpointRouteBuilder MapSubmitAcceptanceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/submit-acceptance", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid id,
        [FromBody] SubmitAcceptanceRequest? body,
        HttpContext context,
        [FromServices] SubmitAcceptanceHandler handler,
        [FromServices] IValidator<SubmitAcceptanceRequest> validator,
        CancellationToken ct)
    {
        // ---------- Idempotency-Key header gate (§1) ----------
        // Reject the empty Guid explicitly — `Guid.TryParse` happily accepts
        // "00000000-0000-0000-0000-000000000000", but using it as a stable
        // dedup key would collide every replay attempt across every caller and
        // poison the conversion handler's idempotency cache.
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idemHeader)
            || idemHeader.Count == 0
            || string.IsNullOrWhiteSpace(idemHeader[0])
            || !Guid.TryParse(idemHeader[0], out var idempotencyKey)
            || idempotencyKey == Guid.Empty)
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Idempotency-Key header is required (must be a non-empty Guid).");
        }

        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        var marketCode = B2BResponseFactory.ResolveMarketCode(context);
        if (marketCode is null)
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteMarketMismatch,
                "Unknown market_code claim on the caller identity.");
        }

        // Empty body is legitimate (§2.7 makes every field optional); coerce null
        // to the default-shaped DTO so the validator + handler don't have to defend.
        body ??= new SubmitAcceptanceRequest(
            PoNumber: null,
            PoWarningAcknowledged: false,
            TaxPreviewDriftAcknowledged: false);

        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            var firstFailure = validation.Errors[0];
            var detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{firstFailure.ErrorCode}",
                title = "Submit-acceptance validation failed.",
                status = 400,
                detail,
                instance = context.Request.Path.ToString(),
                reasonCode = firstFailure.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }

        var localeHint = B2BResponseFactory.ResolveLocaleHint(context);
        var result = await handler.HandleAsync(
            id, customerId.Value, marketCode, idempotencyKey, body, ct, localeHint);

        if (result.IsSuccess)
        {
            // Two flavours of 200: committed-success vs. soft-warning. Shape
            // disambiguates so the client doesn't have to peek at status alone.
            if (result.PoWarningResponse is not null)
            {
                return Results.Ok(result.PoWarningResponse);
            }
            return Results.Ok(result.Response);
        }

        return B2BResponseFactory.Problem(context,
            result.StatusCode,
            result.ReasonCode!.Value,
            result.StatusCode switch
            {
                404 => "Quote not found.",
                409 => "Quote acceptance rejected.",
                422 => "Quote acceptance not permitted.",
                _ => "Submit-acceptance rejected.",
            });
    }
}
