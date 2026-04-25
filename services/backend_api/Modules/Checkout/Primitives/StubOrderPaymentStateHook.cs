using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// No-op <see cref="IOrderPaymentStateHook"/> registered in Dev/Test ONLY when spec 011's
/// real handler isn't installed. Mirrors the <c>StubOrderFromCheckoutHandler</c> pattern.
/// Production environments MUST register Orders' real handler — the LAST registration wins
/// for single-resolution, so a present OrdersModule overrides this stub.
/// </summary>
public sealed class StubOrderPaymentStateHook(ILogger<StubOrderPaymentStateHook> logger) : IOrderPaymentStateHook
{
    public Task<OrderPaymentAdvanceResult> AdvanceFromAttemptAsync(
        OrderPaymentAdvanceRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "checkout.stub_order_payment_hook providerId={ProviderId} txnId={TxnId} attemptState={State} — no-op until spec 011 real handler is registered.",
            request.ProviderId, request.ProviderTxnId, request.MappedAttemptState);
        return Task.FromResult(new OrderPaymentAdvanceResult(false, null, null));
    }
}
