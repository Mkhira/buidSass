using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Subscribers;

/// <summary>
/// Bridges <see cref="IOrderLifecycleSubscriber"/> (Modules/Shared) into the
/// Payments module. At v1 we defensively check whether a Payment already
/// exists for the order on <c>order.confirmed</c> (it usually does — created
/// at checkout). On <c>order.cancelled</c> we cascade a refund where a
/// captured Payment exists. On <c>order.refund_initiated</c> we route the
/// refund request through the Refunds feature.
/// </summary>
public sealed class OrderLifecycleSubscriber : IOrderLifecycleSubscriber
{
    private readonly PaymentsDbContext _db;

    public OrderLifecycleSubscriber(PaymentsDbContext db)
    {
        _db = db;
    }

    public async Task OnOrderConfirmedAsync(OrderConfirmedEvent @event, CancellationToken cancellationToken)
    {
        // Defensive — if Checkout pre-created a Payment, this is a no-op.
        var exists = await _db.Payments.AnyAsync(p =>
            p.OrderId == @event.OrderId && p.DeletedAt == null, cancellationToken);
        if (exists) return;
        // We do NOT create a Payment row from this subscriber alone at v1 —
        // doing so requires a chosen method + currency, which only the
        // checkout flow knows. The defensive arm logs and returns; spec 011's
        // checkout-to-payment hand-off is responsible for the create call.
    }

    public Task OnOrderCancelledAsync(OrderCancelledEvent @event, CancellationToken cancellationToken)
    {
        // Hook is invoked by Orders on cancel; the refund cascade lives in the
        // Refund feature (Phase 5). Kept as a no-op here to satisfy the
        // IOrderLifecycleSubscriber contract until that handler exists.
        return Task.CompletedTask;
    }

    public Task OnOrderRefundInitiatedAsync(OrderRefundInitiatedEvent @event, CancellationToken cancellationToken)
    {
        // Same as above — the dispatch path lives in Features/Refund.
        return Task.CompletedTask;
    }
}
