using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Workers;
using BackendApi.Modules.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 task T138 — drives <see cref="QuoteExpiryWorker"/> on a real Postgres
/// (Testcontainers) and verifies:
///
/// <list type="bullet">
///   <item>Non-terminal quotes past <c>expires_at</c> transition to <c>expired</c>.</item>
///   <item>State-transition ledger row is appended with the system actor.</item>
///   <item>Audit + <see cref="QuoteExpired"/> domain event are published.</item>
///   <item>Re-running the worker on the same data is a no-op (idempotent).</item>
///   <item>Quotes with future <c>expires_at</c> are untouched.</item>
///   <item>Already-terminal quotes are untouched.</item>
/// </list>
///
/// Drives time via <see cref="FakeTimeProvider"/>; never blocks on real wall clock.
/// </summary>
public sealed class QuoteExpiryWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_quote_expiry_worker")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _sp = default!;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditPublisher _audit = new();
    private readonly RecordingDomainPublisher _domain = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<B2BDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton<IAuditEventPublisher>(_audit);
        services.AddSingleton<IPublisher>(_domain);
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IOptions<B2BWorkerOptions>>(Options.Create(new B2BWorkerOptions()));
        services.AddLogging();
        services.AddSingleton<QuoteExpiryWorker>();
        _sp = services.BuildServiceProvider();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
        await db.Database.MigrateAsync();
        await SeedMarketSchemaAsync(db);
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Expires_revised_quote_past_expires_at()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedQuoteAsync(db, state: "revised", expiresAt: nowUtc.AddHours(-1));
        }

        var worker = _sp.GetRequiredService<QuoteExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(1);

        await using var verifyScope = _sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var quote = await verify.Quotes.SingleAsync();
        quote.State.Should().Be("expired");
        quote.TerminalAt.Should().Be(nowUtc);
        quote.TerminalReason.Should().Be("quote.expired");

        var transition = await verify.QuoteStateTransitions
            .Where(t => t.QuoteId == quote.Id)
            .OrderByDescending(t => t.OccurredAt)
            .FirstAsync();
        transition.PriorState.Should().Be("revised");
        transition.NewState.Should().Be("expired");
        transition.ActorKind.Should().Be("system");
        transition.ActorId.Should().BeNull();

        _audit.Events.Should().ContainSingle(e =>
            e.Action == "quote.state_changed"
            && e.EntityId == quote.Id
            && e.Reason == "quote.expired");

        _domain.Notifications.OfType<QuoteExpired>().Should().ContainSingle(e => e.QuoteId == quote.Id);
    }

    [Fact]
    public async Task Skips_terminal_quotes()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedQuoteAsync(db, state: "accepted", expiresAt: nowUtc.AddHours(-1), terminalAt: nowUtc.AddHours(-1));
            await SeedQuoteAsync(db, state: "withdrawn", expiresAt: nowUtc.AddHours(-1), terminalAt: nowUtc.AddHours(-1));
        }

        var worker = _sp.GetRequiredService<QuoteExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(0, "terminal quotes are excluded by the WHERE clause");

        await using var verifyScope = _sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var states = await verify.Quotes.OrderBy(q => q.State).Select(q => q.State).ToListAsync();
        states.Should().BeEquivalentTo(new[] { "accepted", "withdrawn" });
    }

    [Fact]
    public async Task Skips_quotes_with_future_expires_at()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedQuoteAsync(db, state: "revised", expiresAt: nowUtc.AddDays(7));
        }

        var worker = _sp.GetRequiredService<QuoteExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(0);

        await using var verifyScope = _sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var quote = await verify.Quotes.SingleAsync();
        quote.State.Should().Be("revised");
    }

    [Fact]
    public async Task Idempotent_when_run_twice()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedQuoteAsync(db, state: "pending-approver", expiresAt: nowUtc.AddSeconds(-1));
        }

        var worker = _sp.GetRequiredService<QuoteExpiryWorker>();
        var first = await worker.RunPassAsync(CancellationToken.None);
        var second = await worker.RunPassAsync(CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(0, "second pass MUST be a no-op");

        await using var verifyScope = _sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        // Count only system-driven expiry transitions; the seed inserts a separate
        // initial __none__ → state row with actor_kind='customer'.
        var expiries = await verify.QuoteStateTransitions
            .Where(t => t.NewState == "expired" && t.ActorKind == "system")
            .CountAsync();
        expiries.Should().Be(1, "exactly one expiry transition row was written");
    }

    private async Task SeedMarketSchemaAsync(B2BDbContext db)
    {
        if (await db.QuoteMarketSchemas.AnyAsync())
        {
            return;
        }
        var nowUtc = DateTimeOffset.UtcNow;
        db.QuoteMarketSchemas.AddRange(
            BuildSchema("ksa", nowUtc),
            BuildSchema("eg", nowUtc));
        await db.SaveChangesAsync();
    }

    private static QuoteMarketSchema BuildSchema(string market, DateTimeOffset effectiveFrom) => new()
    {
        MarketCode = market,
        Version = 1,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = null,
        ValidityDays = 14,
        RateLimitPerCustomerPerHour = 10,
        RateLimitPerCompanyPerHour = 50,
        CompanyVerificationRequired = false,
        TaxPreviewDriftThresholdPct = 5.00m,
        SlaDecisionBusinessDays = 2,
        SlaWarningBusinessDays = 1,
        InvitationTtlDays = 14,
        HolidaysListJson = "[]",
    };

    private async Task SeedQuoteAsync(
        B2BDbContext db,
        string state,
        DateTimeOffset expiresAt,
        DateTimeOffset? terminalAt = null)
    {
        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = state,
            RequestedAt = expiresAt.AddDays(-14),
            ExpiresAt = expiresAt,
            TerminalAt = terminalAt,
            TerminalReason = terminalAt is null ? null : state,
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
        };
        db.Quotes.Add(quote);
        db.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            MarketCode = "ksa",
            PriorState = "__none__",
            NewState = state,
            ActorKind = QuoteActorKind.Customer.ToToken(),
            ActorId = quote.CustomerId,
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = quote.RequestedAt,
        });
        await db.SaveChangesAsync();
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
