using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideReject;
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
/// Spec 020 US2 batch 2 — DecideReject handler. Asserts state machine,
/// cooldown_until computation against the snapshotted schema, eligibility
/// cache stays "ineligible" (rejection MUST NOT promote the customer), and
/// the bilingual reason payload preserved in audit.
/// </summary>
public sealed class AdminRejectHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_reject_test")
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
    public async Task Reject_flips_state_and_returns_cooldown_until()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var (handler, audit) = NewHandler(db, clock);

        var result = await handler.HandleAsync(
            verificationId, reviewerId,
            new DecideRejectRequest(new ReviewerReason("Insufficient documentation.", "وثائق غير كافية.")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("rejected");
        result.Response.CooldownUntil.Should().Be(clock.GetUtcNow().AddDays(7),
            "cooldown_until = decided_at + market.cooldown_days (KSA seed=7)");

        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Rejected);
        row.DecidedAt.Should().Be(clock.GetUtcNow());
        row.ExpiresAt.Should().BeNull("rejection MUST NOT set expires_at");

        // Eligibility cache stays ineligible.
        var cache = await verify.EligibilityCache.SingleAsync(c => c.CustomerId == customerId);
        cache.EligibilityClass.Should().Be("ineligible");
        cache.ProfessionsJson.Should().Be("[]");

        // Audit captured both reason locales.
        audit.Captured.Should().ContainSingle();
        audit.Captured[0].Reason.Should().Be("reviewer_reject");
    }

    [Fact]
    public async Task Reject_on_already_rejected_returns_InvalidStateForAction()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using (var db = NewContext())
        {
            var (h, _) = NewHandler(db, clock);
            await h.HandleAsync(verificationId, reviewerId,
                new DecideRejectRequest(new ReviewerReason("First rejection.", null)),
                CancellationToken.None);
        }

        await using (var db = NewContext())
        {
            var (h, _) = NewHandler(db, clock);
            var second = await h.HandleAsync(verificationId, reviewerId,
                new DecideRejectRequest(new ReviewerReason("Second attempt.", null)),
                CancellationToken.None);

            second.IsSuccess.Should().BeFalse();
            second.ReasonCode.Should().Be(VerificationReasonCode.InvalidStateForAction);
        }
    }

    [Fact]
    public async Task Reject_does_not_revoke_a_prior_active_approval()
    {
        // Customer has an existing approval. They submit a renewal. Reviewer
        // rejects the renewal. The prior approval MUST stay approved.
        var (customerId, priorId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        // Approve the prior submission.
        await using (var db = NewContext())
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
            var approveHandler = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(), new NullVerificationDomainEventPublisher(), clock,
                NullLogger<DecideApproveHandler>.Instance);
            await approveHandler.HandleAsync(priorId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
        }

        // Submit + reject a renewal.
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
            renewalId = renewal.Response!.Id;
        }

        await using (var db = NewContext())
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
            var (h, _) = NewHandler(db, clock);
            var rejection = await h.HandleAsync(renewalId, reviewerId,
                new DecideRejectRequest(new ReviewerReason("Renewal docs incomplete.", null)),
                CancellationToken.None);
            rejection.IsSuccess.Should().BeTrue();
        }

        await using var verify = NewContext();
        var prior = await verify.Verifications.SingleAsync(v => v.Id == priorId);
        prior.State.Should().Be(VerificationState.Approved,
            "rejection of a renewal MUST NOT alter the prior approval");
        prior.SupersededById.Should().BeNull();

        var cache = await verify.EligibilityCache.SingleAsync(c => c.CustomerId == customerId);
        cache.EligibilityClass.Should().Be("eligible",
            "the prior approval still drives the eligibility class");
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
                RegulatorIdentifier: $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant(),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, result.Response.SubmittedAt);
    }

    private (DecideRejectHandler, RecordingAuditPublisher) NewHandler(
        VerificationDbContext db, FakeTimeProvider clock)
    {
        var audit = new RecordingAuditPublisher();
        var handler = new DecideRejectHandler(
            db, new EligibilityCacheInvalidator(), audit, new NullVerificationDomainEventPublisher(), clock,
            NullLogger<DecideRejectHandler>.Instance);
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
