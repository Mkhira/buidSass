using BackendApi.Modules.B2B.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.GetMyQuote;

/// <summary>
/// Spec 021 contract §2.4 — endpoint for <c>GET /api/customer/quotes/{id}</c>.
///
/// Read-only — no <c>Idempotency-Key</c> header. Visibility-leak prevention is
/// the load-bearing wire concern: 404 fires for both not-found and not-authorized
/// branches with the same <c>quote.not_found</c> reason code (per §2.4 + §9).
/// </summary>
public static class GetMyQuoteEndpoint
{
    public static IEndpointRouteBuilder MapGetMyQuoteEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid id,
        HttpContext context,
        [FromServices] GetMyQuoteHandler handler,
        CancellationToken ct)
    {
        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        var result = await handler.HandleAsync(customerId.Value, new GetMyQuoteRequest(id), ct);

        if (!result.Found)
        {
            // Single 404 path for both unknown-id and not-authorized branches —
            // per §2.4 visibility-leak prevention. NEVER 403 here.
            return B2BResponseFactory.Problem(context, 404,
                QuoteReasonCode.QuoteNotFound,
                "Quote not found.");
        }

        return Results.Ok(result.Response);
    }
}
