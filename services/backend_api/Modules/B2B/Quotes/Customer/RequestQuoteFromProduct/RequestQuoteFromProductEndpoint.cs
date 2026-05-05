using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 — endpoint for <c>POST /api/customer/quotes/from-product</c>.
/// Same wire-shape concerns as the from-cart endpoint: <c>CustomerJwt</c> required,
/// <c>Idempotency-Key</c> header required, body validated, handler invoked, problem-details
/// emitted on rejection.
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

        var marketCode = B2BResponseFactory.ResolveMarketCode(context);
        if (marketCode is null)
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteMarketMismatch,
                "Unknown market claim.");
        }

        if (body is null)
        {
            // A null body means every field is missing — the validator will surface
            // the product_id / quantity required-field violation as 400.
            body = new RequestQuoteFromProductRequest(null, null, null, null, null, null);
        }

        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
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
