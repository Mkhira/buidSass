using BackendApi.Modules.Storage;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Admin.OpenHistoricalDocument;

/// <summary>
/// Spec 020 contracts §3.7 / tasks T075. Returns a short-lived signed URL
/// for the document body. PII-access audit invariant per FR-015a-e:
/// <list type="bullet">
///   <item>Non-terminal / approved parent: ONE <c>verification.pii_access</c>
///         event with <c>kind=DocumentBodyRead</c>, <c>surface=admin_review</c>.</item>
///   <item>Terminal parent (rejected / expired / revoked / superseded / void):
///         TWO events — the body-read event PLUS a separate
///         <c>verification.pii_access.historical_open</c> event flagging the
///         action for ops review.</item>
///   <item>Document already purged (<c>purged_at IS NOT NULL</c>): returns
///         <c>410 verification.document_purged</c>; no audit event written.</item>
/// </list>
/// </summary>
public sealed class OpenHistoricalDocumentHandler(
    VerificationDbContext db,
    IStorageService storage,
    PiiAccessRecorder piiRecorder)
{
    /// <summary>Default signed-URL TTL — 5 minutes. Stays well below the JWT life.</summary>
    private static readonly TimeSpan SignedUrlTtl = TimeSpan.FromMinutes(5);

    public async Task<OpenResult> HandleAsync(
        Guid verificationId,
        Guid documentId,
        IReadOnlySet<string> reviewerMarkets,
        CancellationToken ct)
    {
        var doc = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId && d.VerificationId == verificationId)
            .Select(d => new
            {
                d.Id,
                d.VerificationId,
                d.StorageKey,
                d.PurgedAt,
            })
            .SingleOrDefaultAsync(ct);

        if (doc is null)
        {
            return OpenResult.NotFound;
        }

        // Market-scope guard via parent verification.
        var verificationMarket = await db.Verifications
            .AsNoTracking()
            .Where(v => v.Id == verificationId)
            .Select(v => new { v.MarketCode, v.State })
            .SingleOrDefaultAsync(ct);

        if (verificationMarket is null
            || !reviewerMarkets.Contains(verificationMarket.MarketCode))
        {
            return OpenResult.NotFound;
        }

        if (doc.PurgedAt is not null || string.IsNullOrEmpty(doc.StorageKey))
        {
            return OpenResult.Purged(doc.PurgedAt);
        }

        // Mint signed URL.
        var signedUrl = await storage.GetSignedUrlAsync(doc.StorageKey!, SignedUrlTtl, ct);
        var expiresAt = DateTimeOffset.UtcNow.Add(SignedUrlTtl);

        // PII audit.
        var isTerminal = verificationMarket.State.IsTerminal();
        if (isTerminal)
        {
            await piiRecorder.RecordHistoricalDocumentOpenAsync(verificationId, documentId, ct);
        }
        else
        {
            await piiRecorder.RecordAsync(
                PiiAccessKind.DocumentBodyRead,
                verificationId,
                documentId,
                ct);
        }

        return OpenResult.Found(new OpenHistoricalDocumentResponse(
            VerificationId: verificationId,
            DocumentId: documentId,
            SignedUrl: signedUrl.ToString(),
            SignedUrlExpiresAt: expiresAt,
            IsHistoricalOpen: isTerminal));
    }
}

public sealed record OpenResult(
    bool Exists,
    bool IsPurged,
    DateTimeOffset? PurgedAt,
    OpenHistoricalDocumentResponse? Response)
{
    public static OpenResult Found(OpenHistoricalDocumentResponse r) => new(true, false, null, r);
    public static OpenResult NotFound => new(false, false, null, null);
    public static OpenResult Purged(DateTimeOffset? purgedAt) => new(true, true, purgedAt, null);
}
