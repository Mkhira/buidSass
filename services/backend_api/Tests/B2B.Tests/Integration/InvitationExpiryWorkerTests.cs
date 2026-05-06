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
/// Spec 021 task T139 — drives <see cref="InvitationExpiryWorker"/> on a real Postgres
/// (Testcontainers) and verifies:
///
/// <list type="bullet">
///   <item>Pending invitations past <c>expires_at</c> transition to <c>expired</c>.</item>
///   <item>Audit + <see cref="CompanyInvitationExpired"/> domain event are published.</item>
///   <item>Re-running the worker on the same data is a no-op (idempotent).</item>
///   <item>Pending invitations with future <c>expires_at</c> are untouched.</item>
///   <item>Already-terminal invitations are untouched.</item>
///   <item>Advisory lock prevents double-execution by a peer instance.</item>
/// </list>
/// </summary>
public sealed class InvitationExpiryWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_invitation_expiry_worker")
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
        services.AddSingleton<InvitationExpiryWorker>();
        _sp = services.BuildServiceProvider();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
        await db.Database.MigrateAsync();
        await SeedCompanyAsync(db);
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private Guid _companyId;

    [Fact]
    public async Task Expires_pending_invitation_past_expires_at()
    {
        var nowUtc = _clock.GetUtcNow();
        Guid invitationId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            invitationId = await SeedInvitationAsync(db, state: "pending", expiresAt: nowUtc.AddHours(-1));
        }

        var worker = _sp.GetRequiredService<InvitationExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(1);

        await using var verifyScope = _sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var inv = await verify.CompanyInvitations.SingleAsync(i => i.Id == invitationId);
        inv.State.Should().Be("expired");

        _audit.Events.Should().ContainSingle(e =>
            e.Action == "company_invitation.state_changed"
            && e.EntityId == invitationId
            && e.Reason == "invitation_expired");

        _domain.Notifications.OfType<CompanyInvitationExpired>()
            .Should().ContainSingle(e => e.InvitationId == invitationId);
    }

    [Fact]
    public async Task Skips_terminal_invitations()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedInvitationAsync(db, state: "accepted", expiresAt: nowUtc.AddHours(-1));
            await SeedInvitationAsync(db, state: "declined", expiresAt: nowUtc.AddHours(-1));
            await SeedInvitationAsync(db, state: "expired", expiresAt: nowUtc.AddHours(-1));
        }

        var worker = _sp.GetRequiredService<InvitationExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(0);
    }

    [Fact]
    public async Task Skips_pending_invitations_with_future_expiry()
    {
        var nowUtc = _clock.GetUtcNow();
        Guid invitationId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            invitationId = await SeedInvitationAsync(db, state: "pending", expiresAt: nowUtc.AddDays(7));
        }

        var worker = _sp.GetRequiredService<InvitationExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(0);

        await using var verifyScope = _sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var inv = await verify.CompanyInvitations.SingleAsync(i => i.Id == invitationId);
        inv.State.Should().Be("pending");
    }

    [Fact]
    public async Task Idempotent_when_run_twice()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedInvitationAsync(db, state: "pending", expiresAt: nowUtc.AddSeconds(-1));
        }

        var worker = _sp.GetRequiredService<InvitationExpiryWorker>();
        var first = await worker.RunPassAsync(CancellationToken.None);
        var second = await worker.RunPassAsync(CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(0);
    }

    [Fact]
    public async Task Advisory_lock_prevents_concurrent_pass()
    {
        var nowUtc = _clock.GetUtcNow();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            await SeedInvitationAsync(db, state: "pending", expiresAt: nowUtc.AddSeconds(-1));
        }

        // Pre-acquire the advisory lock on a parallel connection; the worker pass
        // MUST detect the contention and no-op cleanly without throwing.
        await using var contentionScope = _sp.CreateAsyncScope();
        var contentionDb = contentionScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        await using var heldLock = await PostgresAdvisoryLock.TryAcquireAsync(
            contentionDb,
            PostgresAdvisoryLock.Keys.InvitationExpiryWorker,
            CancellationToken.None);
        heldLock.Acquired.Should().BeTrue();

        var worker = _sp.GetRequiredService<InvitationExpiryWorker>();
        var count = await worker.RunPassAsync(CancellationToken.None);

        count.Should().Be(0, "lock contention MUST cause a clean no-op pass");
    }

    private async Task SeedCompanyAsync(B2BDbContext db)
    {
        _companyId = Guid.NewGuid();
        db.Companies.Add(new Company
        {
            Id = _companyId,
            MarketCode = "ksa",
            NameJson = "{\"en\":\"Test Company\",\"ar\":\"شركة اختبار\"}",
            TaxId = "TAX-" + Guid.NewGuid().ToString("N")[..10],
            PrimaryAddressJson = "{}",
            BillingAddressJson = null,
            State = "active",
            ApproverRequired = false,
            PoRequired = false,
            UniquePoRequired = false,
            InvoiceBillingEligible = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedInvitationAsync(
        B2BDbContext db,
        string state,
        DateTimeOffset expiresAt)
    {
        var id = Guid.NewGuid();
        db.CompanyInvitations.Add(new CompanyInvitation
        {
            Id = id,
            CompanyId = _companyId,
            MarketCode = "ksa",
            InvitedBy = Guid.NewGuid(),
            InvitedEmail = $"invitee-{id:N}@example.test",
            TargetRole = "buyer",
            // Token-hash is unique-indexed; vary per-row.
            TokenHash = "test-hash-" + id.ToString("N"),
            State = state,
            SentAt = expiresAt.AddDays(-14),
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return id;
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
