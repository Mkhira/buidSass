using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.Quotations.GetQuotation;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCustomerGetQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }
        var q = await db.Quotations.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null || q.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.quote.not_found", "Quotation not found", "");
        }
        return Results.Ok(new
        {
            quotationId = q.Id,
            quoteNumber = q.QuoteNumber,
            status = q.Status,
            market = q.MarketCode,
            validUntil = q.ValidUntil,
            createdAt = q.CreatedAt,
            convertedOrderId = q.ConvertedOrderId,
            lines = q.Lines.Select(l => new
            {
                lineId = l.Id,
                productId = l.ProductId,
                sku = l.Sku,
                nameAr = l.NameAr,
                nameEn = l.NameEn,
                qty = l.Qty,
                unitPriceMinor = l.UnitPriceMinor,
                lineTaxMinor = l.LineTaxMinor,
                lineDiscountMinor = l.LineDiscountMinor,
                lineTotalMinor = l.LineTotalMinor,
                restricted = l.Restricted,
            }),
        });
    }
}
