using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Documents;
using BackendApi.Modules.B2B.Documents.PdfTemplates;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Pdf;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.B2B.Quotes.Admin.PublishQuoteVersion;

/// <summary>
/// Spec 021 T088 — publish handler. Generates EN + AR PDFs for the latest
/// (drafted) <see cref="QuoteVersion"/>, persists two
/// <see cref="QuoteVersionDocument"/> rows, transitions <c>drafted → revised</c>,
/// updates <see cref="Quote.CurrentVersionId"/>, and recomputes <c>expires_at</c>
/// when <c>validity_extends=true</c> (Clarifications Q5) or on first publish.
///
/// The QuoteVersion itself is materialized by <see cref="AuthorQuoteDraft.AuthorQuoteDraftHandler"/>
/// when state transitions to <c>drafted</c>; publish operates on that immutable row.
/// </summary>
public sealed class PublishQuoteVersionHandler
{
    private readonly B2BDbContext _db;
    private readonly QuoteVersionPdfRenderer _renderer;
    private readonly IStorageService _storage;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly TimeProvider _time;
    private readonly ILogger<PublishQuoteVersionHandler> _logger;

    public PublishQuoteVersionHandler(
        B2BDbContext db,
        QuoteVersionPdfRenderer renderer,
        IStorageService storage,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        TimeProvider time,
        ILogger<PublishQuoteVersionHandler> logger)
    {
        _db = db;
        _renderer = renderer;
        _storage = storage;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _time = time;
        _logger = logger;
    }

    public async Task<PublishQuoteVersionResult> HandleAsync(
        Guid quoteId,
        Guid actorId,
        PublishQuoteVersionRequest body,
        CancellationToken ct)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return PublishQuoteVersionResult.NotFound();

        if (!QuoteStateExtensions.TryParseToken(quote.State, out var current)
            || current != QuoteState.Drafted)
        {
            return PublishQuoteVersionResult.InvalidState();
        }

        // Locate the draft version. Prefer the highest version_number for this
        // quote — that's the one AuthorQuoteDraft just inserted. If the quote was
        // seeded as `drafted` without a version row (test-fixture path), fall back
        // to creating one inline from the originating cart snapshot.
        var draftVersion = await _db.QuoteVersions
            .Where(v => v.QuoteId == quote.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var schema = await _db.QuoteMarketSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.MarketCode == quote.MarketCode && s.Version == quote.SchemaVersion, ct);
        var nowUtc = _time.GetUtcNow();

        if (draftVersion is null)
        {
            draftVersion = await CreateFallbackVersionAsync(quote, actorId, nowUtc, ct);
            if (draftVersion is null)
            {
                return PublishQuoteVersionResult.RequiredFieldMissing();
            }
        }

        // Compute expires_at: extend on validity_extends, set on first publish.
        var validityExtends = body?.ValidityExtends ?? draftVersion.ValidityExtends;
        var newExpiresAt = quote.ExpiresAt;
        if (validityExtends || quote.CurrentVersionId is null || newExpiresAt is null)
        {
            var validityDays = schema?.ValidityDays ?? 14;
            newExpiresAt = nowUtc.AddDays(validityDays);
        }

        // Render PDFs from the draft version's snapshot.
        var pdfData = BuildPdfData(quote, draftVersion, nowUtc, newExpiresAt);

        string enKey, arKey;
        try
        {
            enKey = await _renderer.RenderAndUploadAsync(pdfData, LocaleCode.EN, ct);
            arKey = await _renderer.RenderAndUploadAsync(pdfData, LocaleCode.AR, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Quote PDF generation failed for quote {QuoteId} version {Version}; publish aborted.",
                quote.Id, draftVersion.VersionNumber);
            return PublishQuoteVersionResult.PdfFailed();
        }

