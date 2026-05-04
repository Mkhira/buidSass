namespace BackendApi.Modules.B2B.Quotes.Customer.DownloadQuoteVersionDocument;

/// <summary>
/// Spec 021 contract §2.8 — request envelope for
/// <c>GET /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale}</c>.
///
/// Route-bound only — no body. <see cref="Locale"/> is canonicalized to lower-case
/// in the endpoint before reaching the handler so the handler's case-sensitive
/// Postgres lookup against <c>quote_version_documents.locale</c> stays simple.
/// Permitted values per spec 021 (data-model §2.7): <c>en</c> | <c>ar</c>. Any
/// other token resolves to <c>404 quote.document_not_found</c> per §2.8.
/// </summary>
public sealed record DownloadQuoteVersionDocumentRequest(
    Guid QuoteId,
    Guid VersionId,
    string Locale);
