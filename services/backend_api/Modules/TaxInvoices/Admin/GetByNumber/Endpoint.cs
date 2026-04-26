using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.GetByNumber;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetByNumberEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/by-number/{invoiceNumber}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.read");
        return builder;
    }

    /// <summary>FR-018 — finance-friendly shortcut search by invoice number.</summary>
    private static async Task<IResult> HandleAsync(
        string invoiceNumber,
        HttpContext context,
        InvoicesDbContext db,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber, ct);
        if (invoice is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 404, "invoice.not_found", "Invoice not found", "");
        }
        return Results.Ok(new { invoiceId = invoice.Id, invoiceNumber = invoice.InvoiceNumber, orderId = invoice.OrderId });
    }
}
