namespace BackendApi.Modules.Verification.Admin.OpenHistoricalDocument;

/// <summary>
/// Response shape per spec 020 contracts §3.7. <see cref="SignedUrl"/> is a
/// short-lived URL (TTL ≤ 5 minutes) returned by spec 015's storage abstraction.
/// </summary>
public sealed record OpenHistoricalDocumentResponse(
    Guid VerificationId,
    Guid DocumentId,
    string SignedUrl,
    DateTimeOffset SignedUrlExpiresAt,
    bool IsHistoricalOpen);
