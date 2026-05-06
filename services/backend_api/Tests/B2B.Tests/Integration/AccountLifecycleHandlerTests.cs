using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Hooks;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 task T142 — drives <see cref="AccountLifecycleHandler"/> against a real
/// Postgres (Testcontainers) and verifies the three event paths:
///
/// <list type="bullet">
///   <item><c>OnAccountLockedAsync</c> — voids non-terminal quotes; preserves memberships.</item>
///   <item><c>OnAccountDeletedAsync</c> — voids non-terminal quotes AND removes memberships.</item>
///   <item><c>OnMarketChangedAsync</c> — voids non-terminal quotes (cross-market scope).</item>
/// </list>
///
/// Cross-cutting invariants:
/// <list type="bullet">
///   <item>Already-terminal quotes (<c>accepted</c>, <c>rejected</c>, etc.) are left untouched.</item>
///   <item>Audit + <see cref="QuoteWithdrawn"/> domain events fire per voided quote.</item>
///   <item>Re-delivery of the same event is a no-op (idempotent).</item>
/// </list>
/// </summary>
public sealed class AccountLifecycleHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_account_lifecycle")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditPublisher _audit = new();
    private readonly RecordingDomainPublisher _domain = new();
    private B2BDbContext _db = default!;
    private AccountLifecycleHandler _handler = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        _db = new B2BDbContext(options);
        await _db.Database.MigrateAsync();
        _handler = new AccountLifecycleHandler(_db, _audit, _domain, _clock, NullLogger<AccountLifecycleHandler>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Locked_voids_every_non_terminal_quote_for_customer()
    {
        var customerId = Guid.NewGuid();
        var revisedId = await SeedQuoteAsync(customerId, "revised");
        var pendingId = await SeedQuoteAsync(customerId, "pending-approver");
        var acceptedId = await SeedQuoteAsync(customerId, "accepted", terminalAt: _clock.GetUtcNow().AddDays(-1));

        await _handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "manual", _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var rows = await verify.Quotes.OrderBy(q => q.State).ToListAsync();
        rows.Single(r => r.Id == revisedId).State.Should().Be("withdrawn");
        rows.Single(r => r.Id == pendingId).State.Should().Be("withdrawn");
        rows.Single(r => r.Id == acceptedId).State.Should().Be("accepted",
            "accepted is terminal; lifecycle MUST NOT touch it");

        rows.Single(r => r.Id == revisedId).TerminalReason.Should().Be("account_inactive");
        rows.Single(r => r.Id == pendingId).TerminalReason.Should().Be("account_inactive");

        _audit.Events.Should().HaveCount(2, "two non-terminal quotes were voided");
        _audit.Events.Should().OnlyContain(e => e.Reason == "account_inactive");
        _domain.Notifications.OfType<QuoteWithdrawn>()
            .Should().HaveCount(2)
            .And.OnlyContain(e => e.Reason == "account_inactive");
    }

    [Fact]
    public async Task Deleted_voids_quotes_and_removes_memberships()
    {
        var customerId = Guid.NewGuid();
        var companyId = await SeedCompanyAsync();
        await SeedMembershipAsync(companyId, customerId, "buyer");
        await SeedMembershipAsync(companyId, customerId, "approver");
        // A second user's membership stays untouched.
        await SeedMembershipAsync(companyId, Guid.NewGuid(), "companies.admin");

        await SeedQuoteAsync(customerId, "revised");

        await _handler.OnAccountDeletedAsync(
            new CustomerAccountDeleted(customerId, _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var quotes = await verify.Quotes.SingleAsync(q => q.CustomerId == customerId);
        quotes.State.Should().Be("withdrawn");
        quotes.TerminalReason.Should().Be("account_deleted");

        var remainingMemberships = await verify.CompanyMemberships
            .Where(m => m.CompanyId == companyId)
            .ToListAsync();
        remainingMemberships.Should().HaveCount(1, "only the OTHER user's membership survives");
        remainingMemberships[0].UserId.Should().NotBe(customerId);
    }

    [Fact]
    public async Task MarketChanged_voids_quotes_in_every_market_for_customer()
    {
        var customerId = Guid.NewGuid();
        await SeedQuoteAsync(customerId, "revised", marketCode: "ksa");
        await SeedQuoteAsync(customerId, "drafted", marketCode: "eg");

        await _handler.OnMarketChangedAsync(
            new CustomerMarketChanged(customerId, "ksa", "eg", Guid.NewGuid(), _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var rows = await verify.Quotes.Where(q => q.CustomerId == customerId).ToListAsync();
        rows.Should().OnlyContain(r => r.State == "withdrawn"
                                     && r.TerminalReason == "customer_market_changed");
    }

    [Fact]
    public async Task Idempotent_on_redelivery()
    {
        var customerId = Guid.NewGuid();
        await SeedQuoteAsync(customerId, "revised");

        await _handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "manual", _clock.GetUtcNow()),
            CancellationToken.None);
        await _handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "manual", _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var withdrawals = await verify.QuoteStateTransitions
            .Where(t => t.NewState == "withdrawn" && t.ActorKind == "system")
            .CountAsync();
        withdrawals.Should().Be(1, "the second delivery has nothing to void");
    }

    private B2BDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new B2BDbContext(options);
    }

    private async Task<Guid> SeedQuoteAsync(
        Guid customerId,
        string state,
        string marketCode = "ksa",
        DateTimeOffset? terminalAt = null)
    {
        var id = Guid.NewGuid();
        _db.Quotes.Add(new Quote
        {
            Id = id,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = marketCode,
            State = state,
            RequestedAt = _clock.GetUtcNow().AddDays(-1),
            ExpiresAt = _clock.GetUtcNow().AddDays(7),
            TerminalAt = terminalAt,
            TerminalReason = terminalAt is null ? null : state,
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
        });
        _db.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = id,
            MarketCode = marketCode,
            PriorState = "__none__",
            NewState = state,
            ActorKind = QuoteActorKind.Customer.ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = _clock.GetUtcNow().AddDays(-1),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return id;
    }

    private async Task<Guid> SeedCompanyAsync()
    {
        var id = Guid.NewGuid();
        _db.Companies.Add(new Company
        {
            Id = id,
            MarketCode = "ksa",
            NameJson = "{\"en\":\"Acme\",\"ar\":\"أكمي\"}",
            TaxId = "TAX-" + Guid.NewGuid().ToString("N")[..10],
            PrimaryAddressJson = "{}",
            BillingAddressJson = null,
            State = "active",
            ApproverRequired = false,
            PoRequired = false,
            UniquePoRequired = false,
            InvoiceBillingEligible = true,
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return id;
    }

    private async Task SeedMembershipAsync(Guid companyId, Guid userId, string role)
    {
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MarketCode = "ksa",
            UserId = userId,
            Role = role,
            JoinedAt = _clock.GetUtcNow().AddDays(-30),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public List<AuditEvent> Events { get; } = new();
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDomainPublisher : IPublisher
    {
        public List<INotification> Notifications { get; } = new();
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n) Notifications.Add(n);
            return Task.CompletedTask;
        }
    }
}
