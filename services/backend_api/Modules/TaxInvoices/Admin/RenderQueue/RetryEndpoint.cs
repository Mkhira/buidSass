using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.RenderQueue;

public static class RetryEndpoint
{
    public static IEndpointRouteBuilder MapAdminRetryRenderJobEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/render-queue/{jobId:long}/retry", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.regenerate");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        long jobId,
        HttpContext context,
        InvoicesDbContext db,
        CancellationToken ct)
    {
        var job = await db.RenderJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 404, "render_job.not_found", "Render job not found", "");
        }
        // CR Major fix — refuse to re-queue 'done' (would silently overwrite the immutable PDF
        // — research R5) or 'rendering' (would race a worker mid-flight). Admins regenerating
        // an issued PDF must use Admin/RegenerateInvoice (audited, preserves invoice number).
        if (string.Equals(job.State, InvoiceRenderJob.StateDone, StringComparison.OrdinalIgnoreCase))
        {
            return AdminInvoiceResponseFactory.Problem(context, 409, "render_job.already_done",
                "Render job is already done; use Admin/RegenerateInvoice instead (preserves the invoice number, audited).",
                "");
        }
        if (string.Equals(job.State, InvoiceRenderJob.StateRendering, StringComparison.OrdinalIgnoreCase))
        {
            return AdminInvoiceResponseFactory.Problem(context, 409, "render_job.rendering",
                "Render job is in flight; wait for the worker to finish before forcing a retry.",
                "");
        }
        if (!string.Equals(job.State, InvoiceRenderJob.StateFailed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.State, InvoiceRenderJob.StateQueued, StringComparison.OrdinalIgnoreCase))
        {
            return AdminInvoiceResponseFactory.Problem(context, 409, "render_job.invalid_state",
                $"Render job state '{job.State}' is not retry-eligible.", "");
        }
        // Reset to queued so the worker picks it up immediately. Attempts counter is preserved.
        job.State = InvoiceRenderJob.StateQueued;
        job.NextAttemptAt = DateTimeOffset.UtcNow;
        job.LastError = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { jobId = job.Id, state = job.State });
    }
}
