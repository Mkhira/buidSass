using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.GetInvoice;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetInvoiceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        InvoicesDbContext db,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.CreditNotes).ThenInclude(c => c.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 404, "invoice.not_found", "Invoice not found", "");
        }
        return Results.Ok(new
        {
            invoiceId = invoice.Id,
            invoiceNumber = invoice.InvoiceNumber,
            orderId = invoice.OrderId,
            accountId = invoice.AccountId,
            market = invoice.MarketCode,
            currency = invoice.Currency,
            issuedAt = invoice.IssuedAt,
            state = invoice.State,
            grandTotalMinor = invoice.GrandTotalMinor,
            subtotalMinor = invoice.SubtotalMinor,
            discountMinor = invoice.DiscountMinor,
            taxMinor = invoice.TaxMinor,
            shippingMinor = invoice.ShippingMinor,
            pdfSha256 = invoice.PdfSha256,
            pdfBlobKey = invoice.PdfBlobKey,
            zatcaQrB64 = invoice.ZatcaQrB64,
            renderAttempts = invoice.RenderAttempts,
            lastError = invoice.LastError,
            lines = invoice.Lines.Select(l => new
            {
                lineId = l.Id,
                originLineId = l.OrderLineId,
                sku = l.Sku,
                nameAr = l.NameAr,
                nameEn = l.NameEn,
                qty = l.Qty,
                unitPriceMinor = l.UnitPriceMinor,
                lineDiscountMinor = l.LineDiscountMinor,
                lineTaxMinor = l.LineTaxMinor,
                lineTotalMinor = l.LineTotalMinor,
                taxRateBp = l.TaxRateBp,
            }),
            creditNotes = invoice.CreditNotes.Select(c => new
            {
                creditNoteId = c.Id,
                creditNoteNumber = c.CreditNoteNumber,
                refundId = c.RefundId,
                issuedAt = c.IssuedAt,
                state = c.State,
                grandTotalMinor = c.GrandTotalMinor,
                reasonCode = c.ReasonCode,
            }),
        });
    }
}
