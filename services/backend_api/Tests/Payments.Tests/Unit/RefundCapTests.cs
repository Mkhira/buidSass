using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Features.Refund;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>
/// AC-22 / AC-23 — V-5 partial-refund-sum constraint enforced at the app
/// layer. (The DB trigger is the second line of defense; the in-memory test
/// here exercises the app-layer pre-check.)
/// </summary>
public sealed class RefundCapTests
{
    private sealed class NoopAuditPublisher : IAuditEventPublisher
    {
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static PaymentsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid():N}")
            .Options;
        return new PaymentsDbContext(options);
    }

    private static Payment SeedCapturedPayment(PaymentsDbContext db, decimal amount)
    {
        var p = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            MarketCode = "sa",
            Method = "mada",
            ProviderId = "hyperpay",
            ProviderMessageId = "HP-1",
            State = PaymentsConstants.PaymentStates.Captured,
            Amount = amount,
            Currency = "SAR",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AttemptId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CapturedAt = DateTimeOffset.UtcNow,
        };
        db.Payments.Add(p);
        db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task PartialRefund_LeavesPaymentInPartiallyRefunded()
    {
        var db = NewDb();
        var providers = new ProviderRegistry(new IPaymentProvider[] { new BackendApi.Modules.Payments.Providers.HyperPay.HyperPayProvider(TimeProvider.System) });
        var transitions = new PaymentTransitionService(new NoopAuditPublisher(), TimeProvider.System);
        var handler = new RefundHandler(db, providers, transitions, TimeProvider.System);
        var payment = SeedCapturedPayment(db, 100m);

        var result = await handler.Handle(new RefundCommand(payment.Id, 30m, "partial", Guid.NewGuid()), CancellationToken.None);

        result.State.Should().Be(PaymentsConstants.RefundStates.Completed);
        result.ParentPaymentState.Should().Be(PaymentsConstants.PaymentStates.PartiallyRefunded);
    }

    [Fact]
    public async Task FullRefund_TransitionsPaymentToRefunded()
    {
        var db = NewDb();
        var providers = new ProviderRegistry(new IPaymentProvider[] { new BackendApi.Modules.Payments.Providers.HyperPay.HyperPayProvider(TimeProvider.System) });
        var transitions = new PaymentTransitionService(new NoopAuditPublisher(), TimeProvider.System);
        var handler = new RefundHandler(db, providers, transitions, TimeProvider.System);
        var payment = SeedCapturedPayment(db, 100m);

        var result = await handler.Handle(new RefundCommand(payment.Id, 100m, "full", Guid.NewGuid()), CancellationToken.None);

        result.State.Should().Be(PaymentsConstants.RefundStates.Completed);
        result.ParentPaymentState.Should().Be(PaymentsConstants.PaymentStates.Refunded);
    }

    [Fact]
    public async Task OverRefund_Rejected()
    {
        var db = NewDb();
        var providers = new ProviderRegistry(new IPaymentProvider[] { new BackendApi.Modules.Payments.Providers.HyperPay.HyperPayProvider(TimeProvider.System) });
        var transitions = new PaymentTransitionService(new NoopAuditPublisher(), TimeProvider.System);
        var handler = new RefundHandler(db, providers, transitions, TimeProvider.System);
        var payment = SeedCapturedPayment(db, 100m);

        await handler.Handle(new RefundCommand(payment.Id, 60m, "first partial", Guid.NewGuid()), CancellationToken.None);

        // Try to refund another 50 — total would be 110 > 100 captured.
        Func<Task> act = () => handler.Handle(new RefundCommand(payment.Id, 50m, "over", Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<RefundExceedsCapturedAmountException>();
    }

    [Fact]
    public async Task SumOfPartials_AtCapturedExactly_ClosesToRefunded()
    {
        var db = NewDb();
        var providers = new ProviderRegistry(new IPaymentProvider[] { new BackendApi.Modules.Payments.Providers.HyperPay.HyperPayProvider(TimeProvider.System) });
        var transitions = new PaymentTransitionService(new NoopAuditPublisher(), TimeProvider.System);
        var handler = new RefundHandler(db, providers, transitions, TimeProvider.System);
        var payment = SeedCapturedPayment(db, 100m);

        await handler.Handle(new RefundCommand(payment.Id, 30m, "p1", Guid.NewGuid()), CancellationToken.None);
        var r2 = await handler.Handle(new RefundCommand(payment.Id, 70m, "p2", Guid.NewGuid()), CancellationToken.None);

        r2.ParentPaymentState.Should().Be(PaymentsConstants.PaymentStates.Refunded);
    }
}
