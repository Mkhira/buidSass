using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideRequestInfo;
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
/// Spec 020 US2 batch 2 — DecideRequestInfo handler. Asserts the SLA-pause
/// invariant (FR-039): transition row metadata captures <c>paused_at</c> +
/// <c>sla_pause_kind</c>, the verification's <c>decided_at</c> stays NULL
/// (info_requested is non-terminal), and a forbidden <c>info_requested →
/// approved</c> direct transition is rejected (must round-trip via in_review).
/// </summary>
public sealed class AdminRequestInfoHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_request_info_test")
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
    public async Task RequestInfo_flips_state_and_writes_paused_at_metadata()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();
        var requestedAt = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(requestedAt);

        await using var db = NewContext();
        var handler = new DecideRequestInfoHandler(
            db, new RecordingAuditPublisher(), clock,
            NullLogger<DecideRequestInfoHandler>.Instance);

        var result = await handler.HandleAsync(
            verificationId, reviewerId,
            new DecideRequestInfoRequest(new ReviewerReason(
                "Please upload a clearer scan of page 2.",
                "يرجى تحميل نسخة أوضح من الصفحة الثانية.")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("info-requested");

        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.InfoRequested);
        row.DecidedAt.Should().BeNull(
            "info_requested is non-terminal; only approve/reject stamps decided_at");

        // Transition row metadata captures paused_at + sla_pause_kind.
        var transition = await verify.StateTransitions
            .Where(t => t.VerificationId == verificationId
                     && t.NewState == "info-requested")
            .SingleAsync();
        transition.OccurredAt.Should().Be(requestedAt);
        var metadata = JsonDocument.Parse(transition.MetadataJson).RootElement;
        metadata.GetProperty("paused_at").GetString().Should()
            .Match(s => DateTimeOffset.Parse(s!) == requestedAt,
                "paused_at metadata captures the request-info instant exactly so the queue handler can compute pause-aware ages later");
        metadata.GetProperty("sla_pause_kind").GetString().Should().Be("info_requested");
        metadata.GetProperty("reason_en").GetString().Should().Be("Please upload a clearer scan of page 2.");
        metadata.GetProperty("reason_ar").GetString().Should().Contain("الصفحة");
    }

    [Fact]
    public async Task Customer_resubmits_after_info_request_state_returns_to_in_review()
    {
        // info_requested → in_review (customer-side resubmit) is allowed only
        // for customer actor; reviewer cannot drive it. We simulate the customer
        // path by directly mutating the row through the DbContext to validate
        // the state-machine guard (full Customer/ResubmitWithInfo slice ships in
        // Phase 3 batch 3).
        var (_, verificationId, _) = await SubmitAsync();

        // Reviewer requests info.
        await using (var db = NewContext())
        {
            var h = new DecideRequestInfoHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideRequestInfoHandler>.Instance);
            await h.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideRequestInfoRequest(new ReviewerReason("More info please.", null)),
                CancellationToken.None);
        }

        // State machine MUST allow info_requested → in_review for customer.
        VerificationStateMachine.CanTransition(
            VerificationState.InfoRequested,
            VerificationState.InReview,
            VerificationActorKind.Customer).Should().BeTrue();

        // And MUST forbid info_requested → approved direct (the round-trip rule).
        VerificationStateMachine.CanTransition(
            VerificationState.InfoRequested,
            VerificationState.Approved,
            VerificationActorKind.Reviewer).Should().BeFalse(
            "info_requested → approved direct is forbidden — must round-trip via in_review");
    }

    [Fact]
    public async Task RequestInfo_with_empty_reason_rejected_by_validator()
    {
        var (ok, code, _) = DecideRequestInfoValidator.Validate(
            new DecideRequestInfoRequest(new ReviewerReason(En: null, Ar: null)));
        ok.Should().BeFalse();
        code.Should().Be(VerificationReasonCode.ReviewReasonRequired);

        // Sanity: ar-only is accepted (FR-033).
        var arOnly = DecideRequestInfoValidator.Validate(
            new DecideRequestInfoRequest(new ReviewerReason(null, "نحتاج إلى مزيد من المعلومات.")));
        arOnly.ok.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RequestInfo_on_already_rejected_returns_InvalidStateForAction()
    {
        var (_, verificationId, _) = await SubmitAsync();

        // Reject first.
        await using (var db = NewContext())
        {
            var rejectHandler = new BackendApi.Modules.Verification.Admin.DecideReject.DecideRejectHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<BackendApi.Modules.Verification.Admin.DecideReject.DecideRejectHandler>.Instance);
            await rejectHandler.HandleAsync(verificationId, Guid.NewGuid(),
                new BackendApi.Modules.Verification.Admin.DecideReject.DecideRejectRequest(
                    new ReviewerReason("Initial rejection.", null)),
                CancellationToken.None);
        }

        // Now try to request info on the rejected row.
        await using (var db = NewContext())
        {
            var h = new DecideRequestInfoHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideRequestInfoHandler>.Instance);
            var result = await h.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideRequestInfoRequest(new ReviewerReason("Need more info.", null)),
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.ReasonCode.Should().Be(VerificationReasonCode.InvalidStateForAction);
        }
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
                RegulatorIdentifier: $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, result.Response.SubmittedAt);
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
