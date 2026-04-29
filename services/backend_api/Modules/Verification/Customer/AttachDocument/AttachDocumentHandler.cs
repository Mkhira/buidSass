using System.Text.Json;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Verification.Customer.AttachDocument;

/// <summary>
/// Spec 020 contracts §2.5 / tasks T054. Records a pre-uploaded storage
/// object as a verification document. Validates:
/// <list type="bullet">
///   <item>customer owns the verification (404 NotFound if not);</item>
///   <item>verification is in a non-terminal state OR <c>info_requested</c>
///         (rejecting attaches on rejected/expired/revoked rows);</item>
///   <item>MIME against the schema's <c>allowed_document_types</c>;</item>
///   <item>per-doc size ≤ 10 MB and cumulative ≤ 25 MB per verification;</item>
///   <item>per-verification document count ≤ 5;</item>
///   <item>scan status — supplied by the caller (read from spec 015 storage
///         abstraction's async scanner output). <c>infected</c> rejects;
///         <c>clean</c> and <c>pending</c> persist; <c>error</c> rejects as
///         transient. Scanning itself is upstream of this handler — see
///         <see cref="AttachDocumentRequest"/> docstring.</item>
/// </list>
/// </summary>
public sealed class AttachDocumentHandler(
    VerificationDbContext db,
    TimeProvider clock,
    ILogger<AttachDocumentHandler> logger)
{
    private const long MaxBytesPerDocument = 10 * 1024 * 1024;       // 10 MB
    private const long MaxAggregateBytesPerVerification = 25 * 1024 * 1024;  // 25 MB
    private const int MaxDocumentsPerVerification = 5;

    public async Task<AttachResult> HandleAsync(
        Guid customerId,
        Guid verificationId,
        AttachDocumentRequest request,
        CancellationToken ct)
    {
        // 0. Required-field validation — both StorageKey and ContentType are
        //    load-bearing for downstream MIME/size/scan checks and document
        //    persistence. Reject blanks early with a precise reason code rather
        //    than letting them flow into Contains() / EF.
        if (string.IsNullOrWhiteSpace(request.StorageKey))
        {
            return AttachResult.Fail(
                VerificationReasonCode.RequiredFieldMissing,
                "storage_key is required.");
        }
        if (string.IsNullOrWhiteSpace(request.ContentType))
        {
            return AttachResult.Fail(
                VerificationReasonCode.RequiredFieldMissing,
                "content_type is required.");
        }

        // 1. Owner gate — 404 (not 403) avoids leaking row existence to a foreign customer.
        var verification = await db.Verifications
            .Where(v => v.Id == verificationId && v.CustomerId == customerId)
            .SingleOrDefaultAsync(ct);
        if (verification is null)
        {
            return AttachResult.NotFound;
        }

        // 2. State gate — terminal rows reject; only non-terminal + info_requested accept.
        if (verification.State.IsTerminal())
        {
            return AttachResult.Fail(
                VerificationReasonCode.InvalidStateForAction,
                $"Cannot attach a document to a verification in state '{verification.State.ToWireValue()}'.");
        }

        // 3. MIME against snapshot schema.
        var schema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == verification.MarketCode && s.Version == verification.SchemaVersion)
            .SingleOrDefaultAsync(ct);
        if (schema is null)
        {
            return AttachResult.Fail(
                VerificationReasonCode.MarketUnsupported,
                "Schema referenced by the verification could not be loaded.");
        }
        // Fail-closed on missing/invalid schema config (Principle 5: market behavior
        // MUST be driven by configuration). A blank or malformed
        // allowed_document_types is a market-config error, not a license to fall
        // back to a hardcoded MIME allowlist.
        if (!TryParseAllowedMimes(schema.AllowedDocumentTypesJson, out var allowedMimes))
        {
            return AttachResult.Fail(
                VerificationReasonCode.MarketUnsupported,
                $"Schema for market '{verification.MarketCode}' has no valid allowed_document_types configuration.");
        }
        if (!allowedMimes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentMimeForbidden,
                $"Content type '{request.ContentType}' is not allowed for market '{verification.MarketCode}'.");
        }

        // 4. Size + count gates.
        if (request.SizeBytes <= 0 || request.SizeBytes > MaxBytesPerDocument)
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentSizeExceeded,
                $"Document size {request.SizeBytes} exceeds the maximum of {MaxBytesPerDocument} bytes.");
        }

        var existingActive = await db.Documents
            .Where(d => d.VerificationId == verificationId && d.PurgedAt == null)
            .Select(d => new { d.SizeBytes })
            .ToListAsync(ct);
        if (existingActive.Count >= MaxDocumentsPerVerification)
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentCountExceeded,
                $"Maximum {MaxDocumentsPerVerification} documents per verification.");
        }
        var newAggregate = existingActive.Sum(d => d.SizeBytes) + request.SizeBytes;
        if (newAggregate > MaxAggregateBytesPerVerification)
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentAggregateSizeExceeded,
                $"Aggregate document size {newAggregate} exceeds {MaxAggregateBytesPerVerification} bytes.");
        }

        // 5. Scan status from upstream (spec 015 storage abstraction's async
        //    scanner). Defaults to "pending" when caller omits — keeps the
        //    contract honest about scanning being a separate concern.
        var scanStatus = (request.ScanStatus ?? "pending").Trim().ToLowerInvariant();
        if (scanStatus is not ("clean" or "pending" or "infected" or "error"))
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentsInvalid,
                $"scan_status '{request.ScanStatus}' is not one of clean | pending | infected | error.");
        }
        if (scanStatus == "infected")
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentScanInfected,
                "Document failed virus scan upstream.");
        }
        if (scanStatus == "error")
        {
            return AttachResult.Fail(
                VerificationReasonCode.DocumentsInvalid,
                "Upstream virus scanner returned an error; please retry.");
        }
        // "clean" and "pending" both persist — pending rows surface their
        // status in the row's scan_status column; submission downstream blocks
        // until the async scanner flips them to "clean".

        // 6. Persist.
        var nowUtc = clock.GetUtcNow();
        var doc = new VerificationDocument
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            MarketCode = verification.MarketCode,
            StorageKey = request.StorageKey,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            ScanStatus = scanStatus,    // honest pass-through: clean | pending
            UploadedAt = nowUtc,
            PurgeAfter = null,
            PurgedAt = null,
        };
        db.Documents.Add(doc);

        // Bump the verification row so the audit trail captures the attachment instant.
        verification.UpdatedAt = nowUtc;

        await db.SaveChangesAsync(ct);

        if (scanStatus == "pending")
        {
            logger.LogInformation(
                "Verification {VerificationId} document {DocumentId} attached with scan_status=pending; submission downstream will block until upstream scanner flips to clean.",
                verification.Id, doc.Id);
        }

        return AttachResult.Ok(new AttachDocumentResponse(
            DocumentId: doc.Id,
            VerificationId: verification.Id,
            ContentType: doc.ContentType,
            SizeBytes: doc.SizeBytes,
            ScanStatus: doc.ScanStatus,
            UploadedAt: doc.UploadedAt));
    }

    private static bool TryParseAllowedMimes(string allowedJson, out IReadOnlyCollection<string> allowedMimes)
    {
        if (string.IsNullOrWhiteSpace(allowedJson))
        {
            allowedMimes = Array.Empty<string>();
            return false;
        }
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(allowedJson);
            if (arr is null || arr.Length == 0)
            {
                allowedMimes = Array.Empty<string>();
                return false;
            }
            allowedMimes = arr;
            return true;
        }
        catch (JsonException)
        {
            allowedMimes = Array.Empty<string>();
            return false;
        }
    }
}

public sealed record AttachResult(
    bool IsSuccess,
    bool IsNotFound,
    AttachDocumentResponse? Response,
    VerificationReasonCode? ReasonCode,
    string? Detail)
{
    public static AttachResult Ok(AttachDocumentResponse r) => new(true, false, r, null, null);
    public static AttachResult NotFound => new(false, true, null, null, null);
    public static AttachResult Fail(VerificationReasonCode code, string detail) =>
        new(false, false, null, code, detail);
}
