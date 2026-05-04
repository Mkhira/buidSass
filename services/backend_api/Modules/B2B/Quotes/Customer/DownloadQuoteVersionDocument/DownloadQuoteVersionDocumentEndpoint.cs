using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.DownloadQuoteVersionDocument;

/// <summary>
/// Spec 021 contract §2.8 — endpoint for
/// <c>GET /api/customer/quotes/{quoteId:guid}/versions/{versionId:guid}/documents/{locale}</c>.
///
/// Read-only — no <c>Idempotency-Key</c> header. Visibility-leak prevention:
/// 404 fires for both not-found and not-authorized branches with reason code
/// <c>quote.not_found</c> (per §2.4 + §2.8). A 404 with reason
/// <c>quote.document_not_found</c> is reserved for the case where the quote
/// IS visible but no document exists for the requested locale.
///
/// Locale tokens accepted on the wire are case-insensitive (<c>EN</c> | <c>en</c>
/// | <c>AR</c> | <c>ar</c>); they are canonicalized to lower-case before the
/// handler's exact-match lookup. Any locale outside the persisted
/// <c>{en, ar}</c> domain resolves cleanly to <c>quote.document_not_found</c>
/// without a separate validation branch.
/// </summary>
public static class DownloadQuoteVersionDocumentEndpoint
{
    public static IEndpointRouteBuilder MapDownloadQuoteVersionDocumentEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet(
                "/{quoteId:guid}/versions/{versionId:guid}/documents/{locale}",
                HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid quoteId,
        [FromRoute] Guid versionId,
        [FromRoute] string locale,
        HttpContext context,
        [FromServices] DownloadQuoteVersionDocumentHandler handler,
        CancellationToken ct)
    {
        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            // RequireAuthorization filters most unauthenticated calls; this is
            // the defensive branch when the auth scheme accepted the request
            // but the subject claim was absent or unparseable.
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        // Canonicalize the route parameter once at the boundary. Storage in
        // quote_version_documents is lower-case-only (data-model §2.7).
        var canonicalLocale = (locale ?? string.Empty).Trim().ToLowerInvariant();

        var result = await handler.HandleAsync(
            customerId.Value,
            new DownloadQuoteVersionDocumentRequest(quoteId, versionId, canonicalLocale),
            ct);

        return result.Kind switch
        {
            ResolutionKind.Found => Results.Ok(new
            {
                url = result.Response!.Url,
                expires_at = result.Response.ExpiresAt,
                content_type = result.Response.ContentType,
            }),
            ResolutionKind.NotFoundQuote => B2BResponseFactory.Problem(
                context, 404,
                QuoteReasonCode.QuoteNotFound,
                "Quote not found."),
            ResolutionKind.NotFoundDocument => B2BResponseFactory.Problem(
                context, 404,
                QuoteReasonCode.QuoteDocumentNotFound,
                "Document not found for this quote version."),
            // ResolutionKind is closed (Found / NotFoundQuote / NotFoundDocument);
            // any other value is a programmer error — fail loudly rather than mint
            // a misleading 500 with a client-shaped reasonCode.
            _ => throw new InvalidOperationException(
                $"Unhandled DownloadQuoteVersionDocument ResolutionKind: {result.Kind}"),
        };
    }
}
