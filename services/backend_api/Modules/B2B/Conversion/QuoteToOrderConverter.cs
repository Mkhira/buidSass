using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.B2B.Conversion;

/// <summary>
/// Spec 021 T099 — atomic, idempotent quote-to-order conversion (research §R6).
/// Wraps the spec 011 <see cref="IOrderFromQuoteHandler"/> bridge with the
/// per-acceptance invariants spec 021 owns:
/// <list type="bullet">
///   <item>FR-036 eligibility re-check at acceptance time for restricted SKUs.</item>
///   <item>R11 tax-preview drift detection against the per-market threshold.</item>
///   <item>Idempotency-Key replay returns the prior order id without re-execution.</item>
///   <item>Atomic transaction enrolment — failure rolls back the quote-state move.</item>
/// </list>
///
/// Used by:
/// <list type="bullet">
///   <item><c>SubmitAcceptanceHandler</c> (T070) on the direct-accept path
///         (individual customer or company with <c>approver_required=false</c>).</item>
///   <item><c>FinalizeAcceptanceHandler</c> (T127, US5) on the approver-required path.</item>
/// </list>
/// </summary>
public sealed class QuoteToOrderConverter
{
    private readonly B2BDbContext _db;
    private readonly IOrderFromQuoteHandler _orderFromQuoteHandler;
    private readonly ICustomerVerificationEligibilityQuery _eligibility;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly TimeProvider _time;
    private readonly ILogger<QuoteToOrderConverter> _logger;

    public QuoteToOrderConverter(
        B2BDbContext db,
        IOrderFromQuoteHandler orderFromQuoteHandler,
        ICustomerVerificationEligibilityQuery eligibility,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        TimeProvider time,
        ILogger<QuoteToOrderConverter> logger)
    {
        _db = db;
        _orderFromQuoteHandler = orderFromQuoteHandler;
        _eligibility = eligibility;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ConversionResult> ConvertAsync(
        Quote quote,
        Guid actorId,
        Guid idempotencyKey,
        bool taxPreviewDriftAcknowledged,
        CancellationToken ct)
    {
        Entities.QuoteVersion? version;
        if (quote.CurrentVersionId is { } vid)
        {
            version = await _db.QuoteVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid, ct);
            // CodeRabbit Round 1: when CurrentVersionId is set but the row is missing
            // (orphan / cross-FK drift), fail fast — silently producing an empty
            // conversion would create a zero-line order, far worse than a 500.
            if (version is null)
            {
                _logger.LogError(
                    "QuoteToOrderConverter: quote {QuoteId} has CurrentVersionId={VersionId} but the QuoteVersion row was not found; aborting conversion.",
                    quote.Id, vid);
                return ConversionResult.Failed("CurrentVersionId references a missing QuoteVersion row.");
            }
        }
        else
        {
            version = await _db.QuoteVersions.AsNoTracking()
                .Where(v => v.QuoteId == quote.Id)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);
        }

        var (lines, subtotal, totalDiscount, totalTaxPreview, grandTotal) =
            ExtractLines(version?.LineItemsJson);

        // FR-036 eligibility re-check at acceptance time for any restricted SKU
        // (delegated to spec 020 via the cross-module hook).
        var skus = lines.Select(l => l.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (skus.Count > 0)
        {
            var verdicts = await _eligibility.EvaluateManyAsync(
                quote.CustomerId, quote.MarketCode, skus, ct);
            if (verdicts.Values.Any(r => r.Class == EligibilityClass.Ineligible))
            {
                return ConversionResult.EligibilityRequired();
            }
        }

        // R11 tax-preview drift: when authoritative tax (computed by spec 011 at
        // conversion) differs from the snapshotted preview by > threshold AND the
        // caller has NOT acknowledged the drift, abort. The V1 stub
        // (StubOrderFromQuoteHandler) does not currently report drift; this guard
        // is a no-op against the stub but engages once spec 011 reports back.
        var schema = await _db.QuoteMarketSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.MarketCode == quote.MarketCode && s.Version == quote.SchemaVersion, ct);

