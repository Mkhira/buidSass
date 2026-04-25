using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.GetOrder;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetOrderEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.read");
        return builder;
    }

    /// <summary>FR-010. Admin detail — full state visibility + transitions.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Shipments)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return AdminOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }

        var transitions = await db.StateTransitions.AsNoTracking()
            .Where(t => t.OrderId == id)
            .OrderBy(t => t.OccurredAt)
            .ToListAsync(ct);

        var hls = HighLevelStatusProjector.Project(order.OrderState, order.PaymentState, order.FulfillmentState, order.RefundState);

        return Results.Ok(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            accountId = order.AccountId,
            market = order.MarketCode,
            currency = order.Currency,
            placedAt = order.PlacedAt,
            cancelledAt = order.CancelledAt,
            deliveredAt = order.DeliveredAt,
            grandTotalMinor = order.GrandTotalMinor,
            subtotalMinor = order.SubtotalMinor,
            discountMinor = order.DiscountMinor,
            taxMinor = order.TaxMinor,
            shippingMinor = order.ShippingMinor,
            states = new
            {
                order = order.OrderState,
                payment = order.PaymentState,
                fulfillment = order.FulfillmentState,
                refund = order.RefundState,
            },
            highLevelStatus = hls,
            paymentProviderId = order.PaymentProviderId,
            paymentProviderTxnId = order.PaymentProviderTxnId,
            checkoutSessionId = order.CheckoutSessionId,
            quotationId = order.QuotationId,
            shippingAddress = System.Text.Json.JsonDocument.Parse(order.ShippingAddressJson).RootElement,
            billingAddress = System.Text.Json.JsonDocument.Parse(order.BillingAddressJson).RootElement,
            lines = order.Lines.Select(l => new
            {
                lineId = l.Id,
                productId = l.ProductId,
                sku = l.Sku,
                nameAr = l.NameAr,
                nameEn = l.NameEn,
                qty = l.Qty,
                cancelledQty = l.CancelledQty,
                returnedQty = l.ReturnedQty,
                unitPriceMinor = l.UnitPriceMinor,
                lineTaxMinor = l.LineTaxMinor,
                lineDiscountMinor = l.LineDiscountMinor,
                lineTotalMinor = l.LineTotalMinor,
                restricted = l.Restricted,
                reservationId = l.ReservationId,
            }),
            shipments = order.Shipments.Select(s => new
            {
                shipmentId = s.Id,
                providerId = s.ProviderId,
                methodCode = s.MethodCode,
                trackingNumber = s.TrackingNumber,
                state = s.State,
                etaFrom = s.EtaFrom,
                etaTo = s.EtaTo,
                handedToCarrierAt = s.HandedToCarrierAt,
                deliveredAt = s.DeliveredAt,
            }),
            transitions = transitions.Select(t => new
            {
                t.Machine,
                t.FromState,
                t.ToState,
                t.OccurredAt,
                t.ActorAccountId,
                t.Trigger,
                t.Reason,
            }),
        });
    }
}
