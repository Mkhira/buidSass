namespace BackendApi.Modules.Verification.Customer.AttachDocument;

/// <summary>
/// Customer attaches a document to an existing verification per spec 020
/// contracts §2.5. The customer pre-uploads the file via spec 015's storage
/// abstraction (presigned URL or equivalent) and then references the resulting
/// <see cref="StorageKey"/> here. This endpoint records the metadata + scan
/// status onto a new <c>verification_documents</c> row.
/// </summary>
/// <param name="StorageKey">Spec 015 storage object id from the pre-upload step.</param>
/// <param name="ContentType">MIME type — must match the schema's allowed_document_types.</param>
/// <param name="SizeBytes">File size; max 10 MB per document, 25 MB cumulative per verification.</param>
public sealed record AttachDocumentRequest(
    string StorageKey,
    string ContentType,
    long SizeBytes);

public sealed record AttachDocumentResponse(
    Guid DocumentId,
    Guid VerificationId,
    string ContentType,
    long SizeBytes,
    string ScanStatus,
    DateTimeOffset UploadedAt);
