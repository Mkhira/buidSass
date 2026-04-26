using System.Globalization;
using System.Text;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.FinanceExport;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminFinanceExportEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/export", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.finance.export");
        return builder;
    }

    /// <summary>FR-014 + SC-007. Streaming CSV with credit-note adjustments per invoice.</summary>
    private static async Task HandleAsync(
        HttpContext context,
        InvoicesDbContext db,
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
        if (from is not null && to is not null && from > to)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("`from` must be on or before `to`.", ct);
            return;
        }
        var q = db.Invoices.AsNoTracking()
            .Include(i => i.CreditNotes)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(i => i.MarketCode == market);
        if (from is not null) q = q.Where(i => i.IssuedAt >= from);
        if (to is not null) q = q.Where(i => i.IssuedAt <= to);
        q = q.OrderBy(i => i.MarketCode).ThenBy(i => i.IssuedAt);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"invoices-finance-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv\"";
        await using var writer = new StreamWriter(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(
            "invoice_number,order_id,market,currency,issued_at,subtotal_minor,discount_minor,tax_minor,shipping_minor,grand_total_minor,credit_note_numbers,net_after_refunds_minor");

        await foreach (var inv in q.AsAsyncEnumerable().WithCancellation(ct))
        {
            var creditNoteNumbers = string.Join("|", inv.CreditNotes.Select(c => c.CreditNoteNumber));
            var refundedTotal = inv.CreditNotes.Sum(c => c.GrandTotalMinor);
            var netAfter = inv.GrandTotalMinor - refundedTotal;
            await writer.WriteLineAsync(
                $"{Csv(inv.InvoiceNumber)},{inv.OrderId},{Csv(inv.MarketCode)},{Csv(inv.Currency)},"
                + $"{inv.IssuedAt.ToString("o", CultureInfo.InvariantCulture)},"
                + $"{inv.SubtotalMinor},{inv.DiscountMinor},{inv.TaxMinor},{inv.ShippingMinor},{inv.GrandTotalMinor},"
                + $"{Csv(creditNoteNumbers)},{netAfter}");
            await writer.FlushAsync();
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
