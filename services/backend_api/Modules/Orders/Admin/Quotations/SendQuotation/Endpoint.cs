using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Admin.Fulfillment.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.Quotations.SendQuotation;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminSendQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/send", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.quotations.write");
        return builder;
    }

    /// <summary>FR-011. Activates a draft quotation: status draft → active. Customer can
    /// now view/accept/reject. Notification dispatch is a downstream concern picked up by
    /// spec 014 via the <c>quote.sent</c> outbox event.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actor = AdminOrdersResponseFactory.ResolveActorAccountId(context);
        if (actor is null || actor == Guid.Empty)
        {
            return AdminOrdersResponseFactory.Problem(context, 401, "orders.actor_required", "Actor required", "");
        }
        var quote = await db.Quotations.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.quote.not_found", "Quotation not found", "");
        }
        if (!string.Equals(quote.Status, Quotation.StatusDraft, StringComparison.OrdinalIgnoreCase))
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.quote.invalid_status",
                $"Cannot send from status '{quote.Status}'", "");
        }
        var fromStatus = quote.Status;
        var nowUtc = DateTimeOffset.UtcNow;
        quote.Status = Quotation.StatusActive;
        quote.UpdatedAt = nowUtc;
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "quote.sent",
            AggregateId = quote.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                quotationId = quote.Id,
                accountId = quote.AccountId,
                quoteNumber = quote.QuoteNumber,
            }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, quote.Id, actor.Value,
            "orders.quotation.send", new { status = fromStatus }, new { status = quote.Status }, null, ct);

        return Results.Ok(new { quotationId = quote.Id, status = quote.Status });
    }
}
