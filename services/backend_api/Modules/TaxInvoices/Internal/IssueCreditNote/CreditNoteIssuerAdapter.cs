using BackendApi.Modules.Shared;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Internal.IssueCreditNote;

/// <summary>
/// Spec 013 → spec 012 in-process adapter. Implements <see cref="ICreditNoteIssuer"/> by
/// resolving the order id to its invoice + invoice-line ids, then delegating to
/// <see cref="IssueCreditNoteHandler"/>. Returns invoices live in spec 012; spec 013 must NOT
/// take a hard dependency on TaxInvoices internals.
/// </summary>
public sealed class CreditNoteIssuerAdapter(InvoicesDbContext db, IssueCreditNoteHandler handler) : ICreditNoteIssuer
{
    public async Task<CreditNoteIssueResult> IssueForRefundAsync(
        CreditNoteIssueRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OrderId == Guid.Empty)
        {
            return new CreditNoteIssueResult(false, null, null, "credit_note.invalid_request",
                "orderId is required.");
        }
        if (request.RefundId == Guid.Empty)
        {
            return new CreditNoteIssueResult(false, null, null, "credit_note.invalid_request",
                "refundId is required.");
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return new CreditNoteIssueResult(false, null, null, "credit_note.invalid_request",
                "At least one credited line is required.");
        }

        var invoice = await db.Invoices.AsNoTracking()
            .Where(i => i.OrderId == request.OrderId)
            .Select(i => new { i.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return new CreditNoteIssueResult(false, null, null, "invoice.not_found",
                $"No invoice found for order {request.OrderId}.");
        }

        // Map orderLineId → invoiceLineId.
        var lineMap = await db.InvoiceLines.AsNoTracking()
            .Where(l => l.InvoiceId == invoice.Id)
            .Select(l => new { l.Id, l.OrderLineId })
            .ToDictionaryAsync(x => x.OrderLineId, x => x.Id, cancellationToken);

        // CR Major fix — aggregate duplicate OrderLineIds at this seam so the underlying
        // handler doesn't see two CreditNoteLineInputs for the same invoice line. Reject
        // non-positive cumulative qty as malformed input rather than punting downstream.
        var aggregated = request.Lines
            .GroupBy(l => l.OrderLineId)
            .Select(g => new { OrderLineId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToList();
        if (aggregated.Any(x => x.Qty <= 0))
        {
            return new CreditNoteIssueResult(false, null, null, "credit_note.invalid_request",
                "Credited quantities must be positive.");
        }

        var mappedLines = new List<CreditNoteLineInput>(aggregated.Count);
        foreach (var line in aggregated)
        {
            if (!lineMap.TryGetValue(line.OrderLineId, out var invoiceLineId))
            {
                return new CreditNoteIssueResult(false, null, null, "credit_note.line_not_found",
                    $"OrderLine {line.OrderLineId} has no matching invoice line.");
            }
            mappedLines.Add(new CreditNoteLineInput(invoiceLineId, line.Qty));
        }

        var result = await handler.IssueAsync(
            new IssueCreditNoteRequest(invoice.Id, request.RefundId, mappedLines, request.ReasonCode),
            cancellationToken);
        return new CreditNoteIssueResult(
            IsSuccess: result.IsSuccess,
            CreditNoteId: result.CreditNoteId,
            CreditNoteNumber: result.CreditNoteNumber,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.Detail);
    }
}
