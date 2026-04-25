using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.RenderQueue;

public static class ListEndpoint
{
    public static IEndpointRouteBuilder MapAdminListRenderQueueEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/render-queue", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.read");
        return builder;
    }

    /// <summary>FR-013 — render-queue inspector (stuck jobs).</summary>
    private static async Task<IResult> HandleAsync(InvoicesDbContext db, CancellationToken ct)
    {
        var rows = await db.RenderJobs.AsNoTracking()
            .Where(j => j.State == "queued" || j.State == "failed" || j.State == "rendering")
            .OrderBy(j => j.NextAttemptAt)
            .Select(j => new
            {
                jobId = j.Id,
                invoiceId = j.InvoiceId,
                creditNoteId = j.CreditNoteId,
                market = j.MarketCode,
                state = j.State,
                attempts = j.Attempts,
                nextAttemptAt = j.NextAttemptAt,
                // CR Major fix — never expose raw `lastError` strings. Worker may stamp DB
                // connection messages or stack-frame fragments; admin UI sees a redacted
                // summary only. Operators with DB access can inspect the full string.
                lastErrorClass = j.LastError == null ? null : j.LastError.Substring(0, Math.Min(80, j.LastError.Length)),
            })
            .Take(200)
            .ToListAsync(ct);
        return Results.Ok(new { jobs = rows });
    }
}
