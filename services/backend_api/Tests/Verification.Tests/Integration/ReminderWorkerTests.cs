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
/// Spec 020 task T090. Verifies VerificationReminderWorker:
/// <list type="bullet">
///   <item>fires the closest unfired window when expiry is inside it,</item>
///   <item>fires each window at most once (UNIQUE constraint guard),</item>
///   <item>back-window outage: only the closest unfired fires; others are
///         recorded with skipped=true (R5).</item>
/// </list>
/// Default reminder windows are <c>[30, 14, 7, 1]</c> per the seeded schema.
/// </summary>
public sealed class ReminderWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_reminder_worker_test")
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
    public async Task Worker_fires_closest_unfired_window_inside_reminder_range()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await ApproveCustomerAsync(customerId);

        // 28 days before 2027-05-01 expiry → inside the 30-day window, before the 14-day one.
        // Closest unfired = 30.
        var snapshot = new DateTimeOffset(2027, 4, 3, 9, 0, 0, TimeSpan.Zero);
        var (worker, audit, domain) = BuildWorker(snapshot);

        var fired = await worker.RunPassAsync(CancellationToken.None);
        fired.Should().Be(1);

        await using var db = NewContext();
        var reminders = await db.Reminders.AsNoTracking()
            .Where(r => r.VerificationId == verificationId)
            .ToListAsync();
        reminders.Should().HaveCount(1);
        reminders[0].WindowDays.Should().Be(30);
        reminders[0].Skipped.Should().BeFalse();

        domain.Events.OfType<VerificationDomainEvents.VerificationReminderDue>()
            .Should().Contain(e => e.VerificationId == verificationId && e.WindowDays == 30);
        audit.Events.Should().Contain(e =>
            e.Action == "verification.reminder_emitted"
            && e.EntityId == verificationId);
    }

    [Fact]
    public async Task Worker_fires_each_window_only_once()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await ApproveCustomerAsync(customerId);

        var snapshot = new DateTimeOffset(2027, 4, 3, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, _) = BuildWorker(snapshot);

        var first = await worker.RunPassAsync(CancellationToken.None);
        var second = await worker.RunPassAsync(CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(0, "the 30-day window already fired in the first pass");

        await using var db = NewContext();
        var reminders = await db.Reminders.AsNoTracking()
            .Where(r => r.VerificationId == verificationId)
            .ToListAsync();
        reminders.Should().HaveCount(1);
    }

    [Fact]
    public async Task Worker_back_window_outage_fires_closest_and_skips_outer()
    {
        // Approval expires 2027-05-01. Multiple windows have already passed without
        // the worker running (30 + 14 + 7 + 1, all behind by 2 days). Snapshot 2027-04-30
        // → expires_at - now = 1 day, so windows 30, 14, 7, 1 are ALL eligible (≤ 1d
        // expiry); R5 says fire only the closest (1) and skip the rest.
        var customerId = Guid.NewGuid();
        var verificationId = await ApproveCustomerAsync(customerId);

        var snapshot = new DateTimeOffset(2027, 4, 30, 12, 0, 0, TimeSpan.Zero);
        var (worker, audit, _) = BuildWorker(snapshot);

        var fired = await worker.RunPassAsync(CancellationToken.None);
        fired.Should().Be(1, "exactly one fired (the closest unfired window)");

        await using var db = NewContext();
        var reminders = await db.Reminders.AsNoTracking()
            .Where(r => r.VerificationId == verificationId)
            .OrderBy(r => r.WindowDays)
            .ToListAsync();
        reminders.Should().HaveCount(4, "all four windows must be recorded — one fired + three skipped");
        reminders.Should().ContainSingle(r => !r.Skipped).Which.WindowDays.Should().Be(1);
        reminders.Where(r => r.Skipped).Select(r => r.WindowDays).Should().BeEquivalentTo(new[] { 7, 14, 30 });

        audit.Events.Where(e => e.Action == "verification.reminder_emitted" && e.Reason == "verification_reminder_skipped")
            .Should().HaveCount(3, "each skipped window writes its own audit row for ops triage (R5)");
    }

    [Fact]
    public async Task Worker_does_not_fire_for_already_expired_verifications()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId);

        // Snapshot is past expiry — ExpiryWorker would handle these; ReminderWorker no-ops.
        var snapshot = new DateTimeOffset(2027, 5, 5, 9, 0, 0, TimeSpan.Zero);
        var (worker, _, _) = BuildWorker(snapshot);

        var fired = await worker.RunPassAsync(CancellationToken.None);
        fired.Should().Be(0);
    }

    // ────────────────────────── helpers ──────────────────────────

    private (VerificationReminderWorker worker, ExpiryWorkerTests.RecordingAuditPublisher audit, ExpiryWorkerTests.RecordingDomainPublisher domain) BuildWorker(DateTimeOffset snapshot)
    {
        var clock = new FakeTimeProvider(snapshot);
        var audit = new ExpiryWorkerTests.RecordingAuditPublisher();
        var domain = new ExpiryWorkerTests.RecordingDomainPublisher();
        var services = new ServiceCollection();
        services.AddDbContext<VerificationDbContext>(o => o.UseNpgsql(ConnectionString),
            ServiceLifetime.Scoped);
        services.AddScoped<EligibilityCacheInvalidator>();
        services.AddSingleton<IAuditEventPublisher>(audit);
        services.AddSingleton<IVerificationDomainEventPublisher>(domain);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new VerificationWorkerOptions());
        var worker = new VerificationReminderWorker(scopeFactory, options, clock,
            NullLogger<VerificationReminderWorker>.Instance);
        return (worker, audit, domain);
    }

    private async Task<Guid> ApproveCustomerAsync(Guid customerId)
    {
        Guid verificationId;
        await using (var db = NewContext())
        {
            var submit = new SubmitVerificationHandler(
                db, new EligibilityCacheInvalidator(), new ExpiryWorkerTests.RecordingAuditPublisher(),
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
                db, new EligibilityCacheInvalidator(), new ExpiryWorkerTests.RecordingAuditPublisher(),
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

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Verification.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
