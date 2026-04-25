using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Primitives;

namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// FR-004 — generates the ZATCA Phase 1 TLV-base64 string for KSA invoices and threads it
/// onto the render model. Egypt invoices skip the QR (research R12). The actual QR image
/// rendering happens client-side / inside the PDF template — the renderer receives the
/// base64 string and embeds it (currently as a code block; a true QR pixmap is queued for
/// Phase 1.5).
/// </summary>
public sealed class ZatcaQrEmbedder
{
    public string? BuildIfApplicable(string marketCode, InvoiceTemplate template, Invoice invoice)
    {
        if (!string.Equals(marketCode, "KSA", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return ZatcaQrTlvBuilder.Build(
            sellerName: template.SellerLegalNameAr,
            sellerVatNumber: template.SellerVatNumber,
            invoiceTimestamp: invoice.IssuedAt,
            totalWithVatMinor: invoice.GrandTotalMinor,
            vatTotalMinor: invoice.TaxMinor);
    }

    public string? BuildIfApplicable(string marketCode, InvoiceTemplate template, CreditNote creditNote)
    {
        if (!string.Equals(marketCode, "KSA", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        // Credit notes carry negative totals at the wire layer but ZATCA Phase 1 expects
        // unsigned positive values for the QR (the document type marks the credit-note
        // semantics elsewhere). We absolute-value here.
        return ZatcaQrTlvBuilder.Build(
            sellerName: template.SellerLegalNameAr,
            sellerVatNumber: template.SellerVatNumber,
            invoiceTimestamp: creditNote.IssuedAt,
            totalWithVatMinor: Math.Abs(creditNote.GrandTotalMinor),
            vatTotalMinor: Math.Abs(creditNote.TaxMinor));
    }
}
