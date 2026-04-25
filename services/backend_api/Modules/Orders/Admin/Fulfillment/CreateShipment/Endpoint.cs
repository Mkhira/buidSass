using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Admin.Fulfillment.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.Fulfillment.CreateShipment;

public sealed record CreateShipmentRequest(
    string ProviderId,
    string MethodCode,
    string? TrackingNumber,
    string? CarrierLabelUrl,
    DateTimeOffset? EtaFrom,
    DateTimeOffset? EtaTo);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCreateShipmentEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/fulfillment/create-shipment", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.fulfillment.write");
        return builder;
    }

    /// <summary>FR-006. One order may produce N shipments — each call appends a shipment row.
    /// The order's fulfillment_state is left at <c>packed</c> until <c>mark-handed-to-carrier</c>
    /// fires; this endpoint is a pure "physical shipment recorded" event.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        CreateShipmentRequest body,
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
        if (string.IsNullOrWhiteSpace(body.ProviderId) || string.IsNullOrWhiteSpace(body.MethodCode))
        {
            return AdminOrdersResponseFactory.Problem(context, 400, "order.shipment.invalid_request",
                "providerId and methodCode are required", "");
        }
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }
        // Spec: shipments are created from `packed`. This is a soft constraint — admins occasionally
        // need to record a shipment after handed_to_carrier (e.g., second-leg fulfillment).
        if (string.Equals(order.FulfillmentState, FulfillmentSm.Cancelled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.FulfillmentState, FulfillmentSm.NotStarted, StringComparison.OrdinalIgnoreCase))
        {
            return AdminOrdersResponseFactory.Problem(context, 409, "order.fulfillment.not_ready",
                "Cannot create shipment from current fulfillment state", "",
                new Dictionary<string, object?> { ["currentState"] = order.FulfillmentState });
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProviderId = body.ProviderId,
            MethodCode = body.MethodCode,
            TrackingNumber = body.TrackingNumber,
            CarrierLabelUrl = body.CarrierLabelUrl,
            EtaFrom = body.EtaFrom,
            EtaTo = body.EtaTo,
            State = Shipment.StateCreated,
            CreatedAt = nowUtc,
            PayloadJson = "{}",
        };
        db.Shipments.Add(shipment);
        order.UpdatedAt = nowUtc;
        db.Outbox.Add(FulfillmentOps.NewOutbox(order, "fulfillment.shipment_created", new
        {
            shipmentId = shipment.Id,
            providerId = body.ProviderId,
            methodCode = body.MethodCode,
            trackingNumber = body.TrackingNumber,
        }));
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, order.Id, actor.Value,
            "orders.fulfillment.create_shipment",
            null,
            new
            {
                shipmentId = shipment.Id,
                providerId = body.ProviderId,
                methodCode = body.MethodCode,
                trackingNumber = body.TrackingNumber,
            },
            null, ct);

        return Results.Ok(new
        {
            shipmentId = shipment.Id,
            orderId = order.Id,
            state = shipment.State,
        });
    }
}
