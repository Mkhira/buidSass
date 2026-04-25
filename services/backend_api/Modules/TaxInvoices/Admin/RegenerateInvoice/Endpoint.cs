using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.RegenerateInvoice;

public sealed record RegenerateRequest(string Reason);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminRegenerateInvoiceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/regenerate", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.regenerate");
        return builder;
    }

    /// <summary>F4 / FR-010 / FR-015. Re-renders the PDF (same number, new SHA). Mandatory
    /// reason; audit row required. The invoice is moved back to <c>pending</c> and the worker
    /// will pick it up on the next poll.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        RegenerateRequest body,
        HttpContext context,
        InvoicesDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actor = AdminInvoiceResponseFactory.ResolveActorAccountId(context);
        if (actor is null || actor == Guid.Empty)
        {
            return AdminInvoiceResponseFactory.Problem(context, 401, "invoice.actor_required", "Actor required", "");
        }
        if (string.IsNullOrWhiteSpace(body.Reason))
        {
            return AdminInvoiceResponseFactory.Problem(context, 400, "invoice.regenerate.denied",
                "A non-empty reason is required.", "");
        }
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 404, "invoice.not_found", "Invoice not found", "");
        }

        var beforeSha = invoice.PdfSha256;
        var beforeKey = invoice.PdfBlobKey;
        var nowUtc = DateTimeOffset.UtcNow;
        invoice.State = Invoice.StatePending;
        invoice.PdfSha256 = null;
        invoice.LastError = null;
        invoice.UpdatedAt = nowUtc;
        db.RenderJobs.Add(new InvoiceRenderJob
        {
            InvoiceId = invoice.Id,
            MarketCode = invoice.MarketCode,
            State = InvoiceRenderJob.StateQueued,
            NextAttemptAt = nowUtc,
            CreatedAt = nowUtc,
        });
        // CR Major fix — emit `invoice.regenerate_queued` (a request event) at this stage.
        // The terminal `invoice.regenerated` event is the worker's responsibility once the
        // new PDF lands.
        db.Outbox.Add(new InvoicesOutboxEntry
        {
            EventType = "invoice.regenerate_queued",
            AggregateId = invoice.Id,
            MarketCode = invoice.MarketCode,
            PayloadJson = JsonSerializer.Serialize(new
            {
                invoiceId = invoice.Id,
                invoiceNumber = invoice.InvoiceNumber,
                reason = body.Reason,
                requestedBy = actor,
            }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        // CR Major fix — audit on regenerate is NOT optional. Surface 500 on failure so
        // the operator retries instead of leaving a silent gap in the compliance trail.
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actor.Value,
                ActorRole: "admin",
                Action: "invoices.regenerate",
                EntityType: "invoices.invoice",
                EntityId: invoice.Id,
                BeforeState: new { sha256 = beforeSha, blobKey = beforeKey, state = Invoice.StateRendered },
                AfterState: new { state = invoice.State, queuedAt = nowUtc },
                Reason: body.Reason), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AdminInvoiceResponseFactory.Problem(context, 500, "invoice.regenerate.audit_failed",
                "Audit emission failed; regenerate aborted (re-run when audit publisher is healthy).",
                ex.GetType().Name);
        }

        return Results.Ok(new
        {
            invoiceId = invoice.Id,
            invoiceNumber = invoice.InvoiceNumber,
            state = invoice.State,
            note = "Render queued; the same invoice number is preserved (FR-010).",
        });
    }
}
