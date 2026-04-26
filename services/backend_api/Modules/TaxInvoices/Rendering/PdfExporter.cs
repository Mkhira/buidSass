using BackendApi.Modules.Pdf;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// Wraps spec 003's <see cref="IPdfService"/> for the tax-invoice flow. Spec 003 owns the
/// underlying PDF library (QuestPDF); we feed it the registered <c>tax-invoice</c> template
/// with our render model. Phase 1B accepts the existing JSON-dump fallback in
/// <see cref="BackendApi.Modules.Pdf.Templates.TaxInvoiceTemplate"/>; a richer Razor-driven
/// layout is queued for Phase 1.5 (research R6).
/// </summary>
public sealed class PdfExporter(IPdfService pdfService, ILogger<PdfExporter> logger)
{
    public async Task<byte[]> ExportAsync(InvoiceRenderModel model, CancellationToken ct)
    {
        try
        {
            // Locale = AR keeps the Arabic-first layout; the bilingual model carries both
            // languages, so the PDF reader sees AR primary + EN secondary regardless.
            var bytes = await pdfService.RenderAsync("tax-invoice", LocaleCode.AR, model, ct);
            if (bytes is null || bytes.Length == 0)
            {
                throw new InvalidOperationException("PDF render returned empty bytes.");
            }
            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "invoices.pdf.export_failed invoiceNumber={Number} market={Market}",
                model.InvoiceNumber, model.MarketCode);
            throw;
        }
    }
}
