using BackendApi.Modules.TaxInvoices.Customer.Common;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Customer.GetInvoicePdf;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetInvoicePdfEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{orderId:guid}/invoice.pdf", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-006 / FR-020. Streams the stored PDF for the customer's own invoice.</summary>
    private static async Task<IResult> HandleAsync(
        Guid orderId,
        HttpContext context,
        InvoicesDbContext db,
        IInvoiceBlobStore blobStore,
        CancellationToken ct)
    {
        var accountId = CustomerInvoiceResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerInvoiceResponseFactory.Problem(context, 401, "invoice.requires_auth", "Auth required", "");
        }
        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
        if (invoice is null || invoice.AccountId != accountId)
        {
            return CustomerInvoiceResponseFactory.Problem(context, 404, "invoice.not_found", "Invoice not found", "");
        }
        if (!string.Equals(invoice.State, Invoice.StateRendered, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(invoice.State, Invoice.StateDelivered, StringComparison.OrdinalIgnoreCase))
        {
            // Per contract: 409 with Retry-After while the render queue is still working on it.
            context.Response.Headers["Retry-After"] = "10";
            return CustomerInvoiceResponseFactory.Problem(context, 409, "invoice.render_pending",
                "Invoice is queued for rendering — try again shortly.", "");
        }
        if (string.IsNullOrWhiteSpace(invoice.PdfBlobKey))
        {
            return CustomerInvoiceResponseFactory.Problem(context, 409, "invoice.render_pending",
                "Invoice was rendered without a blob key — operations notified.", "");
        }
        var bytes = await blobStore.GetAsync(invoice.PdfBlobKey, ct);
        if (bytes is null)
        {
            return CustomerInvoiceResponseFactory.Problem(context, 503, "invoice.blob_unavailable",
                "Invoice PDF is temporarily unavailable. Retry later.", "");
        }
        return Results.File(bytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }
}
