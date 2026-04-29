using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Storage;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideRequestInfo;
using BackendApi.Modules.Verification.Customer.AttachDocument;
using BackendApi.Modules.Verification.Customer.RequestRenewal;
using BackendApi.Modules.Verification.Customer.ResubmitWithInfo;
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
/// Spec 020 Phase 3 batch 3 — final two US1 slices: ResubmitWithInfo (T058)
/// and RequestRenewal (T059). Both depend on states reachable only via the
/// reviewer surface shipped in Phase 4 / US2.
/// </summary>
public sealed class ResubmitAndRenewalTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_resubmit_renew_test")
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

    // ────────── ResubmitWithInfo (T058) ──────────

    [Fact]
    public async Task Resubmit_flips_info_requested_to_in_review_preserving_submitted_at()
    {
        var (customerId, verificationId, originalSubmittedAt) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        // Reviewer requests info.
        var infoRequestedAt = new DateTimeOffset(2026, 5, 5, 14, 0, 0, TimeSpan.Zero);
        await using (var db = NewContext())
        {
            var reqInfo = new DecideRequestInfoHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(infoRequestedAt),
                NullLogger<DecideRequestInfoHandler>.Instance);
            await reqInfo.HandleAsync(verificationId, reviewerId,
                new DecideRequestInfoRequest(new ReviewerReason("Need clearer scan.", null)),
                CancellationToken.None);
        }

        // Customer attaches a new document AFTER the info-request transition.
        var attachAt = infoRequestedAt.AddDays(1);
        await using (var db = NewContext())
        {
            var attach = new AttachDocumentHandler(
                db,
                new FakeVirusScanService(ScanResult.Clean),
                new FakeStorageService(),
                new FakeTimeProvider(attachAt),
                NullLogger<AttachDocumentHandler>.Instance);
            var attachResult = await attach.HandleAsync(
                customerId, verificationId,
                new AttachDocumentRequest(
                    "verifications/clearer-scan.pdf", "application/pdf", 1024 * 100),
                CancellationToken.None);
            attachResult.IsSuccess.Should().BeTrue();
        }

        // Customer resubmits.
        var resubmitAt = infoRequestedAt.AddDays(2);
        await using var db2 = NewContext();
        var handler = new ResubmitWithInfoHandler(
            db2, new RecordingAuditPublisher(),
            new FakeTimeProvider(resubmitAt),
            NullLogger<ResubmitWithInfoHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, verificationId,
            new ResubmitWithInfoRequest("I have uploaded a clearer scan as requested."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("in-review");
        result.Response.SubmittedAt.Should().Be(originalSubmittedAt,
            "FR-016 — original submitted_at MUST be preserved through the round trip");
        result.Response.ResubmittedAt.Should().Be(resubmitAt);

        // Row + transition confirmed.
        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.InReview);
        row.SubmittedAt.Should().Be(originalSubmittedAt);

        var transitions = await verify.StateTransitions
            .Where(t => t.VerificationId == verificationId)
            .OrderBy(t => t.OccurredAt)
            .ToListAsync();
        transitions.Should().HaveCount(3);  // submitted, info-requested, in-review
        transitions[2].PriorState.Should().Be("info-requested");
        transitions[2].NewState.Should().Be("in-review");
        transitions[2].ActorKind.Should().Be("customer");
        transitions[2].Reason.Should().Be("customer_resubmit_with_info");
    }

    [Fact]
    public async Task Resubmit_without_new_documents_returns_no_changes_provided()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        await using (var db = NewContext())
        {
            var reqInfo = new DecideRequestInfoHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 5, 14, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideRequestInfoHandler>.Instance);
            await reqInfo.HandleAsync(verificationId, reviewerId,
                new DecideRequestInfoRequest(new ReviewerReason("Need more info.", null)),
                CancellationToken.None);
        }

        // Customer tries to resubmit without attaching new documents.
        await using var db2 = NewContext();
        var handler = new ResubmitWithInfoHandler(
            db2, new RecordingAuditPublisher(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<ResubmitWithInfoHandler>.Instance);
        var result = await handler.HandleAsync(
            customerId, verificationId,
            new ResubmitWithInfoRequest("I have nothing new to add."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.RequiredFieldMissing);
        result.Detail.Should().Contain("no_changes_provided");
    }

    [Fact]
    public async Task Resubmit_on_non_info_requested_state_returns_InvalidStateForAction()
    {
        // Try to resubmit a row that's still in `submitted` (no info-request yet).
        var (customerId, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new ResubmitWithInfoHandler(
            db, new RecordingAuditPublisher(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<ResubmitWithInfoHandler>.Instance);
        var result = await handler.HandleAsync(
            customerId, verificationId,
            new ResubmitWithInfoRequest("Acknowledged."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.InvalidStateForAction);
    }

    [Fact]
    public async Task Resubmit_on_foreign_id_returns_NotFound()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var foreignCustomer = Guid.NewGuid();

        await using var db = NewContext();
        var handler = new ResubmitWithInfoHandler(
            db, new RecordingAuditPublisher(),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<ResubmitWithInfoHandler>.Instance);
        var result = await handler.HandleAsync(
            foreignCustomer, verificationId,
            new ResubmitWithInfoRequest("Acknowledged."),
            CancellationToken.None);

        result.IsNotFound.Should().BeTrue();
    }

    // ────────── RequestRenewal (T059) ──────────

    [Fact]
    public async Task Renewal_outside_window_returns_RenewalNotEligible()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        await ApproveAsync(verificationId);

        // Approval at 2026-05-01 + 365d = 2027-05-01 expiry. 30d window opens 2027-04-01.
        // Snapshot at 2026-09-01 is way outside the window.
        var snapshot = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        await using var db = NewContext();
        var handler = new RequestRenewalHandler(
            db, new RecordingAuditPublisher(),
            new FakeTimeProvider(snapshot),
            NullLogger<RequestRenewalHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, new RequestRenewalRequest(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.RenewalNotEligible);
        result.Detail.Should().Contain("renewal_window_not_open");
    }

    [Fact]
    public async Task Renewal_inside_window_creates_renewal_with_supersedes_id_set()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        await ApproveAsync(verificationId);

        // Approval expires 2027-05-01. 30d window opens 2027-04-01. Snapshot 2027-04-15.
        var snapshot = new DateTimeOffset(2027, 4, 15, 9, 0, 0, TimeSpan.Zero);
        await using var db = NewContext();
        var handler = new RequestRenewalHandler(
            db, new RecordingAuditPublisher(),
            new FakeTimeProvider(snapshot),
            NullLogger<RequestRenewalHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, new RequestRenewalRequest(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.SupersedesId.Should().Be(verificationId);
        result.Response.State.Should().Be("submitted");
        result.Response.SubmittedAt.Should().Be(snapshot);

        // Prior approval STAYS approved (FR-010).
        await using var verify = NewContext();
        var prior = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        prior.State.Should().Be(VerificationState.Approved,
            "renewal request MUST NOT alter the prior approval — only renewal *approval* triggers supersession");
    }

    [Fact]
    public async Task Renewal_carries_profession_and_regulator_id_forward_when_omitted()
    {
        var (customerId, verificationId, _) = await SubmitAsync(
            profession: "dentist",
            regulatorIdentifier: "SCFHS-9999999");
        await ApproveAsync(verificationId);

        var snapshot = new DateTimeOffset(2027, 4, 15, 9, 0, 0, TimeSpan.Zero);
        await using var db = NewContext();
        var handler = new RequestRenewalHandler(
            db, new RecordingAuditPublisher(),
            new FakeTimeProvider(snapshot),
            NullLogger<RequestRenewalHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, new RequestRenewalRequest(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verify = NewContext();
        var renewal = await verify.Verifications.SingleAsync(v => v.Id == result.Response!.Id);
        renewal.Profession.Should().Be("dentist");
        renewal.RegulatorIdentifier.Should().Be("SCFHS-9999999");
    }

    [Fact]
    public async Task Renewal_already_pending_returns_RenewalAlreadyPending()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        await ApproveAsync(verificationId);

        var snapshot = new DateTimeOffset(2027, 4, 15, 9, 0, 0, TimeSpan.Zero);

        await using (var db = NewContext())
        {
            var handler = new RequestRenewalHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(snapshot),
                NullLogger<RequestRenewalHandler>.Instance);
            var first = await handler.HandleAsync(
                customerId, new RequestRenewalRequest(null, null), CancellationToken.None);
            first.IsSuccess.Should().BeTrue();
        }

        await using (var db = NewContext())
        {
            var handler = new RequestRenewalHandler(
                db, new RecordingAuditPublisher(),
                new FakeTimeProvider(snapshot.AddDays(1)),
                NullLogger<RequestRenewalHandler>.Instance);
            var second = await handler.HandleAsync(
                customerId, new RequestRenewalRequest(null, null), CancellationToken.None);

            second.IsSuccess.Should().BeFalse();
            second.ReasonCode.Should().Be(VerificationReasonCode.RenewalAlreadyPending);
        }
    }

    [Fact]
    public async Task Renewal_with_no_active_approval_returns_RenewalNotEligible()
    {
        var customerId = Guid.NewGuid();

        await using var db = NewContext();
        var handler = new RequestRenewalHandler(
            db, new RecordingAuditPublisher(),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<RequestRenewalHandler>.Instance);
        var result = await handler.HandleAsync(
            customerId, new RequestRenewalRequest(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.RenewalNotEligible);
        result.Detail.Should().Contain("no_active_approval");
    }

    // ────────── helpers ──────────

    private async Task<(Guid CustomerId, Guid VerificationId, DateTimeOffset SubmittedAt)> SubmitAsync(
        string profession = "dentist",
        string? regulatorIdentifier = null)
    {
        var customerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(submittedAt);

        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest(
                Profession: profession,
                RegulatorIdentifier: regulatorIdentifier
                    ?? $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, submittedAt);
    }

    private async Task ApproveAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var approve = new DecideApproveHandler(
            db, new EligibilityCacheInvalidator(),
            new RecordingAuditPublisher(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideApproveHandler>.Instance);
        var result = await approve.HandleAsync(verificationId, Guid.NewGuid(),
            new DecideApproveRequest(new ReviewerReason("Verified.", null)),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private sealed class FakeVirusScanService(ScanResult result) : IVirusScanService
    {
        public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class FakeStorageService : IStorageService
    {
        public Task<StoredFileResult> UploadAsync(Stream content, string fileName, string mimeType, MarketCode market, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<Uri> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken cancellationToken)
            => Task.FromResult(new Uri($"https://storage.test/{fileId}"));
        public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
            => Task.CompletedTask;
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
