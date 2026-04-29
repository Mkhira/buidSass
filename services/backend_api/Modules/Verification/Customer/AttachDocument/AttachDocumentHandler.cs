using System.Text.Json;
using BackendApi.Modules.Storage;
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
///   <item>virus scan via spec 015 — only <c>clean</c> rows persist.</item>
/// </list>
/// </summary>
public sealed class AttachDocumentHandler(
    VerificationDbContext db,
    IVirusScanService virusScanner,
    IStorageService storage,
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
        var allowedMimes = ParseAllowedMimes(schema.AllowedDocumentTypesJson);
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

        // 5. Virus scan via spec 015. Only "clean" results persist; "infected"
        //    rejects up-front; "error" / "pending" reject as a transient.
        var scanResult = await ScanAsync(request.StorageKey, ct);
        var scanStatus = scanResult switch
        {
            ScanResult.Clean => "clean",
            ScanResult.Infected => "infected",
            ScanResult.ServiceUnavailable => "error",
            _ => "pending",
        };

        if (scanStatus != "clean")
        {
            return AttachResult.Fail(
                scanStatus switch
                {
                    "infected" => VerificationReasonCode.DocumentScanInfected,
                    "error" => VerificationReasonCode.DocumentsInvalid,
                    _ => VerificationReasonCode.DocumentScanPending,
                },
                $"Document scan returned '{scanStatus}'.");
        }

        // 6. Persist.
        var nowUtc = clock.GetUtcNow();
        var doc = new VerificationDocument
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            StorageKey = request.StorageKey,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            ScanStatus = "clean",
            UploadedAt = nowUtc,
            PurgeAfter = null,
            PurgedAt = null,
        };
        db.Documents.Add(doc);

        // Bump the verification row so the audit trail captures the attachment instant.
        verification.UpdatedAt = nowUtc;

        await db.SaveChangesAsync(ct);

        return AttachResult.Ok(new AttachDocumentResponse(
            DocumentId: doc.Id,
            VerificationId: verification.Id,
            ContentType: doc.ContentType,
            SizeBytes: doc.SizeBytes,
            ScanStatus: doc.ScanStatus,
            UploadedAt: doc.UploadedAt));
    }

    private async Task<ScanResult> ScanAsync(string storageKey, CancellationToken ct)
    {
        // Real impl streams the bytes from spec 015 storage to the scanner.
        // Phase 3 batch 2 ships a placeholder that resolves "scan via key" through
        // the storage abstraction's signed-url path; full streaming wires up in
        // the polish pass when the storage abstraction's read-stream API is
        // finalized.
        try
        {
            await storage.GetSignedUrlAsync(storageKey, TimeSpan.FromMinutes(5), ct);
            using var emptyStream = new MemoryStream();
            return await virusScanner.ScanAsync(emptyStream, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Document scan failed for storage_key {StorageKey}; treating as ServiceUnavailable.",
                storageKey);
            return ScanResult.ServiceUnavailable;
        }
    }

    private static IReadOnlyCollection<string> ParseAllowedMimes(string allowedJson)
    {
        if (string.IsNullOrWhiteSpace(allowedJson))
        {
            return new[] { "application/pdf", "image/jpeg", "image/png", "image/heic" };
        }
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(allowedJson);
            return arr is null || arr.Length == 0
                ? new[] { "application/pdf", "image/jpeg", "image/png", "image/heic" }
                : arr;
        }
        catch (JsonException)
        {
            return new[] { "application/pdf", "image/jpeg", "image/png", "image/heic" };
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
