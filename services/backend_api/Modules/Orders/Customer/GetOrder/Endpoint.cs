using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.GetOrder;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetOrderEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-009 / FR-018. Customer detail view; rejects cross-account access.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        ReturnEligibilityEvaluator returnEligibility,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }

        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Shipments)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || order.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }

        var transitions = await db.StateTransitions.AsNoTracking()
            .Where(t => t.OrderId == id)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new { t.Machine, t.FromState, t.ToState, t.OccurredAt, t.Trigger, t.Reason })
            .ToListAsync(ct);

        var eligibility = returnEligibility.Evaluate(order, DateTimeOffset.UtcNow);
        var hls = HighLevelStatusProjector.Project(order.OrderState, order.PaymentState, order.FulfillmentState, order.RefundState);

        return Results.Ok(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            placedAt = order.PlacedAt,
            highLevelStatus = hls,
            states = new
            {
                order = order.OrderState,
                payment = order.PaymentState,
                fulfillment = order.FulfillmentState,
                refund = order.RefundState,
            },
            grandTotalMinor = order.GrandTotalMinor,
            subtotalMinor = order.SubtotalMinor,
            discountMinor = order.DiscountMinor,
            taxMinor = order.TaxMinor,
            shippingMinor = order.ShippingMinor,
            currency = order.Currency,
            shippingAddress = BackendApi.Modules.Orders.Primitives.AddressJson.Parse(order.ShippingAddressJson),
            billingAddress = BackendApi.Modules.Orders.Primitives.AddressJson.Parse(order.BillingAddressJson),
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
                lineTotalMinor = l.LineTotalMinor,
                restricted = l.Restricted,
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
            timeline = transitions,
            returnEligibility = new
            {
                eligible = eligibility.Eligible,
                daysRemaining = eligibility.DaysRemaining,
                reasonCode = eligibility.ReasonCode,
            },
            invoiceUrl = (string?)null,
        });
    }
}
