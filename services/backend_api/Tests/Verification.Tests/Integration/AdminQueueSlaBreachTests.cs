using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideRequestInfo;
using BackendApi.Modules.Verification.Admin.ListVerificationQueue;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Seeding;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 US2 batch 2 — T069b. Asserts the queue's SLA signal honors:
/// <list type="bullet">
///   <item>FR-039 — submitted &gt; <c>sla_decision_business_days</c> ago AND not paused → "breach";</item>
///   <item>submitted &gt; <c>sla_warning_business_days</c> ago AND not paused → "warning";</item>
///   <item>submitted recently AND not paused → "ok";</item>
///   <item><c>info_requested</c> state pauses the timer regardless of total elapsed time.</item>
/// </list>
/// </summary>
public sealed class AdminQueueSlaBreachTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_sla_breach_test")
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

    [Fact]
    public async Task Queue_returns_breach_for_3_business_day_old_submission()
    {
        // KSA seed: sla_decision_business_days = 2; warning = 1; weekend Fri+Sat.
        // Submitted Mon 2026-04-27, queue snapshot Thu 2026-04-30 — 3 business
        // days elapsed (Mon → Tue → Wed → Thu, count from-cursor-then-advance).
        var submitTime = new DateTimeOffset(2026, 4, 27, 9, 0, 0, TimeSpan.Zero);
        var snapshotTime = new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero);
        await SubmitAsync(submittedAt: submitTime);

        await using var db = NewContext();
        var clock = new FakeTimeProvider(snapshotTime);
        var queueHandler = new ListVerificationQueueHandler(db, clock);

        var result = await queueHandler.HandleAsync(
            new HashSet<string> { "ksa" },
            EmptyQuery(),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].SlaSignal.Should().Be("breach",
            "3 business days >= 2 (sla_decision_business_days) → breach");
    }

    [Fact]
    public async Task Queue_returns_warning_when_age_at_or_above_warning_threshold()
    {
        // Submit on Tuesday 2026-04-28 09:00; snapshot on Wed 2026-04-29 09:00 →
        // exactly 1 business day elapsed = at the warning threshold (1).
        var submitTime = new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
        var snapshotTime = new DateTimeOffset(2026, 4, 29, 9, 0, 0, TimeSpan.Zero);
        await SubmitAsync(submittedAt: submitTime);

        await using var db = NewContext();
        var clock = new FakeTimeProvider(snapshotTime);
        var queueHandler = new ListVerificationQueueHandler(db, clock);

        var result = await queueHandler.HandleAsync(
            new HashSet<string> { "ksa" },
            EmptyQuery(),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].SlaSignal.Should().Be("warning",
            "age=1 matches warning threshold but is below decision threshold");
    }

    [Fact]
    public async Task Queue_returns_ok_for_recent_submission()
    {
        var submitTime = new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
        var snapshotTime = new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
        await SubmitAsync(submittedAt: submitTime);

        await using var db = NewContext();
        var queueHandler = new ListVerificationQueueHandler(db, new FakeTimeProvider(snapshotTime));

        var result = await queueHandler.HandleAsync(
            new HashSet<string> { "ksa" },
            EmptyQuery(),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].SlaSignal.Should().Be("ok",
            "less than a business day elapsed → ok");
    }

    [Fact]
    public async Task Queue_returns_ok_for_info_requested_row_regardless_of_age()
    {
        // Customer submitted 5 business days ago, then reviewer requested-info
        // 4 business days ago. The customer hasn't resubmitted, so the row sits
        // in info_requested. SLA timer is paused → signal is "ok" no matter how
        // much time elapsed.
        var submitTime = new DateTimeOffset(2026, 4, 19, 9, 0, 0, TimeSpan.Zero); // Sun
        var requestInfoTime = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero); // Mon
        var snapshotTime = new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero); // 11 days later

        var (_, verificationId, _) = await SubmitAsync(submittedAt: submitTime);

        await using (var db = NewContext())
        {
            var requestInfo = new DecideRequestInfoHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(requestInfoTime),
                NullLogger<DecideRequestInfoHandler>.Instance);
            await requestInfo.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideRequestInfoRequest(new ReviewerReason("Need clearer scan.", null)),
                CancellationToken.None);
        }

        await using var db2 = NewContext();
        var queueHandler = new ListVerificationQueueHandler(db2, new FakeTimeProvider(snapshotTime));
        var result = await queueHandler.HandleAsync(
            new HashSet<string> { "ksa" },
            EmptyQuery(),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].State.Should().Be("info-requested");
        result.Items[0].SlaSignal.Should().Be("ok",
            "info_requested pauses the timer per FR-039 — 11 calendar days elapsed but signal stays ok");
    }

    private static ListVerificationQueueQuery EmptyQuery() =>
        new(MarketFilter: null, StateFilter: null, ProfessionFilter: null,
            AgeMinBusinessDays: null, Search: null, Sort: "oldest",
            Page: 1, PageSize: 25);

    private async Task<(Guid CustomerId, Guid VerificationId, DateTimeOffset SubmittedAt)> SubmitAsync(
        DateTimeOffset submittedAt)
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(submittedAt);

        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, result.Response.SubmittedAt);
    }

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
            => Task.CompletedTask;
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
