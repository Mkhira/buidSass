using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

/// <summary>
/// Spec 021 contract §2.1 — endpoint for <c>POST /api/customer/quotes/from-cart</c>.
/// Wire-shape concerns only:
/// <list type="bullet">
///   <item>Authentication: <c>CustomerJwt</c> scheme required (401 otherwise).</item>
///   <item><c>Idempotency-Key</c> header required (400 otherwise) — replay safety
///         for the rare case where the client retries the POST after a network blip.
///         The key is recorded into the QuoteStateTransition metadata in Cycle B/C
///         once the SubmitAcceptance handler defines the idempotency-replay table.
///         For this Cycle B slice we enforce the header presence only; full replay
///         semantics land with the SubmitAcceptance slice (T070 / Cycle C).</item>
///   <item>Body deserialization + FluentValidation pre-flight.</item>
///   <item>Translates handler results into HTTP responses + problem-details.</item>
/// </list>
/// </summary>
public static class RequestQuoteFromCartEndpoint
{
    public static IEndpointRouteBuilder MapRequestQuoteFromCartEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/from-cart", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] RequestQuoteFromCartRequest? body,
        HttpContext context,
        [FromServices] RequestQuoteFromCartHandler handler,
        [FromServices] IValidator<RequestQuoteFromCartRequest> validator,
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
            // Should not normally fire — RequireAuthorization filters unauthenticated
            // requests. Defensive in case the auth scheme accepted the request but
            // omitted the subject claim.
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        var marketCode = B2BResponseFactory.ResolveMarketCode(context);

        // ---------- Body shape ----------
        if (body is null)
        {
            // Treat null body as an empty request — every field is optional. This is
            // important: contract tests POST `new { }` with no body fields, expecting
            // the empty-cart branch to fire (because nothing was supplied).
            body = new RequestQuoteFromCartRequest(null, null, null, null);
        }

        // ---------- Validate ----------
        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            // Surface the first failure's error code (which we set to a stable
            // QuoteReasonCode token in the validator). Aggregate detail strings
            // across all failures so the client gets the full picture.
            var firstFailure = validation.Errors[0];
            var detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{firstFailure.ErrorCode}",
                title = "Quote request validation failed.",
                status = 400,
                detail,
                instance = context.Request.Path.ToString(),
                reasonCode = firstFailure.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }

        // ---------- Handler ----------
        var result = await handler.HandleAsync(customerId.Value, marketCode, body, ct);

        if (result.IsSuccess)
        {
            return Results.Created($"/api/customer/quotes/{result.Response!.Id}", result.Response);
        }

        return B2BResponseFactory.Problem(
            context,
            result.StatusCode,
            result.ReasonCode!.Value,
            result.Detail ?? "Quote request rejected.",
            result.Extensions);
    }
}
