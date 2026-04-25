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
        // Reset to queued so the worker picks it up immediately. Attempts counter is preserved.
        job.State = InvoiceRenderJob.StateQueued;
        job.NextAttemptAt = DateTimeOffset.UtcNow;
        job.LastError = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { jobId = job.Id, state = job.State });
    }
}
