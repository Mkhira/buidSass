using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Providers.HyperPay;
using BackendApi.Modules.Payments.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>AC-29 / AC-30 / AC-31 — exception classification by ReconciliationMatcher.</summary>
public sealed class ReconciliationMatcherTests
{
    private sealed class NoopAuditPublisher : IAuditEventPublisher
    {
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static PaymentsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase($"recon-{Guid.NewGuid():N}").Options);

    private static Payment SeedCaptured(PaymentsDbContext db, string providerId, string providerMessageId, decimal amount, string currency = "SAR")
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var p = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            MarketCode = "sa",
            Method = "mada",
            ProviderId = providerId,
            ProviderMessageId = providerMessageId,
            State = PaymentsConstants.PaymentStates.Captured,
            Amount = amount,
            Currency = currency,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AttemptId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CapturedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(12),
        };
        db.Payments.Add(p);
        db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task MissingOnProvider_Classified()
    {
        var db = NewDb();
        var matcher = new ReconciliationMatcher(db, new NoopAuditPublisher(), TimeProvider.System);
        var provider = new HyperPayProvider(TimeProvider.System);
        SeedCaptured(db, provider.ProviderId, "HP-1", 100m);

        var emptyLedger = new SettlementLedger(provider.ProviderId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Array.Empty<SettlementLedgerRow>());
        var (matched, exceptions) = await matcher.MatchAsync(provider, emptyLedger, Guid.NewGuid(), CancellationToken.None);
        matched.Should().Be(0);
        exceptions.Should().Be(1);
        db.ReconciliationExceptions.Single().Reason.Should().Be(PaymentsConstants.ReconciliationExceptionReasons.MissingOnProvider);
    }

    [Fact]
    public async Task OrphanProviderRow_Classified()
    {
        var db = NewDb();
        var matcher = new ReconciliationMatcher(db, new NoopAuditPublisher(), TimeProvider.System);
        var provider = new HyperPayProvider(TimeProvider.System);
        var ledger = new SettlementLedger(provider.ProviderId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            new[] { new SettlementLedgerRow("HP-orphan", 50m, "SAR", DateTimeOffset.UtcNow, null) });
        var (matched, exceptions) = await matcher.MatchAsync(provider, ledger, Guid.NewGuid(), CancellationToken.None);
        matched.Should().Be(0);
        exceptions.Should().Be(1);
        db.ReconciliationExceptions.Single().Reason.Should().Be(PaymentsConstants.ReconciliationExceptionReasons.OrphanProviderRow);
    }

    [Fact]
    public async Task AmountMismatch_Classified()
    {
        var db = NewDb();
        var matcher = new ReconciliationMatcher(db, new NoopAuditPublisher(), TimeProvider.System);
        var provider = new HyperPayProvider(TimeProvider.System);
        SeedCaptured(db, provider.ProviderId, "HP-2", 100m);
        var ledger = new SettlementLedger(provider.ProviderId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            new[] { new SettlementLedgerRow("HP-2", 99.99m, "SAR", DateTimeOffset.UtcNow, null) });
        var (matched, exceptions) = await matcher.MatchAsync(provider, ledger, Guid.NewGuid(), CancellationToken.None);
        matched.Should().Be(0);
        exceptions.Should().Be(1);
        db.ReconciliationExceptions.Single().Reason.Should().Be(PaymentsConstants.ReconciliationExceptionReasons.AmountMismatch);
    }

    [Fact]
    public async Task CurrencyMismatch_Classified()
    {
        var db = NewDb();
        var matcher = new ReconciliationMatcher(db, new NoopAuditPublisher(), TimeProvider.System);
        var provider = new HyperPayProvider(TimeProvider.System);
        SeedCaptured(db, provider.ProviderId, "HP-3", 100m, "SAR");
        var ledger = new SettlementLedger(provider.ProviderId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            new[] { new SettlementLedgerRow("HP-3", 100m, "USD", DateTimeOffset.UtcNow, null) });
        var (matched, exceptions) = await matcher.MatchAsync(provider, ledger, Guid.NewGuid(), CancellationToken.None);
        matched.Should().Be(0);
        exceptions.Should().Be(1);
        db.ReconciliationExceptions.Single().Reason.Should().Be(PaymentsConstants.ReconciliationExceptionReasons.CurrencyMismatch);
    }

    [Fact]
    public async Task PerfectMatch_RaisesNoException()
    {
        var db = NewDb();
        var matcher = new ReconciliationMatcher(db, new NoopAuditPublisher(), TimeProvider.System);
        var provider = new HyperPayProvider(TimeProvider.System);
        SeedCaptured(db, provider.ProviderId, "HP-4", 200m);
        var ledger = new SettlementLedger(provider.ProviderId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            new[] { new SettlementLedgerRow("HP-4", 200m, "SAR", DateTimeOffset.UtcNow, null) });
        var (matched, exceptions) = await matcher.MatchAsync(provider, ledger, Guid.NewGuid(), CancellationToken.None);
        matched.Should().Be(1);
        exceptions.Should().Be(0);
    }
}
