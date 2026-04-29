using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Admin.GetVerificationDetail;
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
/// Spec 020 US2 batch 1 — read-path handlers. Asserts the queue's market-scope
/// filter, default state filter, oldest-first sort, and SLA signal computation;
/// asserts the detail handler returns the schema snapshot + transition history
/// and 404s on a foreign-market id.
/// </summary>
public sealed class AdminQueueAndDetailHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_admin_read_test")
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

    private VerificationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<VerificationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new VerificationDbContext(options);
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

    [Fact]
    public async Task Queue_returns_only_rows_in_reviewer_assigned_markets()
    {
        await SubmitAsync(market: "ksa");
        await SubmitAsync(market: "ksa");
        await SubmitAsync(market: "eg");

        await using var db = NewContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var handler = new ListVerificationQueueHandler(db, clock);

        var ksaOnly = new HashSet<string> { "ksa" };
        var result = await handler.HandleAsync(
            ksaOnly,
            new ListVerificationQueueQuery(
                MarketFilter: null, StateFilter: null, ProfessionFilter: null,
                AgeMinBusinessDays: null, Search: null, Sort: "oldest",
                Page: 1, PageSize: 25),
            CancellationToken.None);

        result.TotalCount.Should().Be(2, "only KSA rows should be visible to a KSA-only reviewer");
        result.Items.Should().AllSatisfy(r => r.MarketCode.Should().Be("ksa"));
        result.Items.Should().BeInAscendingOrder(r => r.SubmittedAt,
            "default sort is oldest-first per contracts §3.1");
    }

    [Fact]
    public async Task Queue_default_state_filter_excludes_terminal_rows()
    {
        await SubmitAsync(market: "ksa");

        await using var db = NewContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var handler = new ListVerificationQueueHandler(db, clock);

        var result = await handler.HandleAsync(
            new HashSet<string> { "ksa" },
            new ListVerificationQueueQuery(
                MarketFilter: null, StateFilter: null, ProfessionFilter: null,
                AgeMinBusinessDays: null, Search: null, Sort: "oldest",
                Page: 1, PageSize: 25),
            CancellationToken.None);

        result.Items.Should().AllSatisfy(r => r.State.Should().BeOneOf("submitted", "in-review", "info-requested"),
            "the default queue filter excludes terminal states (approved counts as non-terminal but " +
            "is also excluded by default — reviewers focus on rows that need attention)");
    }

    [Fact]
    public async Task Queue_sla_signal_returns_breach_when_age_exceeds_decision_threshold()
    {
        // Submit at T-3 business days; KSA SLA decision = 2 business days → breach.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var oldSubmit = new DateTimeOffset(2026, 4, 27, 9, 0, 0, TimeSpan.Zero); // Monday
        await SubmitAsync(market: "ksa", submittedAt: oldSubmit);

        await using var db = NewContext();
        var handler = new ListVerificationQueueHandler(db, clock);

        var result = await handler.HandleAsync(
            new HashSet<string> { "ksa" },
            new ListVerificationQueueQuery(
                MarketFilter: null, StateFilter: null, ProfessionFilter: null,
                AgeMinBusinessDays: null, Search: null, Sort: "oldest",
                Page: 1, PageSize: 25),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].SlaSignal.Should().Be("breach",
            "3+ business days since submission with no decision MUST surface as breach (KSA decision SLA = 2 business days)");
        result.Items[0].AgeBusinessDays.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Detail_returns_schema_snapshot_and_transition_history()
    {
        var (_, verificationId, _) = await SubmitAsync(market: "ksa");

        await using var db = NewContext();
        var handler = new GetVerificationDetailHandler(db, new NullRegulatorAssistLookup());

        var result = await handler.HandleAsync(
            verificationId, new HashSet<string> { "ksa" }, CancellationToken.None);

        result.Exists.Should().BeTrue();
        var detail = result.Response!;
        detail.Id.Should().Be(verificationId);
        detail.MarketCode.Should().Be("ksa");
        detail.State.Should().Be("submitted");
        detail.SchemaSnapshot.MarketCode.Should().Be("ksa");
        detail.SchemaSnapshot.Version.Should().Be(1);
        detail.SchemaSnapshot.RetentionMonths.Should().Be(24);
        detail.SchemaSnapshot.SlaDecisionBusinessDays.Should().Be(2);
        detail.Transitions.Should().HaveCount(1, "an unapproved row has only the initial submission transition");
        detail.Transitions[0].PriorState.Should().Be("__none__");
        detail.Transitions[0].NewState.Should().Be("submitted");
        detail.RegulatorAssist.Should().BeNull(
            "V1 default IRegulatorAssistLookup returns null → field absent");
    }

    [Fact]
    public async Task Detail_returns_NotFound_for_foreign_market_row()
    {
        var (_, verificationId, _) = await SubmitAsync(market: "ksa");

        await using var db = NewContext();
        var handler = new GetVerificationDetailHandler(db, new NullRegulatorAssistLookup());

        var result = await handler.HandleAsync(
            verificationId, new HashSet<string> { "eg" }, CancellationToken.None);

        result.Exists.Should().BeFalse(
            "an EG-only reviewer MUST see 404 (not 403) for a KSA row — avoids leaking existence");
    }

    private async Task<(Guid CustomerId, Guid VerificationId, DateTimeOffset SubmittedAt)> SubmitAsync(
        string market = "ksa",
        DateTimeOffset? submittedAt = null)
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(
            submittedAt ?? new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(),
            new NoOpAuditPublisher(), clock,
            NullLogger<SubmitVerificationHandler>.Instance);

        var result = await submit.HandleAsync(
            customerId, market,
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"submission for market {market} should succeed");
        return (customerId, result.Response!.Id, result.Response.SubmittedAt);
    }

    private sealed class NoOpAuditPublisher : IAuditEventPublisher
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
