using System.Globalization;
using System.Text;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.ReturnsExport;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminReturnsExportEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/export", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.read");
        return builder;
    }

    /// <summary>FR-016. CSV export. Capped at 10 000 rows per call to keep memory bounded;
    /// callers paginate by date range.</summary>
    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ReturnsDbContext db,
        string? market,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? format,
        CancellationToken ct)
    {
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(format))
        {
            return ReturnsResponseFactory.Problem(context, 400, "export.format.unsupported",
                "Only format=csv is supported.");
        }

        var q = db.ReturnRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(r => r.MarketCode == market);
        if (from is { } f) q = q.Where(r => r.SubmittedAt >= f);
        if (to is { } t) q = q.Where(r => r.SubmittedAt <= t);
        var rows = await q.OrderBy(r => r.SubmittedAt).Take(10_000)
            .Select(r => new
            {
                r.Id,
                r.ReturnNumber,
                r.OrderId,
                r.MarketCode,
                r.State,
                r.ReasonCode,
                r.SubmittedAt,
                r.DecidedAt,
                r.ForceRefund,
                LineCount = r.Lines.Count,
                TotalRefunded = r.Refunds
                    .Where(rf => rf.State == "completed")
                    .Sum(rf => (long?)rf.AmountMinor) ?? 0L,
                Currency = r.Refunds.Select(rf => rf.Currency).FirstOrDefault() ?? "",
            })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("id,return_number,order_id,market_code,state,reason_code,submitted_at,decided_at,force_refund,line_count,total_refunded_minor,currency");
        foreach (var r in rows)
        {
            // CR Minor: escape every emitted string column — a stray comma/quote/newline
            // in the return number, market, state, or currency would otherwise break the
            // CSV row. The ID and ISO-8601 timestamps are safe because their character set
            // is constrained.
            sb.Append(r.Id).Append(',');
            sb.Append(Csv(r.ReturnNumber)).Append(',');
            sb.Append(r.OrderId).Append(',');
            sb.Append(Csv(r.MarketCode)).Append(',');
            sb.Append(Csv(r.State)).Append(',');
            sb.Append(Csv(r.ReasonCode)).Append(',');
            sb.Append(r.SubmittedAt.ToString("o", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DecidedAt?.ToString("o", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(r.ForceRefund ? "true" : "false").Append(',');
            sb.Append(r.LineCount).Append(',');
            sb.Append(r.TotalRefunded).Append(',');
            sb.Append(Csv(r.Currency)).AppendLine();
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Results.File(bytes, "text/csv; charset=utf-8", "returns_export.csv");
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
