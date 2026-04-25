using System.Globalization;
using System.Text;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.FinanceExport;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminFinanceExportEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/export", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.finance.export");
        return builder;
    }

    /// <summary>
    /// FR-010 / SC-007. Streaming CSV export with line-level tax + discount columns. Streamed
    /// row-by-row so multi-million-row exports don't hold the whole result set in memory.
    /// </summary>
    private static async Task HandleAsync(
        HttpContext context,
        OrdersDbContext db,
        string? market,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? format,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(format) && !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync($"Only format=csv is supported (got '{format}').", ct);
            return;
        }

        // Stream-friendly query: anonymous projection avoids change-tracking overhead.
        var q = db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(o => o.MarketCode == market);
        if (from is not null) q = q.Where(o => o.PlacedAt >= from);
        if (to is not null) q = q.Where(o => o.PlacedAt <= to);
        q = q.OrderBy(o => o.PlacedAt);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"orders-finance-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv\"";

        await using var writer = new StreamWriter(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        // Header row.
        await writer.WriteLineAsync(
            "order_number,placed_at,market,currency,grand_total_minor,subtotal_minor,discount_minor,tax_minor,shipping_minor,payment_state,fulfillment_state,refund_state,line_sku,line_qty,line_unit_price_minor,line_discount_minor,line_tax_minor,line_total_minor");

        await foreach (var order in q.AsAsyncEnumerable().WithCancellation(ct))
        {
            foreach (var line in order.Lines)
            {
                var row =
                    $"{Csv(order.OrderNumber)},{order.PlacedAt.ToString("o", CultureInfo.InvariantCulture)},"
                    + $"{Csv(order.MarketCode)},{Csv(order.Currency)},"
                    + $"{order.GrandTotalMinor},{order.SubtotalMinor},{order.DiscountMinor},{order.TaxMinor},{order.ShippingMinor},"
                    + $"{Csv(order.PaymentState)},{Csv(order.FulfillmentState)},{Csv(order.RefundState)},"
                    + $"{Csv(line.Sku)},{line.Qty},{line.UnitPriceMinor},{line.LineDiscountMinor},{line.LineTaxMinor},{line.LineTotalMinor}";
                await writer.WriteLineAsync(row);
            }
            await writer.FlushAsync();
        }
    }

    /// <summary>
    /// Minimal RFC 4180 CSV escaping: wrap fields containing comma / quote / newline in double
    /// quotes and double internal quotes. Sufficient for sku / state / market values which are
    /// citext-constrained, but the function defensively handles any string.
    /// </summary>
    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
