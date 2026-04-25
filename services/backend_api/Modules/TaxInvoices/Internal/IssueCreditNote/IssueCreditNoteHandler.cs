using System.Text.Json;
using BackendApi.Modules.Observability;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Primitives;
using BackendApi.Modules.TaxInvoices.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Internal.IssueCreditNote;

/// <summary>
/// FR-008 / FR-009 — issues a credit note triggered by spec 013's refund event. Tax rate is
/// always the original invoice's stored rate (research R7); the original invoice is never
/// mutated. Idempotent on <c>refundId</c> via the unique partial index defined on
/// <c>credit_notes.RefundId</c>.
/// </summary>
public sealed record CreditNoteLineInput(Guid InvoiceLineId, int Qty);

public sealed record IssueCreditNoteRequest(
    Guid InvoiceId,
    Guid RefundId,
    IReadOnlyList<CreditNoteLineInput> Lines,
    string ReasonCode);

public sealed record IssueCreditNoteResult(
    bool IsSuccess,
    Guid? CreditNoteId,
    string? CreditNoteNumber,
    string? ErrorCode,
    string? Detail);

public sealed class IssueCreditNoteHandler(
    InvoicesDbContext invoicesDb,
    CreditNoteNumberSequencer sequencer,
    InvoiceTemplateResolver templateResolver,
    ZatcaQrEmbedder qrEmbedder,
    InvoicesMetrics metrics,
    ILogger<IssueCreditNoteHandler> logger)
{
    public async Task<IssueCreditNoteResult> IssueAsync(IssueCreditNoteRequest request, CancellationToken ct)
    {
        if (request.InvoiceId == Guid.Empty || request.RefundId == Guid.Empty)
        {
            return new IssueCreditNoteResult(false, null, null, "credit_note.invalid_request",
                "invoiceId and refundId are required.");
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return new IssueCreditNoteResult(false, null, null, "credit_note.invalid_request",
                "At least one credited line is required.");
        }
        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return new IssueCreditNoteResult(false, null, null, "credit_note.invalid_request",
                "reasonCode is required.");
        }

        // Idempotency: refundId is unique across the table.
        var existing = await invoicesDb.CreditNotes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.RefundId == request.RefundId, ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "invoices.credit_note.idempotent_hit refundId={RefundId} creditNoteNumber={Number}",
                request.RefundId, existing.CreditNoteNumber);
            return new IssueCreditNoteResult(true, existing.Id, existing.CreditNoteNumber, null, null);
        }

        var invoice = await invoicesDb.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct);
        if (invoice is null)
        {
            return new IssueCreditNoteResult(false, null, null, "invoice.not_found",
                "Original invoice not found.");
        }

        // B2 fix — cumulative refund check requires summing prior credit notes against THIS
        // invoice. Without this, two partial refunds could each individually pass the
        // line-vs-original check but together exceed the invoice grand total.
        var priorRefundedTotal = await invoicesDb.CreditNotes.AsNoTracking()
            .Where(c => c.InvoiceId == invoice.Id)
            .SumAsync(c => (long?)c.GrandTotalMinor, ct) ?? 0L;

        // B3 fix — sum prior credited qty PER ORIGINAL LINE so a third partial credit can't
        // sneak past the per-line check. Done in one bulk query rather than per-line round trips.
        var priorCreditedByLine = await invoicesDb.CreditNoteLines.AsNoTracking()
            .Where(cnl => invoice.Lines.Select(l => l.Id).Contains(cnl.InvoiceLineId))
            .GroupBy(cnl => cnl.InvoiceLineId)
            .Select(g => new { LineId = g.Key, TotalQty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.LineId, x => x.TotalQty, ct);

        // Validate and snapshot the credited lines from the original invoice (preserves tax rate).
        var creditNoteLines = new List<CreditNoteLine>();
        long subtotal = 0, discount = 0, tax = 0, grand = 0;
        foreach (var input in request.Lines)
        {
            if (input.Qty <= 0)
            {
                return new IssueCreditNoteResult(false, null, null, "credit_note.invalid_request",
                    $"Line {input.InvoiceLineId} qty must be positive.");
            }
            var originLine = invoice.Lines.FirstOrDefault(l => l.Id == input.InvoiceLineId);
            if (originLine is null)
            {
                return new IssueCreditNoteResult(false, null, null, "credit_note.line_not_found",
                    $"Invoice line {input.InvoiceLineId} not found on invoice {invoice.Id}.");
            }
            var priorCreditedQty = priorCreditedByLine.GetValueOrDefault(originLine.Id, 0);
            if (priorCreditedQty + input.Qty > originLine.Qty)
            {
                return new IssueCreditNoteResult(false, null, null, "credit_note.line_exceeds_invoice",
                    $"Credited qty {priorCreditedQty + input.Qty} exceeds invoice line qty {originLine.Qty} (prior {priorCreditedQty} + this {input.Qty}).");
            }
            // Pro-rate amounts by qty using the original tax rate. UnitPriceMinor × Qty is a
            // long; the discount/tax pro-rate uses decimal to avoid integer truncation, then
            // rounds back to minor units.
            var ratio = (decimal)input.Qty / originLine.Qty;
            var lineSubtotal = (long)originLine.UnitPriceMinor * input.Qty;
            var lineDiscount = (long)Math.Round(originLine.LineDiscountMinor * ratio);
            var lineTax = (long)Math.Round(originLine.LineTaxMinor * ratio);
            var lineTotal = lineSubtotal - lineDiscount + lineTax;

            subtotal += lineSubtotal;
            discount += lineDiscount;
            tax += lineTax;
            grand += lineTotal;

            creditNoteLines.Add(new CreditNoteLine
            {
                Id = Guid.NewGuid(),
                InvoiceLineId = originLine.Id,
                Sku = originLine.Sku,
                NameAr = originLine.NameAr,
                NameEn = originLine.NameEn,
                Qty = input.Qty,
                UnitPriceMinor = originLine.UnitPriceMinor,
                LineDiscountMinor = lineDiscount,
                LineTaxMinor = lineTax,
                LineTotalMinor = lineTotal,
                TaxRateBp = originLine.TaxRateBp,
            });
        }
        // B2 fix — cumulative refund total (existing prior + this) cannot exceed the original
        // invoice grand total.
        if (priorRefundedTotal + grand > invoice.GrandTotalMinor)
        {
            return new IssueCreditNoteResult(false, null, null, "credit_note.line_exceeds_invoice",
                $"Cumulative credit-note total {priorRefundedTotal + grand} (prior {priorRefundedTotal} + this {grand}) "
                + $"exceeds invoice grand total {invoice.GrandTotalMinor}.");
        }

        InvoiceTemplate template;
        try { template = await templateResolver.LoadAsync(invoice.MarketCode, ct); }
        catch (InvalidOperationException ex)
        {
            return new IssueCreditNoteResult(false, null, null, "invoice.template.missing", ex.Message);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var creditNoteNumber = await sequencer.NextAsync(invoice.MarketCode, nowUtc, ct);
        var creditNote = new CreditNote
        {
            Id = Guid.NewGuid(),
            CreditNoteNumber = creditNoteNumber,
            InvoiceId = invoice.Id,
            RefundId = request.RefundId,
            IssuedAt = nowUtc,
            SubtotalMinor = subtotal,
            DiscountMinor = discount,
            TaxMinor = tax,
            ShippingMinor = 0,
            GrandTotalMinor = grand,
            ReasonCode = request.ReasonCode,
            State = CreditNote.StatePending,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        foreach (var line in creditNoteLines) { line.CreditNoteId = creditNote.Id; creditNote.Lines.Add(line); }
        creditNote.ZatcaQrB64 = qrEmbedder.BuildIfApplicable(invoice.MarketCode, template, creditNote);

        invoicesDb.CreditNotes.Add(creditNote);
        invoicesDb.RenderJobs.Add(new InvoiceRenderJob
        {
            CreditNoteId = creditNote.Id,
            State = InvoiceRenderJob.StateQueued,
            NextAttemptAt = nowUtc,
            CreatedAt = nowUtc,
        });
        invoicesDb.Outbox.Add(new InvoicesOutboxEntry
        {
            EventType = "credit_note.issued",
            AggregateId = creditNote.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                creditNoteId = creditNote.Id,
                creditNoteNumber = creditNote.CreditNoteNumber,
                invoiceId = invoice.Id,
                invoiceNumber = invoice.InvoiceNumber,
                refundId = request.RefundId,
                grandTotalMinor = creditNote.GrandTotalMinor,
                currency = invoice.Currency,
                reasonCode = creditNote.ReasonCode,
            }),
            CommittedAt = nowUtc,
        });

        try
        {
            await invoicesDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            var raced = await invoicesDb.CreditNotes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.RefundId == request.RefundId, ct);
            if (raced is not null)
            {
                return new IssueCreditNoteResult(true, raced.Id, raced.CreditNoteNumber, null, null);
            }
            return new IssueCreditNoteResult(false, null, null, "credit_note.duplicate_key", ex.Message);
        }

        metrics.IncrementCreditNoteIssued(invoice.MarketCode);
        logger.LogInformation(
            "invoices.credit_note.issued invoiceId={InvoiceId} creditNoteNumber={Number} refundId={RefundId}",
            invoice.Id, creditNoteNumber, request.RefundId);
        return new IssueCreditNoteResult(true, creditNote.Id, creditNote.CreditNoteNumber, null, null);
    }
}
