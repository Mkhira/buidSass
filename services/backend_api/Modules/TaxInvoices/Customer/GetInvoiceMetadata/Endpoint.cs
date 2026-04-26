using BackendApi.Modules.TaxInvoices.Customer.Common;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Customer.GetInvoiceMetadata;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetInvoiceMetadataEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{orderId:guid}/invoice", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid orderId,
        HttpContext context,
        InvoicesDbContext db,
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
        var pdfAvailable = string.Equals(invoice.State, Invoice.StateRendered, StringComparison.OrdinalIgnoreCase)
            || string.Equals(invoice.State, Invoice.StateDelivered, StringComparison.OrdinalIgnoreCase);
        return Results.Ok(new
        {
            invoiceNumber = invoice.InvoiceNumber,
            issuedAt = invoice.IssuedAt,
            currency = invoice.Currency,
            grandTotalMinor = invoice.GrandTotalMinor,
            state = invoice.State,
            pdfAvailable,
        });
    }
}
