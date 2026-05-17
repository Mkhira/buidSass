using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Features.CreatePayment;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Providers.HyperPay;
using BackendApi.Modules.Payments.Providers.Paymob;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>AC-9 — V-2 idempotent create returns the original Payment.</summary>
public sealed class CreatePaymentIdempotencyTests
{
    private static PaymentsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid():N}")
            .Options;
        return new PaymentsDbContext(options);
    }

    private static CreatePaymentHandler NewHandler(PaymentsDbContext db)
    {
        var providers = new ProviderRegistry(new IPaymentProvider[]
        {
            new HyperPayProvider(TimeProvider.System),
            new PaymobProvider(TimeProvider.System),
        });
        var validator = new CreatePaymentValidator();
        return new CreatePaymentHandler(db, providers, validator, TimeProvider.System);
    }

    private static void SeedRouting(PaymentsDbContext db, string market, string method, string provider)
    {
        db.ProviderRoutings.Add(new ProviderRouting
        {
            MarketCode = market,
            Method = method,
            PrimaryProviderId = provider,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task DuplicateAttempt_ReturnsOriginalPayment()
    {
        var db = NewDb();
        SeedRouting(db, "sa", "mada", "hyperpay");
        var handler = NewHandler(db);
        var attemptId = Guid.NewGuid();
        var cmd = new CreatePaymentCommand(
            OrderId: Guid.NewGuid(), CustomerId: Guid.NewGuid(),
            MarketCode: "sa", Method: "mada",
            Amount: 250m, Currency: "SAR",
            AttemptId: attemptId);

        var first = await handler.Handle(cmd, CancellationToken.None);
        var second = await handler.Handle(cmd, CancellationToken.None);

        first.IdempotentReplay.Should().BeFalse();
        second.IdempotentReplay.Should().BeTrue();
        second.PaymentId.Should().Be(first.PaymentId);
        db.Payments.Count().Should().Be(1, "duplicate request must not insert a second row");
    }

    [Fact]
    public async Task DifferentAttemptId_CreatesNewPayment()
    {
        var db = NewDb();
        SeedRouting(db, "sa", "mada", "hyperpay");
        var handler = NewHandler(db);
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var first = await handler.Handle(new CreatePaymentCommand(orderId, customerId, "sa", "mada", 250m, "SAR", Guid.NewGuid()), CancellationToken.None);
        var second = await handler.Handle(new CreatePaymentCommand(orderId, customerId, "sa", "mada", 250m, "SAR", Guid.NewGuid()), CancellationToken.None);
        first.PaymentId.Should().NotBe(second.PaymentId);
        db.Payments.Count().Should().Be(2);
    }

    [Fact]
    public async Task CardPayment_InitialState_IsPendingAuthorization()
    {
        var db = NewDb();
        SeedRouting(db, "sa", "mada", "hyperpay");
        var handler = NewHandler(db);
        var result = await handler.Handle(new CreatePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), "sa", "mada", 100m, "SAR", Guid.NewGuid()), CancellationToken.None);
        result.State.Should().Be(PaymentsConstants.PaymentStates.PendingAuthorization);
        result.HostedFieldsConfigJson.Should().NotBeNull();
    }

    [Fact]
    public async Task BankTransfer_AllocatesUniqueReference()
    {
        var db = NewDb();
        var handler = NewHandler(db);
        var result = await handler.Handle(new CreatePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), "sa", "bank_transfer", 100m, "SAR", Guid.NewGuid()), CancellationToken.None);
        result.State.Should().Be(PaymentsConstants.PaymentStates.PendingBankTransfer);
        result.BankTransferReference.Should().NotBeNullOrWhiteSpace();
        result.BankTransferReference.Should().StartWith("SA-");
    }

    [Fact]
    public async Task Cod_InitialState_IsPendingCollectionOnDelivery()
    {
        var db = NewDb();
        var handler = NewHandler(db);
        var result = await handler.Handle(new CreatePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), "eg", "cod", 250m, "EGP", Guid.NewGuid()), CancellationToken.None);
        result.State.Should().Be(PaymentsConstants.PaymentStates.PendingCollectionOnDelivery);
        result.ProviderId.Should().BeNull("COD has no provider");
        db.CodCollectionLogs.Count().Should().Be(1);
    }

    [Fact]
    public async Task UnknownMethodMarketCombo_FailsValidation()
    {
        var db = NewDb();
        var handler = NewHandler(db);
        Func<Task> act = () => handler.Handle(new CreatePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), "eg", "mada", 100m, "EGP", Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
