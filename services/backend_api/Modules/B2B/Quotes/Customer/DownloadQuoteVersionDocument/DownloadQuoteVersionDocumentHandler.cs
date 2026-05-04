using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Quotes.Customer.Visibility;
using BackendApi.Modules.Storage;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Customer.DownloadQuoteVersionDocument;

/// <summary>
/// Spec 021 contract §2.8 — handler for
/// <c>GET /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale}</c>.
///
/// Returns a short-lived signed URL pointing at the locale-specific PDF blob.
/// Reuses <see cref="CustomerQuoteVisibility"/> for the read-side gate so the
/// 404 envelope here is the SAME envelope <see cref="GetMyQuote.GetMyQuoteHandler"/>
/// emits — non-members of a company quote and unknown-quote-id callers BOTH see
/// <see cref="DownloadResult.NotFound"/> with reason <c>quote.not_found</c>
/// (visibility-leak prevention per §2.4).
///
/// Resolution order (per contract §2.8 + visibility-leak rule):
/// <list type="number">
///   <item>Visibility-gated quote read — fail to <c>NotFoundQuote</c> if the
///         caller can't see the parent quote (covers both unknown-id and
///         not-authorized branches with the same envelope).</item>
///   <item>QuoteVersion existence under the quote (FK invariant) — fail to
///         <c>NotFoundDocument</c> if the version doesn't belong to this quote.</item>
///   <item>QuoteVersionDocument lookup by <c>(quote_version_id, locale)</c> —
///         fail to <c>NotFoundDocument</c> if no row matches the locale; also
///         fail when the row exists but <c>StorageKey</c> is empty (defensive
///         guard for partially-written rows during a failed publish).</item>
///   <item>Mint signed URL via <see cref="IStorageService.GetSignedUrlAsync"/>
///         with a 5-minute TTL (mirrors spec 020's
///         <c>OpenHistoricalDocumentHandler</c> default — well below the JWT
///         life and short enough that link-sharing risk is minimal).</item>
/// </list>
///
/// No PII-access audit fires for a quote-document read. Spec 020's terminal-state
/// branched audit doesn't apply here: a quote PDF is the customer's own purchase
/// artefact, not third-party PII. Spec 021's audit list (FR-043) covers state
/// transitions, not document opens. If product later wants document-read
/// telemetry it can layer in via a separate <c>quote.document_opened</c> event
/// in a follow-up cycle.
/// </summary>
public sealed class DownloadQuoteVersionDocumentHandler
{
    /// <summary>Default signed-URL TTL — 5 minutes. Mirrors spec 020 admin-doc downloads.</summary>
    private static readonly TimeSpan SignedUrlTtl = TimeSpan.FromMinutes(5);

    private readonly B2BDbContext _db;
    private readonly IStorageService _storage;
    private readonly TimeProvider _time;

    public DownloadQuoteVersionDocumentHandler(
        B2BDbContext db,
        IStorageService storage,
        TimeProvider time)
    {
        _db = db;
        _storage = storage;
        _time = time;
    }

    public async Task<DownloadResult> HandleAsync(
        Guid customerId,
        DownloadQuoteVersionDocumentRequest request,
        CancellationToken ct)
    {
        // ---------- Visibility ----------
        var visibleCompanyIds = await CustomerQuoteVisibility
            .GetVisibleCompanyIdsAsync(_db, customerId, ct);

        var quote = await CustomerQuoteVisibility.GetVisibleQuoteAsync(
            _db, request.QuoteId, customerId, visibleCompanyIds, ct);

        if (quote is null)
        {
            // Both not-found and not-authorized land here — single 404 envelope.
            return DownloadResult.NotFoundQuote();
        }

        // ---------- Version belongs to quote ----------
        // Defensive: an attacker who knows a valid version id from a different
        // quote MUST NOT be able to attach it to a quote they CAN see. We pin
        // the lookup on (version_id, quote_id) so a mismatched pair surfaces as
        // document-not-found instead of leaking the version.
        var versionExists = await _db.QuoteVersions
            .AsNoTracking()
            .AnyAsync(v => v.Id == request.VersionId && v.QuoteId == request.QuoteId, ct);

        if (!versionExists)
        {
            return DownloadResult.NotFoundDocument();
        }

        // ---------- Document lookup ----------
        // Locale is already canonicalized to lower-case in the endpoint; the
        // (quote_version_id, locale) UNIQUE index on quote_version_documents
        // makes this a single-row read.
        var doc = await _db.QuoteVersionDocuments
            .AsNoTracking()
            .Where(d => d.QuoteVersionId == request.VersionId && d.Locale == request.Locale)
            .Select(d => new { d.StorageKey, d.ContentType })
            .SingleOrDefaultAsync(ct);

        if (doc is null || string.IsNullOrEmpty(doc.StorageKey))
        {
            return DownloadResult.NotFoundDocument();
        }

        // ---------- Mint signed URL ----------
        var signedUrl = await _storage.GetSignedUrlAsync(doc.StorageKey, SignedUrlTtl, ct);
        var expiresAt = _time.GetUtcNow().Add(SignedUrlTtl);

        return DownloadResult.Found(new DownloadQuoteVersionDocumentResponse(
            Url: signedUrl.ToString(),
            ExpiresAt: expiresAt,
            ContentType: doc.ContentType));
    }
}

/// <summary>
/// Internal handler envelope. Endpoint maps:
/// <list type="bullet">
///   <item><see cref="ResolutionKind.Found"/> → <c>200 OK</c> with
///         <c>{ url, expires_at, content_type }</c>.</item>
///   <item><see cref="ResolutionKind.NotFoundQuote"/> →
///         <c>404 quote.not_found</c> (visibility-leak envelope; same as §2.4).</item>
///   <item><see cref="ResolutionKind.NotFoundDocument"/> →
///         <c>404 quote.document_not_found</c>.</item>
/// </list>
/// </summary>
public sealed record DownloadResult(
    ResolutionKind Kind,
    DownloadQuoteVersionDocumentResponse? Response)
{
    public static DownloadResult Found(DownloadQuoteVersionDocumentResponse r) =>
        new(ResolutionKind.Found, r);
    public static DownloadResult NotFoundQuote() =>
        new(ResolutionKind.NotFoundQuote, null);
    public static DownloadResult NotFoundDocument() =>
        new(ResolutionKind.NotFoundDocument, null);
}

public enum ResolutionKind
{
    Found,
    NotFoundQuote,
    NotFoundDocument,
}

public sealed record DownloadQuoteVersionDocumentResponse(
    string Url,
    DateTimeOffset ExpiresAt,
    string ContentType);
