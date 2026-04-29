namespace BackendApi.Modules.Verification.Customer.AttachDocument;

/// <summary>
/// Customer attaches a document to an existing verification per spec 020
/// contracts §2.5.
///
/// <para>The actual virus scan happens UPSTREAM of this endpoint, in the
/// storage abstraction (spec 015) — typically via an async scanner that runs
/// when the storage object lands. The customer's client receives the resulting
/// <see cref="ScanStatus"/> from the upload step and forwards it here.</para>
///
/// <para><b>Persistence contract:</b> the endpoint persists rows for BOTH
/// <c>clean</c> and <c>pending</c> scan results. Pending rows surface their
/// status via <see cref="Entities.VerificationDocument.ScanStatus"/> until the
/// async scanner flips them to <c>clean</c> (or <c>infected</c>, in which case
/// downstream submission is blocked). <c>infected</c> and <c>error</c> are
/// rejected at attach time and no row is written.</para>
///
/// <para>This contract is deliberately honest about where scanning lives —
/// previously an in-handler <c>IVirusScanService</c> call ran against an
/// empty stream, which produced a misleading <c>clean</c> signal regardless
/// of the actual file. Removed to match reality.</para>
/// </summary>
/// <param name="StorageKey">Spec 015 storage object id from the pre-upload step.</param>
/// <param name="ContentType">MIME type — must match the schema's allowed_document_types.</param>
/// <param name="SizeBytes">File size; max 10 MB per document, 25 MB cumulative per verification.</param>
/// <param name="ScanStatus">Upstream scan result from the storage abstraction. One of <c>clean</c>, <c>pending</c>, <c>infected</c>, <c>error</c>. Defaults to <c>pending</c> when omitted.</param>
public sealed record AttachDocumentRequest(
    string StorageKey,
    string ContentType,
    long SizeBytes,
    string? ScanStatus = null);

public sealed record AttachDocumentResponse(
    Guid DocumentId,
    Guid VerificationId,
    string ContentType,
    long SizeBytes,
    string ScanStatus,
    DateTimeOffset UploadedAt);
