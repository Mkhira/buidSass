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
            .Where(j => j.State == "queued" || j.State == "failed")
            .OrderBy(j => j.NextAttemptAt)
            .Select(j => new
            {
                jobId = j.Id,
                invoiceId = j.InvoiceId,
                creditNoteId = j.CreditNoteId,
                state = j.State,
                attempts = j.Attempts,
                nextAttemptAt = j.NextAttemptAt,
                lastError = j.LastError,
            })
            .Take(200)
            .ToListAsync(ct);
        return Results.Ok(new { jobs = rows });
    }
}
