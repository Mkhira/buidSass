using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 — endpoint for
/// <c>POST /api/customer/quotes/from-product</c>. Wire-shape responsibilities:
/// <list type="bullet">
///   <item>Authentication: <c>CustomerJwt</c> scheme required (401 otherwise).</item>
///   <item><c>Idempotency-Key</c> header required (400 otherwise).</item>
///   <item>Body deserialization + FluentValidation pre-flight (product_id and
///         quantity are mandatory; branch_id requires company_id).</item>
///   <item>Account-locked surfacing: the spec 020 <c>account.locked</c> permission
///         claim is forwarded to the handler as a boolean so the handler enforces
///         the FR-038 carry-over without needing to read claims itself.</item>
///   <item>Translates handler results into HTTP responses + problem-details.</item>
/// </list>
/// </summary>
public static class RequestQuoteFromProductEndpoint
{
    public static IEndpointRouteBuilder MapRequestQuoteFromProductEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/from-product", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] RequestQuoteFromProductRequest? body,
        HttpContext context,
        [FromServices] RequestQuoteFromProductHandler handler,
        [FromServices] IValidator<RequestQuoteFromProductRequest> validator,
        CancellationToken ct)
    {
        // ---------- Idempotency-Key required ----------
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idemKey)
            || idemKey.Count == 0
            || string.IsNullOrWhiteSpace(idemKey[0]))
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Idempotency-Key header is required.");
        }

        // ---------- Customer claim ----------
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
                "Unknown market claim.");
        }

        // ---------- Body shape ----------
        // Empty body is invalid for §2.2 — product_id and quantity are mandatory —
        // but the validator surfaces the precise missing-field message so we still
        // coerce null to an empty DTO and let the validator report.
        body ??= new RequestQuoteFromProductRequest(null, null, null, null, null, null);

        // ---------- Validate ----------
        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            var firstFailure = validation.Errors[0];
            var detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{firstFailure.ErrorCode}",
                title = "Quote-from-product request validation failed.",
                status = 400,
                detail,
                instance = context.Request.Path.ToString(),
                reasonCode = firstFailure.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }

        // FR-038 carry-over — spec 020 marks locked accounts via the `account.locked`
        // permission claim until the in-process subscriber lands. The handler treats
        // it as the canonical signal for `quote.account_inactive`.
        var accountLocked = context.User.HasClaim("permission", "account.locked");

        // ---------- Handler ----------
        var result = await handler.HandleAsync(customerId.Value, marketCode, accountLocked, body, ct);

        if (result.IsSuccess)
        {
            return Results.Created($"/api/customer/quotes/{result.Response!.Id}", result.Response);
        }

        return B2BResponseFactory.Problem(
            context,
            result.StatusCode,
            result.ReasonCode!.Value,
            result.Detail ?? "Quote-from-product request rejected.",
            result.Extensions);
    }
}
