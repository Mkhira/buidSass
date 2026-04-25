using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H5 / SC-005 — Order-level webhook dedup. The PaymentWebhookAdvanceHandler
/// (IOrderPaymentStateHook) MUST be idempotent: 100 deliveries with the same providerEventId
/// produce ONE state mutation and ONE payment.captured outbox row.
/// </summary>
[Collection("orders-fixture")]
public sealed class WebhookDedupTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task IdempotentSelfTransitions_AreNoOps()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        // Seed an order in authorized state with provider txn id we can target.
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Authorized, paymentProviderId: "stub", paymentProviderTxnId: "txn-dedup-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var hook = scope.ServiceProvider.GetRequiredService<IOrderPaymentStateHook>();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // Fire 100 captured webhooks at the same order. First flips authorized→captured,
        // remaining 99 land on captured→captured (idempotent self per PaymentSm).
        for (var i = 0; i < 100; i++)
        {
            var result = await hook.AdvanceFromAttemptAsync(
                new OrderPaymentAdvanceRequest(
                    ProviderId: "stub",
                    ProviderTxnId: "txn-dedup-1",
                    MappedAttemptState: "captured",
                    ErrorCode: null,
                    ErrorMessage: null,
                    ProviderEventId: $"evt-{i}"),
                CancellationToken.None);
            result.MatchedOrder.Should().BeTrue();
            result.OrderId.Should().Be(order.Id);
            result.FinalPaymentState.Should().Be(PaymentSm.Captured);
        }

        // Exactly one transition row.
        var transitionCount = await ordersDb.StateTransitions.AsNoTracking()
            .CountAsync(t => t.OrderId == order.Id
                && t.Machine == BackendApi.Modules.Orders.Entities.OrderStateTransition.MachinePayment
                && t.FromState == PaymentSm.Authorized && t.ToState == PaymentSm.Captured);
        transitionCount.Should().Be(1);

        // Exactly one payment.captured outbox row.
        var capturedOutboxCount = await ordersDb.Outbox.AsNoTracking()
            .CountAsync(e => e.AggregateId == order.Id && e.EventType == "payment.captured");
        capturedOutboxCount.Should().Be(1);
    }

    [Fact]
    public async Task UnknownTxnId_DoesNotMatchOrThrow()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var hook = scope.ServiceProvider.GetRequiredService<IOrderPaymentStateHook>();

        var result = await hook.AdvanceFromAttemptAsync(
            new OrderPaymentAdvanceRequest("stub", "no-such-txn", "captured", null, null, "evt-orphan"),
            CancellationToken.None);
        result.MatchedOrder.Should().BeFalse();
    }
}