        _db.QuoteVersionDocuments.Add(new QuoteVersionDocument
        {
            Id = Guid.NewGuid(),
            QuoteVersionId = draftVersion.Id,
            MarketCode = quote.MarketCode,
            Locale = "en",
            StorageKey = enKey,
            ContentType = "application/pdf",
            GeneratedAt = nowUtc,
        });
        _db.QuoteVersionDocuments.Add(new QuoteVersionDocument
        {
            Id = Guid.NewGuid(),
            QuoteVersionId = draftVersion.Id,
            MarketCode = quote.MarketCode,
            Locale = "ar",
            StorageKey = arKey,
            ContentType = "application/pdf",
            GeneratedAt = nowUtc,
        });

        var priorState = quote.State;
        quote.State = QuoteState.Revised.ToToken();
        quote.CurrentVersionId = draftVersion.Id;
        quote.ExpiresAt = newExpiresAt;
        quote.DraftBodyJson = null;
        quote.ApproverRejectionNote = null;

        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            MarketCode = quote.MarketCode,
            PriorState = priorState,
            NewState = quote.State,
            ActorKind = QuoteActorKind.AdminOperator.ToToken(),
            ActorId = actorId,
            ReasonJson = null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                version_number = draftVersion.VersionNumber,
                validity_extends = validityExtends,
            }),
            OccurredAt = nowUtc,
        };
        _db.QuoteStateTransitions.Add(transition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // CodeRabbit Round 2: SaveChanges failed AFTER the PDFs were uploaded —
            // best-effort cleanup so we don't accumulate orphan files in storage on
            // every retry. The state remains `drafted`; client retries are safe via
            // the Idempotency-Key path. Cleanup failures are logged but don't
            // override the original DbUpdateConcurrencyException return path.
            await TryDeleteOrphanPdfAsync(enKey, ct);
            await TryDeleteOrphanPdfAsync(arKey, ct);
            return PublishQuoteVersionResult.InvalidState();
        }

        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: "admin_operator",
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: quote.Id,
                BeforeState: new { state = priorState },
                AfterState: new
                {
                    state = quote.State,
                    current_version_id = draftVersion.Id,
                    version_number = draftVersion.VersionNumber,
                    expires_at = newExpiresAt,
                },
                Reason: "publish"), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // CodeRabbit Round 2: log audit failures so operational gaps are visible.
            _logger.LogWarning(ex,
                "PublishQuoteVersion: failed to publish quote.state_changed audit "
                + "(quote_id={QuoteId}, version={VersionId}). Audit-pipeline replay required.",
                quote.Id, draftVersion.Id);
        }

        try
        {
            await _domainPublisher.Publish(new QuotePublished(
                QuoteId: quote.Id,
                QuoteVersionId: draftVersion.Id,
                VersionNumber: draftVersion.VersionNumber,
                CustomerId: quote.CustomerId,
                CompanyId: quote.CompanyId,
                MarketCode: quote.MarketCode,
                LocaleHint: "en",
                PdfStorageKeysByLocale: new Dictionary<string, string>
                {
                    ["en"] = enKey,
                    ["ar"] = arKey,
                }), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PublishQuoteVersion: QuotePublished domain-event publish failed "
                + "(quote_id={QuoteId}, version={VersionId}). Notification subscribers "
                + "(spec 025) will not fire unless replayed.",
                quote.Id, draftVersion.Id);
        }

        return PublishQuoteVersionResult.Success(new PublishQuoteVersionResponse(
            Id: quote.Id,
            State: quote.State,
            CurrentVersionId: draftVersion.Id,
            VersionNumber: draftVersion.VersionNumber,
            ExpiresAt: newExpiresAt));
    }

    /// <summary>
    /// Best-effort cleanup of an orphaned PDF blob after a failed
    /// <c>SaveChangesAsync</c>. The storage key is the GUID returned by
    /// <see cref="QuoteVersionPdfRenderer.RenderAndUploadAsync"/> (which mints a
    /// fresh <see cref="StoredFileResult.FileId"/> per upload). Failure to delete
    /// is logged but does not override the upstream error path.
    /// </summary>
    private async Task TryDeleteOrphanPdfAsync(string storageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return;
        try
        {
            await _storage.DeleteAsync(storageKey, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PublishQuoteVersion: failed to delete orphan PDF storage_key={StorageKey} "
                + "after SaveChanges failure. Storage-cleanup worker should reap it.",
                storageKey);
        }
    }

    private async Task<QuoteVersion?> CreateFallbackVersionAsync(
        Quote quote, Guid actorId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // CodeRabbit Round 2: derive currency BEFORE building the line snapshot so
        // the per-line `currency` token matches the totals header — Round 1 fixed
        // the totals but the line-items helper was still hardcoding SAR.
        var currency = string.Equals(quote.MarketCode, "eg", StringComparison.OrdinalIgnoreCase) ? "EGP" : "SAR";
        var lines = ExtractCartSnapshotLines(quote.OriginatingCartSnapshotJson, currency);
        if (lines.Count == 0) return null;
        var totalsJson = $"{{\"subtotal\":0,\"total_discount\":0,\"total_tax_preview\":0,\"grand_total\":0,\"currency\":\"{currency}\"}}";
        var version = new QuoteVersion
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            MarketCode = quote.MarketCode,
            VersionNumber = 1,
            AuthoredBy = actorId,
            PublishedAt = nowUtc,
            LineItemsJson = JsonSerializer.Serialize(lines),
            TermsTextJson = """{"en":"","ar":""}""",
            TermsDays = 0,
            ValidityExtends = false,
            TotalsSummaryJson = totalsJson,
            CustomerRevisionCommentJson = null,
        };
        _db.QuoteVersions.Add(version);
        return version;
    }

    private static List<object> ExtractCartSnapshotLines(string? json, string currency)
    {
        var result = new List<object>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string? sku = null;
                int qty = 1;
                if (el.TryGetProperty("sku", out var s) && s.ValueKind == JsonValueKind.String) sku = s.GetString();
                if (el.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number) qty = q.GetInt32();
                if (!string.IsNullOrWhiteSpace(sku))
                {
                    result.Add(new
                    {
                        sku,
                        qty,
                        baseline_unit_price = 0m,
                        override_unit_price = (decimal?)null,
                        override_reason = (object?)null,
                        line_discount_amount = 0m,
                        line_tax_preview = 0m,
                        currency,
                    });
                }
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static QuoteVersionPdfData BuildPdfData(
        Quote quote, QuoteVersion version, DateTimeOffset publishedAt, DateTimeOffset? expiresAt)
    {
        var (lines, subtotal, totalDiscount, totalTaxPreview, grandTotal, currency) =
            ExtractPdfLines(version.LineItemsJson, quote.MarketCode);
        var (termsEn, termsAr) = ExtractTerms(version.TermsTextJson);
        // CodeRabbit Round 1: company/customer NAME resolution requires reading
        // outside spec 021 (Identity / B2B.Companies cross-module read). For the
        // V1 PDF surface we render the bilingual company name from the existing
        // Company.NameJson when known, and fall back to the entity-id token for
        // the customer (resolved by spec 014's Flutter render layer when fetching
        // the signed-URL PDF). When spec 014 wires identity-name lookup into the
        // PDF render path, this fallback can be replaced.
        return new QuoteVersionPdfData(
            QuoteId: quote.Id,
            VersionNumber: version.VersionNumber,
            MarketCode: quote.MarketCode,
            CompanyName: quote.CompanyId is null
                ? "—"
                : $"company:{quote.CompanyId:N}",
            CustomerName: $"customer:{quote.CustomerId:N}",
            PoNumber: quote.PoNumber,
            PublishedAt: publishedAt,
            ExpiresAt: expiresAt,
            TermsTextEn: termsEn,
            TermsTextAr: termsAr,
            TermsDays: version.TermsDays,
            Lines: lines,
            Subtotal: subtotal,
            TotalDiscount: totalDiscount,
            TotalTaxPreview: totalTaxPreview,
            GrandTotal: grandTotal,
            Currency: currency);
    }

    private static (List<QuoteVersionPdfLine>, decimal, decimal, decimal, decimal, string)
        ExtractPdfLines(string lineItemsJson, string marketCode)
    {
        var lines = new List<QuoteVersionPdfLine>();
        decimal subtotal = 0m, totalDiscount = 0m, totalTaxPreview = 0m;
        // Per-market default currency. Without this, EG quotes whose first
        // line-item lacks a `currency` token (e.g. legacy snapshots) render
        // SAR-denominated PDFs.
        var currency = string.Equals(marketCode, "eg", StringComparison.OrdinalIgnoreCase)
            ? "EGP"
            : "SAR";
        try
        {
            using var doc = JsonDocument.Parse(lineItemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (lines, subtotal, totalDiscount, totalTaxPreview, 0m, currency);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var sku = el.TryGetProperty("sku", out var s) ? (s.GetString() ?? "") : "";
                var qty = el.TryGetProperty("qty", out var q) ? q.GetInt32() : 1;
                var baseline = el.TryGetProperty("baseline_unit_price", out var b) ? b.GetDecimal() : 0m;
                var ovUnitPrice = el.TryGetProperty("override_unit_price", out var o) && o.ValueKind == JsonValueKind.Number
                    ? o.GetDecimal() : (decimal?)null;
                var unitPrice = ovUnitPrice ?? baseline;
                var lineDiscount = el.TryGetProperty("line_discount_amount", out var d) ? d.GetDecimal() : 0m;
                var lineTaxPreview = el.TryGetProperty("line_tax_preview", out var tp) ? tp.GetDecimal() : 0m;
                if (el.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String)
                    currency = c.GetString() ?? currency;
                var lineTotal = (unitPrice * qty) - lineDiscount + lineTaxPreview;
                lines.Add(new QuoteVersionPdfLine(sku, qty, unitPrice, lineDiscount, lineTaxPreview, lineTotal));
                subtotal += unitPrice * qty;
                totalDiscount += lineDiscount;
                totalTaxPreview += lineTaxPreview;
            }
        }
        catch (JsonException) { }
        return (lines, subtotal, totalDiscount, totalTaxPreview, subtotal - totalDiscount + totalTaxPreview, currency);
    }

    private static (string en, string ar) ExtractTerms(string termsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(termsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ("", "");
            var en = doc.RootElement.TryGetProperty("en", out var e) && e.ValueKind == JsonValueKind.String
                ? (e.GetString() ?? "") : "";
            var ar = doc.RootElement.TryGetProperty("ar", out var a) && a.ValueKind == JsonValueKind.String
                ? (a.GetString() ?? "") : "";
            return (en, ar);
        }
        catch (JsonException)
        {
            return ("", "");
        }
    }
}

public sealed record PublishQuoteVersionResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    PublishQuoteVersionResponse? Response)
{
    public static PublishQuoteVersionResult Success(PublishQuoteVersionResponse r) => new(true, 200, null, r);
    public static PublishQuoteVersionResult NotFound() => new(false, 404, QuoteReasonCode.QuoteNotFound, null);
    public static PublishQuoteVersionResult InvalidState() => new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null);
    public static PublishQuoteVersionResult RequiredFieldMissing() => new(false, 400, QuoteReasonCode.QuoteRequiredFieldMissing, null);
    // CodeRabbit Round 1: PdfFailed previously reused QuoteRequiredFieldMissing,
    // which is misleading. There is no dedicated PDF-failure reason code in the
    // contract, so we re-use QuoteInvalidStateForAction with a 500 status — the
    // PDF generation is part of publish state transition, and a 500 surface
    // matches §4.4 ("500 if PDF generation fails, the entire publish is rolled back").
    public static PublishQuoteVersionResult PdfFailed() => new(false, 500, QuoteReasonCode.QuoteInvalidStateForAction, null);
}