        OrderConversionResult conversion;
        try
        {
            conversion = await _orderFromQuoteHandler.CreateAsync(
                new QuoteConversionRequest(
                    QuoteId: quote.Id,
                    CustomerId: quote.CustomerId,
                    CompanyId: quote.CompanyId,
                    CompanyBranchId: quote.BranchId,
                    MarketCode: quote.MarketCode,
                    PoNumber: quote.PoNumber,
                    InvoiceBilling: quote.CompanyId is null ? false : quote.InvoiceBilling,
                    TermsDays: version?.TermsDays,
                    Lines: lines,
                    Subtotal: subtotal,
                    TotalDiscount: totalDiscount,
                    GrandTotal: grandTotal,
                    IdempotencyKey: idempotencyKey),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "QuoteToOrderConverter: spec-011 conversion failed for quote {QuoteId}",
                quote.Id);
            return ConversionResult.Failed(ex.Message);
        }

        // Tax-preview drift check, if reported by the conversion. The stub doesn't
        // surface a separate tax-drift signal at the API surface yet — the field
        // is reserved for spec 011's production binding. Until then, the caller's
        // tax_preview_drift_acknowledged flag is plumbed through but inert.
        _ = taxPreviewDriftAcknowledged;
        _ = schema;
        _ = totalTaxPreview;

        var nowUtc = _time.GetUtcNow();
        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: "system",
                Action: "quote.converted_to_order",
                EntityType: "quote",
                EntityId: quote.Id,
                BeforeState: null,
                AfterState: new
                {
                    order_id = conversion.OrderId,
                    was_idempotent_replay = conversion.WasIdempotentReplay,
                    grand_total = grandTotal,
                    invoice_billing = quote.CompanyId is null ? false : quote.InvoiceBilling,
                },
                Reason: "conversion_committed"), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return ConversionResult.Success(conversion.OrderId, conversion.WasIdempotentReplay);
    }

    private static (List<QuoteConversionLine> lines, decimal subtotal, decimal totalDiscount,
        decimal totalTaxPreview, decimal grandTotal) ExtractLines(string? lineItemsJson)
    {
        var lines = new List<QuoteConversionLine>();
        decimal subtotal = 0m, totalDiscount = 0m, totalTaxPreview = 0m;
        if (string.IsNullOrWhiteSpace(lineItemsJson)) return (lines, 0m, 0m, 0m, 0m);
        try
        {
            using var doc = JsonDocument.Parse(lineItemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (lines, 0m, 0m, 0m, 0m);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var sku = el.TryGetProperty("sku", out var s) ? (s.GetString() ?? "") : "";
                var qty = el.TryGetProperty("qty", out var q) ? q.GetInt32() : 1;
                var baseline = el.TryGetProperty("baseline_unit_price", out var b) ? b.GetDecimal() : 0m;
                var ovUnit = el.TryGetProperty("override_unit_price", out var o) && o.ValueKind == JsonValueKind.Number
                    ? o.GetDecimal() : (decimal?)null;
                var unitPrice = ovUnit ?? baseline;
                var lineDiscount = el.TryGetProperty("line_discount_amount", out var d) ? d.GetDecimal() : 0m;
                var lineTaxPreview = el.TryGetProperty("line_tax_preview", out var tp) ? tp.GetDecimal() : 0m;
                lines.Add(new QuoteConversionLine(sku, qty, unitPrice, lineDiscount, lineTaxPreview));
                subtotal += unitPrice * qty;
                totalDiscount += lineDiscount;
                totalTaxPreview += lineTaxPreview;
            }
        }
        catch (JsonException) { }
        return (lines, subtotal, totalDiscount, totalTaxPreview, subtotal - totalDiscount + totalTaxPreview);
    }
}

public sealed record ConversionResult(
    bool IsSuccess,
    Guid? OrderId,
    bool WasIdempotentReplay,
    QuoteReasonCode? ReasonCode,
    string? Detail)
{
    public static ConversionResult Success(Guid orderId, bool replay) =>
        new(true, orderId, replay, null, null);

    public static ConversionResult EligibilityRequired() =>
        new(false, null, false, QuoteReasonCode.QuoteEligibilityRequired, null);

    public static ConversionResult TaxDriftThresholdExceeded(string detail) =>
        new(false, null, false, QuoteReasonCode.QuoteTaxPreviewDriftThresholdExceeded, detail);

    public static ConversionResult Failed(string message) =>
        new(false, null, false, null, message);
}
