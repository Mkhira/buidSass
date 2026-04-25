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

namespace BackendApi.Modules.Orders.Admin.Quotations.ExpireQuotation;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminExpireQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/expire", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.quotations.write");
        return builder;
    }

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
        if (string.Equals(quote.Status, Quotation.StatusExpired, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { quotationId = quote.Id, status = quote.Status });
        }
        if (!string.Equals(quote.Status, Quotation.StatusActive, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(quote.Status, Quotation.StatusDraft, StringComparison.OrdinalIgnoreCase))
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.quote.invalid_status",
                $"Cannot expire from status '{quote.Status}'", "");
        }
        var fromStatus = quote.Status;
        var nowUtc = DateTimeOffset.UtcNow;
        quote.Status = Quotation.StatusExpired;
        quote.UpdatedAt = nowUtc;
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "quote.expired",
            AggregateId = quote.Id,
            PayloadJson = JsonSerializer.Serialize(new { quotationId = quote.Id, expiredBy = "admin" }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, quote.Id, actor.Value,
            "orders.quotation.expire", new { status = fromStatus }, new { status = quote.Status }, null, ct);

        return Results.Ok(new { quotationId = quote.Id, status = quote.Status });
    }
}
