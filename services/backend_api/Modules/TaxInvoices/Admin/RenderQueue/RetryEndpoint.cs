using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        IAuditEventPublisher auditPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var actor = AdminInvoiceResponseFactory.ResolveActorAccountId(context);
        if (actor is null || actor == Guid.Empty)
        {
            return AdminInvoiceResponseFactory.Problem(context, 401, "invoice.actor_required", "Actor required", "");
        }

        // CR R2 Critical fix — atomic conditional UPDATE so a worker can't claim the row
        // between read and write. The clause restricts mutation to retry-eligible states
        // ('queued','failed') and explicitly RESETs Attempts to 0 — without that, an admin
        // retry of a job already at MaxAttempts would persist queued but the worker's
        // `Attempts < MaxAttempts` filter would skip it forever (per spec 012 round-2 finding).
        // ExecuteSqlInterpolatedAsync returns the affected row count; 0 = the precondition
        // wasn't met (already done / rendering / unknown id).
        var nowUtc = DateTimeOffset.UtcNow;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE invoices.invoice_render_jobs
            SET "State" = 'queued',
                "Attempts" = 0,
                "NextAttemptAt" = {nowUtc},
                "LastError" = NULL
            WHERE "Id" = {jobId}
              AND "State" IN ('queued', 'failed')
            """, ct);

        if (affected == 0)
        {
            // Distinguish 404 from 409 — re-fetch to know why the conditional didn't apply.
            var current = await db.RenderJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (current is null)
            {
                return AdminInvoiceResponseFactory.Problem(context, 404, "render_job.not_found",
                    "Render job not found", "");
            }
            if (string.Equals(current.State, InvoiceRenderJob.StateDone, StringComparison.OrdinalIgnoreCase))
            {
                return AdminInvoiceResponseFactory.Problem(context, 409, "render_job.already_done",
                    "Render job is already done; use Admin/RegenerateInvoice instead (preserves the invoice number, audited).",
                    "");
            }
            if (string.Equals(current.State, InvoiceRenderJob.StateRendering, StringComparison.OrdinalIgnoreCase))
            {
                return AdminInvoiceResponseFactory.Problem(context, 409, "render_job.rendering",
                    "Render job is in flight; wait for the worker to finish before forcing a retry.",
                    "");
            }
            return AdminInvoiceResponseFactory.Problem(context, 409, "render_job.invalid_state",
                $"Render job state '{current.State}' is not retry-eligible.", "");
        }

        // R2 Major — admin-forced render-queue retry IS a critical action; emit an audit row
        // (post-commit non-fatal pattern: the conditional UPDATE has already landed).
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actor.Value,
                ActorRole: "admin",
                Action: "invoices.render_queue.retry",
                EntityType: "invoices.render_job",
                EntityId: Guid.Empty, // jobId is bigserial; we record it in AfterState
                BeforeState: null,
                AfterState: new { jobId, state = InvoiceRenderJob.StateQueued, forcedAt = nowUtc },
                Reason: null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory?.CreateLogger("Invoices.RenderQueue.Retry").LogWarning(ex,
                "invoices.retry.audit_publish_failed jobId={JobId}", jobId);
        }

        return Results.Ok(new { jobId, state = InvoiceRenderJob.StateQueued });
    }
}
