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
                // CR R2 Major fix — taking the first 80 chars still leaks message content
                // (DB hostnames, paths, stack-frame fragments). The worker stamps
                // `ExceptionType: message`; project ONLY the type token before the colon so
                // the admin UI sees a category, not user/system data. Operators with DB
                // access can inspect the full string out-of-band.
                lastErrorClass = j.LastError == null
                    ? null
                    : j.LastError.Split(':', 2, StringSplitOptions.TrimEntries)[0],
            })
            .Take(200)
            .ToListAsync(ct);
        return Results.Ok(new { jobs = rows });
    }
}
