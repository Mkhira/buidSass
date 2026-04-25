using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// Interim implementation of <see cref="IOrderFromCheckoutHandler"/> used until spec 011
/// lands. Generates a deterministic placeholder OrderId + OrderNumber so the Submit flow
/// can finalize a session without a real orders schema. Spec 011 will:
///   1. Replace this registration with its real handler.
///   2. Own the transactional order-row creation + reservation conversion (spec 008).
///   3. Emit `payment.captured` / `order.placed` on the outbox that spec 012 consumes.
/// Until then this stub logs loud enough that operators can see "checkout handed off to
/// a stub, not a real order service" in dev + staging environments.
/// </summary>
public sealed class StubOrderFromCheckoutHandler(ILogger<StubOrderFromCheckoutHandler> logger) : IOrderFromCheckoutHandler
{
    public Task<OrderFromCheckoutResult> CreateAsync(OrderFromCheckoutRequest request, CancellationToken cancellationToken)
    {
        // Honour Checkout's pre-allocated order id — Pricing's Issue explanation already
        // references this GUID, so the order row MUST use it too (spec 011 preserves contract).
        var orderId = request.PreallocatedOrderId;
        var orderNumber = $"STUB-{request.MarketCode.ToUpperInvariant()}-{orderId:N}"[..32];
        var paymentState = request.PaymentMethod switch
        {
            PaymentMethodCatalog.BankTransfer => "pending",
            PaymentMethodCatalog.Cod => "pending_cod",
            _ => "captured",
        };
        logger.LogWarning(
            "checkout.stub_order_created sessionId={SessionId} orderId={OrderId} — spec 011 will replace this handler.",
            request.SessionId, orderId);
        return Task.FromResult(new OrderFromCheckoutResult(
            IsSuccess: true,
            OrderId: orderId,
            OrderNumber: orderNumber,
            PaymentState: paymentState));
    }

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
