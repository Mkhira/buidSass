using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.PreviewInvoice;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminPreviewInvoiceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}/pdf", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.read");
        return builder;
    }

    /// <summary>F3 — admin preview returns the same stored PDF (research R5 — immutable).</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        InvoicesDbContext db,
        IInvoiceBlobStore blobStore,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 404, "invoice.not_found", "Invoice not found", "");
        }
        if (string.IsNullOrWhiteSpace(invoice.PdfBlobKey))
        {
            return AdminInvoiceResponseFactory.Problem(context, 409, "invoice.not_rendered",
                "Invoice has not been rendered yet.", "");
        }
        var bytes = await blobStore.GetAsync(invoice.PdfBlobKey, ct);
        if (bytes is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 503, "invoice.blob_unavailable",
                "Invoice PDF is temporarily unavailable.", "");
        }
        return Results.File(bytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }
}
