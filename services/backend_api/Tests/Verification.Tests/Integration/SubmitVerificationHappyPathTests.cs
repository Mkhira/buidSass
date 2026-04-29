using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
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
/// Spec 020 US1 / Phase 3 batch 1. Exercises the SubmitVerification handler
/// end-to-end against real Testcontainers Postgres. Asserts:
/// <list type="bullet">
///   <item>row + initial state-transition land atomically with the lowercase
///         wire-format state value passing the CHECK constraint;</item>
///   <item>schema_version snapshot points at the active per-market schema row;</item>
///   <item>second submission while one is still non-terminal returns
///         <see cref="VerificationReasonCode.AlreadyPending"/>;</item>
///   <item>renewal path (with SupersedesId set) bypasses the no-other-non-
///         terminal guard.</item>
/// </list>
/// </summary>
public sealed class SubmitVerificationHappyPathTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_submit_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();
    private RecordingAuditPublisher _auditPublisher = null!;

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
            Db: null!,
            Services: provider,
            Size: DatasetSize.Small,
            Env: new TestHostEnv(),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private SubmitVerificationHandler NewHandler(VerificationDbContext db, FakeTimeProvider clock)
    {
        _auditPublisher = new RecordingAuditPublisher();
        return new SubmitVerificationHandler(
            db: db,
            eligibilityInvalidator: new EligibilityCacheInvalidator(),
            auditPublisher: _auditPublisher,
            clock: clock,
            logger: NullLogger<SubmitVerificationHandler>.Instance);
    }

    [Fact]
    public async Task First_submission_creates_verification_and_initial_transition_atomically()
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = NewHandler(db, clock);

        var result = await handler.HandleAsync(
            customerId,
            "ksa",
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: "SCFHS-1234567",
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.State.Should().Be("submitted");
        result.Response.MarketCode.Should().Be("ksa");
        result.Response.SchemaVersion.Should().Be(1);

        // Row landed.
        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == result.Response.Id);
        row.State.Should().Be(VerificationState.Submitted);
        row.CustomerId.Should().Be(customerId);
        row.MarketCode.Should().Be("ksa");
        row.SchemaVersion.Should().Be(1);
        row.SubmittedAt.Should().Be(clock.GetUtcNow());
        row.RegulatorIdentifier.Should().Be("SCFHS-1234567");

        // Initial transition row landed with the wire-format state values.
        var transition = await verify.StateTransitions
            .SingleAsync(t => t.VerificationId == result.Response.Id);
        transition.PriorState.Should().Be("__none__");
        transition.NewState.Should().Be("submitted");
        transition.ActorKind.Should().Be("customer");
        transition.ActorId.Should().Be(customerId);
        transition.Reason.Should().Be("customer_submission");

        // Audit event published.
        _auditPublisher.Captured.Should().ContainSingle();
        _auditPublisher.Captured[0].Action.Should().Be("verification.state_changed");
        _auditPublisher.Captured[0].EntityId.Should().Be(result.Response.Id);
        _auditPublisher.Captured[0].ActorId.Should().Be(customerId);
    }

    [Fact]
    public async Task Second_submission_while_open_returns_AlreadyPending()
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = NewHandler(db, clock);

        await handler.HandleAsync(customerId, "ksa", BasicRequest(), CancellationToken.None);

        await using var db2 = NewContext();
        var handler2 = NewHandler(db2, clock);
        var second = await handler2.HandleAsync(customerId, "ksa", BasicRequest(), CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.ReasonCode.Should().Be(VerificationReasonCode.AlreadyPending);
    }

    [Fact]
    public async Task Renewal_submission_with_approved_supersedes_id_bypasses_already_pending_guard()
    {
        // Submit + approve a first verification → second submission with
        // supersedes_id pointing at the approved row is a legitimate renewal
        // and bypasses the AlreadyPending guard per data-model §3.2.
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = NewHandler(db, clock);

        var first = await handler.HandleAsync(customerId, "ksa", BasicRequest(), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Approve the first verification so it becomes a legitimate prior approval.
        await using (var dbApprove = NewContext())
        {
            var approve = new BackendApi.Modules.Verification.Admin.DecideApprove.DecideApproveHandler(
                dbApprove, new BackendApi.Modules.Verification.Eligibility.EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(), clock,
                NullLogger<BackendApi.Modules.Verification.Admin.DecideApprove.DecideApproveHandler>.Instance);
            var approveResult = await approve.HandleAsync(
                first.Response!.Id, Guid.NewGuid(),
                new BackendApi.Modules.Verification.Admin.DecideApprove.DecideApproveRequest(
                    new BackendApi.Modules.Verification.Admin.DecideApprove.ReviewerReason("Verified.", null)),
                CancellationToken.None);
            approveResult.IsSuccess.Should().BeTrue();
        }

        await using var db2 = NewContext();
        var handler2 = NewHandler(db2, clock);
        var renewal = await handler2.HandleAsync(
            customerId,
            "ksa",
            BasicRequest(supersedesId: first.Response!.Id),
            CancellationToken.None);

        renewal.IsSuccess.Should().BeTrue(
            "renewals MUST be allowed alongside an active approved prior verification per data-model §3.2");
        renewal.Response!.SupersedesId.Should().Be(first.Response.Id);
    }

    [Fact]
    public async Task Renewal_submission_with_unapproved_supersedes_id_returns_RenewalNotEligible()
    {
        // Per Fix #5 (CodeRabbit review #1) — supersedes_id MUST point at a
        // currently-approved row owned by the same customer in the same market.
        // A non-approved prior (submitted/in-review/rejected) is not a legitimate
        // renewal target.
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = NewHandler(db, clock);

        var first = await handler.HandleAsync(customerId, "ksa", BasicRequest(), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        await using var db2 = NewContext();
        var handler2 = NewHandler(db2, clock);
        var renewal = await handler2.HandleAsync(
            customerId,
            "ksa",
            BasicRequest(supersedesId: first.Response!.Id),
            CancellationToken.None);

        renewal.IsSuccess.Should().BeFalse();
        renewal.ReasonCode.Should().Be(VerificationReasonCode.RenewalNotEligible);
    }

    [Fact]
    public async Task Renewal_submission_with_foreign_customer_supersedes_id_returns_RenewalNotEligible()
    {
        // Customer A submits + gets approved; Customer B tries to submit a
        // "renewal" pointing at A's approval. MUST be rejected to prevent
        // cross-customer chain forgery (security boundary, CodeRabbit Fix #5).
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        Guid customerAVerificationId;
        await using (var db = NewContext())
        {
            var handler = NewHandler(db, clock);
            var first = await handler.HandleAsync(customerA, "ksa", BasicRequest(), CancellationToken.None);
            first.IsSuccess.Should().BeTrue();
            customerAVerificationId = first.Response!.Id;
        }
        await using (var dbApprove = NewContext())
        {
            var approve = new BackendApi.Modules.Verification.Admin.DecideApprove.DecideApproveHandler(
                dbApprove, new BackendApi.Modules.Verification.Eligibility.EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(), clock,
                NullLogger<BackendApi.Modules.Verification.Admin.DecideApprove.DecideApproveHandler>.Instance);
            await approve.HandleAsync(customerAVerificationId, Guid.NewGuid(),
                new BackendApi.Modules.Verification.Admin.DecideApprove.DecideApproveRequest(
                    new BackendApi.Modules.Verification.Admin.DecideApprove.ReviewerReason("Verified.", null)),
                CancellationToken.None);
        }

        await using var dbB = NewContext();
        var handlerB = NewHandler(dbB, clock);
        var forgedRenewal = await handlerB.HandleAsync(
            customerB,
            "ksa",
            BasicRequest(supersedesId: customerAVerificationId),
            CancellationToken.None);

        forgedRenewal.IsSuccess.Should().BeFalse();
        forgedRenewal.ReasonCode.Should().Be(VerificationReasonCode.RenewalNotEligible);
    }

    [Fact]
    public async Task Submission_for_unsupported_market_returns_MarketUnsupported()
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = NewHandler(db, clock);

        // The CHECK constraint on market_code IN ('eg','ksa') would reject 'fr'
        // at INSERT time anyway, but the handler short-circuits earlier when no
        // active schema is found.
        var result = await handler.HandleAsync(
            customerId,
            "fr",
            BasicRequest(),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.MarketUnsupported);
    }

    private static SubmitVerificationRequest BasicRequest(Guid? supersedesId = null) =>
        new(
            Profession: "dentist",
            RegulatorIdentifier: "SCFHS-1234567",
            DocumentIds: Array.Empty<Guid>(),
            SupersedesId: supersedesId);

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        private readonly List<AuditEvent> _captured = new();
        public IReadOnlyList<AuditEvent> Captured => _captured;
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            _captured.Add(auditEvent);
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
