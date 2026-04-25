using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Admin.Common;
using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Admin.ResendInvoice;

public sealed record ResendRequest(string? Channel);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminResendInvoiceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/resend", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.resend");
        return builder;
    }

    /// <summary>FR-007. Triggers spec 019 notification (email/whatsapp) with the SAME stored
    /// PDF — never re-renders. Audited per FR-015.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        ResendRequest? body,
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
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return AdminInvoiceResponseFactory.Problem(context, 404, "invoice.not_found", "Invoice not found", "");
        }
        if (!string.Equals(invoice.State, Invoice.StateRendered, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(invoice.State, Invoice.StateDelivered, StringComparison.OrdinalIgnoreCase))
        {
            return AdminInvoiceResponseFactory.Problem(context, 409, "invoice.not_rendered",
                "Invoice must be rendered before resend.", "");
        }
        var channel = string.IsNullOrWhiteSpace(body?.Channel) ? "email" : body!.Channel!.ToLowerInvariant();
        if (channel is not "email" and not "whatsapp")
        {
            return AdminInvoiceResponseFactory.Problem(context, 400, "invoice.invalid_channel",
                "channel must be 'email' or 'whatsapp'.", "");
        }
        // The actual notification dispatch ships in spec 019. Phase 1B emits an outbox event
        // that the future notifications consumer will pick up.
        var nowUtc = DateTimeOffset.UtcNow;
        db.Outbox.Add(new InvoicesOutboxEntry
        {
            EventType = "invoice.resend_requested",
            AggregateId = invoice.Id,
            MarketCode = invoice.MarketCode,
            PayloadJson = JsonSerializer.Serialize(new
            {
                invoiceId = invoice.Id,
                invoiceNumber = invoice.InvoiceNumber,
                accountId = invoice.AccountId,
                channel,
                requestedBy = actor,
            }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        // CR R2 Critical fix — the resend intent is already committed via SaveChangesAsync
        // above. Returning 500 "aborted" after that would be a lie: a client retry would
        // enqueue a duplicate `invoice.resend_requested` event. Treat post-commit audit
        // publication failure as a warning (the state-machine trail is preserved by the
        // outbox row + spec 003's eventual-consistency replay path). The admin audit gap
        // surfaces as a missing entry in the audit_log_entries query, which finance-ops
        // dashboards already alert on.
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actor.Value,
                ActorRole: "admin",
                Action: "invoices.resend",
                EntityType: "invoices.invoice",
                EntityId: invoice.Id,
                BeforeState: null,
                AfterState: new { channel, invoiceNumber = invoice.InvoiceNumber },
                Reason: null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory?.CreateLogger("Invoices.Resend").LogWarning(ex,
                "invoices.resend.audit_publish_failed invoiceId={InvoiceId} channel={Channel}",
                invoice.Id, channel);
        }

        return Results.Ok(new { invoiceId = invoice.Id, channel, invoiceNumber = invoice.InvoiceNumber });
    }
}
