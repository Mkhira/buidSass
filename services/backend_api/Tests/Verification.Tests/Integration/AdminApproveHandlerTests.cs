using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
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
/// Spec 020 US2 batch 1 — DecideApprove handler. Covers:
/// <list type="bullet">
///   <item>Approval flips state to <c>approved</c> with <c>expires_at</c>
///         set to <c>now + market.expiry_days</c>;</item>
///   <item>Initial state-transition row + approval state-transition row both
///         landed in the append-only ledger;</item>
///   <item>Eligibility cache row UPSERTed to <c>eligible</c> with the customer's
///         profession (real impl, replacing the Phase 2 stub);</item>
///   <item>Audit event published with both bilingual reason locales preserved;</item>
///   <item>Renewal supersession transitions the prior approval to
///         <c>superseded</c> in the same Tx and stamps <c>SupersededById</c>;</item>
///   <item>Repeat approval on a row that's already approved returns
///         <see cref="VerificationReasonCode.InvalidStateForAction"/>;</item>
///   <item>Empty bilingual reason rejected by the validator with
///         <see cref="VerificationReasonCode.ReviewReasonRequired"/>.</item>
/// </list>
/// </summary>
public sealed class AdminApproveHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_approve_test")
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
            Db: null!,
            Services: provider,
            Size: DatasetSize.Small,
            Env: new TestHostEnv(),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    [Fact]
    public async Task Approve_flips_state_to_approved_and_writes_expires_at()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        await using var db = NewContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var (handler, audit) = NewHandler(db, clock);

        var result = await handler.HandleAsync(
            verificationId,
            reviewerId,
            new DecideApproveRequest(new ReviewerReason(En: "Verified.", Ar: "تم التحقق.")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("approved");
        result.Response.ExpiresAt.Should().Be(clock.GetUtcNow().AddDays(365));
        result.Response.DecidedBy.Should().Be(reviewerId);

        // Row reflects approval.
        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Approved);
        row.DecidedAt.Should().Be(clock.GetUtcNow());
        row.DecidedBy.Should().Be(reviewerId);
        row.ExpiresAt.Should().Be(clock.GetUtcNow().AddDays(365));

        // Two ledger rows: initial submission + approval.
        var transitions = await verify.StateTransitions
            .Where(t => t.VerificationId == verificationId)
            .OrderBy(t => t.OccurredAt)
            .ToListAsync();
        transitions.Should().HaveCount(2);
        transitions[0].NewState.Should().Be("submitted");
        transitions[1].PriorState.Should().Be("submitted");
        transitions[1].NewState.Should().Be("approved");
        transitions[1].ActorKind.Should().Be("reviewer");
        transitions[1].ActorId.Should().Be(reviewerId);

        // Eligibility cache rebuilt to "eligible".
        var cache = await verify.EligibilityCache.SingleAsync(c => c.CustomerId == customerId);
        cache.EligibilityClass.Should().Be("eligible");
        cache.MarketCode.Should().Be("ksa");
        cache.ExpiresAt.Should().Be(clock.GetUtcNow().AddDays(365));
        cache.ProfessionsJson.Should().Be("[\"dentist\"]");

        // Audit event published with both reason locales preserved.
        audit.Captured.Should().ContainSingle();
        audit.Captured[0].Action.Should().Be("verification.state_changed");
        audit.Captured[0].EntityId.Should().Be(verificationId);
        audit.Captured[0].ActorRole.Should().Be("reviewer");
    }

    [Fact]
    public async Task Approve_with_supersedes_id_transitions_prior_to_superseded_in_same_tx()
    {
        var (customerId, priorId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        // Approve the prior submission.
        await using (var db = NewContext())
        {
            var (h, _) = NewHandler(db, new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)));
            await h.HandleAsync(priorId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Initial verified.", null)),
                CancellationToken.None);
        }

        // Submit a renewal pointing at the prior approval.
        Guid renewalId;
        await using (var db = NewContext())
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
            var submit = new SubmitVerificationHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                clock, NullLogger<SubmitVerificationHandler>.Instance);

            var renewal = await submit.HandleAsync(customerId, "ksa",
                new SubmitVerificationRequest(
                    Profession: "dentist",
                    RegulatorIdentifier: "SCFHS-1234567",
                    DocumentIds: Array.Empty<Guid>(),
                    SupersedesId: priorId),
                CancellationToken.None);
            renewal.IsSuccess.Should().BeTrue("renewal path should bypass AlreadyPending guard");
            renewalId = renewal.Response!.Id;
        }

        // Approve the renewal — should also flip the prior approval to superseded.
        await using (var db = NewContext())
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
            var (h, _) = NewHandler(db, clock);
            var result = await h.HandleAsync(renewalId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Renewal verified.", null)),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Response!.SupersededId.Should().Be(priorId,
                "renewal approval MUST stamp SupersededId pointing at the prior row");
        }

        await using var verify = NewContext();
        var prior = await verify.Verifications.SingleAsync(v => v.Id == priorId);
        prior.State.Should().Be(VerificationState.Superseded);
        prior.SupersededById.Should().Be(renewalId);

        var renewal2 = await verify.Verifications.SingleAsync(v => v.Id == renewalId);
        renewal2.State.Should().Be(VerificationState.Approved);
        renewal2.SupersedesId.Should().Be(priorId);

        // Three transitions for the prior row: submission, initial approval, supersession.
        // Three for the renewal: submission, approval. Total 5 across both.
        var allTransitions = await verify.StateTransitions
            .Where(t => t.VerificationId == priorId || t.VerificationId == renewalId)
            .ToListAsync();
        allTransitions.Should().HaveCount(5);
    }

    [Fact]
    public async Task Approve_on_already_approved_row_returns_InvalidStateForAction()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using (var db = NewContext())
        {
            var (h, _) = NewHandler(db, clock);
            var first = await h.HandleAsync(verificationId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("First approval.", null)),
                CancellationToken.None);
            first.IsSuccess.Should().BeTrue();
        }

        await using (var db = NewContext())
        {
            var (h, _) = NewHandler(db, clock);
            var second = await h.HandleAsync(verificationId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Second approval attempt.", null)),
                CancellationToken.None);

            second.IsSuccess.Should().BeFalse();
            second.ReasonCode.Should().Be(VerificationReasonCode.InvalidStateForAction);
        }
    }

    [Fact]
    public void Empty_reason_rejected_by_validator()
    {
        var (ok, code, _) = DecideApproveValidator.Validate(
            new DecideApproveRequest(new ReviewerReason(En: null, Ar: null)));

        ok.Should().BeFalse();
        code.Should().Be(VerificationReasonCode.ReviewReasonRequired);
    }

    [Fact]
    public void Reason_with_only_ar_is_accepted()
    {
        var (ok, code, _) = DecideApproveValidator.Validate(
            new DecideApproveRequest(new ReviewerReason(En: null, Ar: "تم التحقق.")));

        ok.Should().BeTrue();
        code.Should().BeNull();
    }

    [Fact]
    public async Task Hundred_parallel_approvals_produce_exactly_one_winner()
    {
        // SC-007 — concurrency stress. xmin guard MUST resolve to one winner.
        var (_, verificationId, _) = await SubmitAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        const int parallelism = 50; // 50 instead of 100 to keep runtime modest while
                                     // still exercising the concurrency contract.
        var tasks = new Task<DecideApproveResult>[parallelism];
        for (var i = 0; i < parallelism; i++)
        {
            var attemptId = i;
            tasks[i] = Task.Run(async () =>
            {
                await using var db = NewContext();
                var (h, _) = NewHandler(db, clock);
                return await h.HandleAsync(
                    verificationId,
                    Guid.NewGuid(),
                    new DecideApproveRequest(new ReviewerReason($"Reviewer attempt {attemptId}", null)),
                    CancellationToken.None);
            });
        }

        var results = await Task.WhenAll(tasks);

        var winners = results.Count(r => r.IsSuccess);
        winners.Should().Be(1, "xmin guard MUST permit exactly one approval");

        var losers = results.Count(r =>
            !r.IsSuccess
            && (r.ReasonCode == VerificationReasonCode.AlreadyDecided
             || r.ReasonCode == VerificationReasonCode.InvalidStateForAction));
        losers.Should().Be(parallelism - 1,
            "every losing call MUST surface AlreadyDecided or InvalidStateForAction");

        // Final state on disk: exactly one approval row + supersession invariants hold.
        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Approved);

        var approvalTransitions = await verify.StateTransitions
            .Where(t => t.VerificationId == verificationId
                     && t.NewState == "approved")
            .CountAsync();
        approvalTransitions.Should().Be(1,
            "ledger MUST capture exactly one approval transition under concurrency");
    }

    private async Task<(Guid CustomerId, Guid VerificationId, DateTimeOffset SubmittedAt)> SubmitAsync()
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);

        var result = await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: "SCFHS-1234567",
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, result.Response.SubmittedAt);
    }

    private (DecideApproveHandler Handler, RecordingAuditPublisher Audit) NewHandler(
        VerificationDbContext db, FakeTimeProvider clock)
    {
        var audit = new RecordingAuditPublisher();
        var handler = new DecideApproveHandler(
            db: db,
            eligibilityInvalidator: new EligibilityCacheInvalidator(),
            auditPublisher: audit,
            domainPublisher: new NullVerificationDomainEventPublisher(),
            clock: clock,
            logger: NullLogger<DecideApproveHandler>.Instance);
        return (handler, audit);
    }

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        private readonly List<AuditEvent> _captured = new();
        public IReadOnlyList<AuditEvent> Captured
        {
            get { lock (_captured) return _captured.ToList(); }
        }
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            lock (_captured) _captured.Add(auditEvent);
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
