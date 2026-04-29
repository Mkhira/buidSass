using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using BackendApi.Modules.Verification.Seeding;
using BackendApi.Modules.Verification.Workers;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 task T089. Verifies VerificationExpiryWorker:
/// <list type="bullet">
///   <item>transitions approved rows whose expires_at &lt;= now to expired,</item>
///   <item>publishes audit + VerificationExpired domain event,</item>
///   <item>rebuilds the eligibility cache to ineligible/expired,</item>
///   <item>stamps purge_after on the verification's documents (T096),</item>
///   <item>is idempotent — re-running a second tick is a no-op.</item>
/// </list>
/// </summary>
public sealed class ExpiryWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_expiry_worker_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
        await SeedSchemasAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private VerificationDbContext NewContext() => new(
        new DbContextOptionsBuilder<VerificationDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact]
    public async Task Worker_expires_approved_rows_past_expires_at()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await ApproveCustomerAsync(customerId);

        // Snapshot is past the 365-day expiry (approved 2026-05-01 + 365d = 2027-05-01).
        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, audit, domain) = BuildWorker(snapshot);

        var expired = await worker.RunPassAsync(CancellationToken.None);

        expired.Should().Be(1);
        await using (var db = NewContext())
        {
            var row = await db.Verifications.SingleAsync(v => v.Id == verificationId);
            row.State.Should().Be(VerificationState.Expired);
            row.UpdatedAt.Should().Be(snapshot);
        }

        audit.Events.Should().Contain(e =>
            e.Action == "verification.state_changed"
            && e.EntityId == verificationId
            && e.Reason == "verification_expired");
        domain.Events.OfType<VerificationDomainEvents.VerificationExpired>()
            .Should().Contain(e => e.VerificationId == verificationId);
    }

    [Fact]
    public async Task Worker_rebuilds_eligibility_cache_to_ineligible_expired()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId);

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, _) = BuildWorker(snapshot);
        await worker.RunPassAsync(CancellationToken.None);

        await using var db = NewContext();
        var cache = await db.EligibilityCache
            .AsNoTracking()
            .SingleAsync(c => c.CustomerId == customerId && c.MarketCode == "ksa");
        cache.EligibilityClass.Should().Be("ineligible");
        cache.ReasonCode.Should().Be("VerificationExpired");
    }

    [Fact]
    public async Task Worker_is_idempotent_on_second_tick()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await ApproveCustomerAsync(customerId);

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, audit, domain) = BuildWorker(snapshot);

        var firstPass = await worker.RunPassAsync(CancellationToken.None);
        var secondPass = await worker.RunPassAsync(CancellationToken.None);

        firstPass.Should().Be(1);
        secondPass.Should().Be(0, "the row was already expired in the first pass — second tick must no-op");

        await using var db = NewContext();
        var transitions = await db.StateTransitions
            .AsNoTracking()
            .Where(t => t.VerificationId == verificationId && t.NewState == "expired")
            .CountAsync();
        transitions.Should().Be(1, "the append-only ledger must record exactly one expired transition per verification");
    }

    [Fact]
    public async Task Worker_does_not_expire_rows_whose_expires_at_is_in_future()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId);

        // Snapshot is well before expiry (approved 2026-05-01, expires 2027-05-01).
        var snapshot = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, _) = BuildWorker(snapshot);

        var expired = await worker.RunPassAsync(CancellationToken.None);
        expired.Should().Be(0);
    }

    [Fact]
    public async Task Worker_stamps_purge_after_on_documents()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await ApproveCustomerAsync(customerId);
        await using (var db = NewContext())
        {
            db.Documents.Add(new BackendApi.Modules.Verification.Entities.VerificationDocument
            {
                Id = Guid.NewGuid(),
                VerificationId = verificationId,
                MarketCode = "ksa",
                StorageKey = "test/key/1",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                ScanStatus = "clean",
                UploadedAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
        }

        var snapshot = new DateTimeOffset(2027, 5, 2, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, _) = BuildWorker(snapshot);
        await worker.RunPassAsync(CancellationToken.None);

        await using var ctx = NewContext();
        var doc = await ctx.Documents.AsNoTracking().SingleAsync(d => d.VerificationId == verificationId);
        doc.PurgeAfter.Should().NotBeNull();
        doc.PurgeAfter!.Value.Should().Be(snapshot.AddMonths(24),
            "ksa retention is 24 months — stamped at the expiry instant");
    }

    // ────────────────────────── helpers ──────────────────────────

    private (VerificationExpiryWorker worker, RecordingAuditPublisher audit, RecordingDomainPublisher domain) BuildWorker(DateTimeOffset snapshot)
    {
        var clock = new FakeTimeProvider(snapshot);
        var audit = new RecordingAuditPublisher();
        var domain = new RecordingDomainPublisher();
        var services = new ServiceCollection();
        services.AddDbContext<VerificationDbContext>(o => o.UseNpgsql(ConnectionString),
            ServiceLifetime.Scoped);
        services.AddScoped<EligibilityCacheInvalidator>();
        services.AddSingleton<IAuditEventPublisher>(audit);
        services.AddSingleton<IVerificationDomainEventPublisher>(domain);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new VerificationWorkerOptions());
        var worker = new VerificationExpiryWorker(scopeFactory, options, clock,
            NullLogger<VerificationExpiryWorker>.Instance);
        return (worker, audit, domain);
    }

    private async Task<Guid> ApproveCustomerAsync(Guid customerId)
    {
        Guid verificationId;
        await using (var db = NewContext())
        {
            var submit = new SubmitVerificationHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero)),
                NullLogger<SubmitVerificationHandler>.Instance);
            var result = await submit.HandleAsync(customerId, "ksa",
                new SubmitVerificationRequest("dentist", "SCFHS-1234567",
                    Array.Empty<Guid>(), null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            verificationId = result.Response!.Id;
        }
        await using (var db = NewContext())
        {
            var approve = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideApproveHandler>.Instance);
            var result = await approve.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }
        return verificationId;
    }

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<VerificationDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new VerificationReferenceDataSeeder();
        var ctx = new SeedContext(
            Db: null!, Services: provider, Size: DatasetSize.Small,
            Env: new TestHostEnv(), Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    internal sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public List<AuditEvent> Events { get; } = new();
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingDomainPublisher : IVerificationDomainEventPublisher
    {
        public List<object> Events { get; } = new();
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : class
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Verification.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
