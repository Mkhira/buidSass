using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Storage;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Customer.AttachDocument;
using BackendApi.Modules.Verification.Customer.GetMyActiveVerification;
using BackendApi.Modules.Verification.Customer.GetMyVerification;
using BackendApi.Modules.Verification.Customer.ListMyVerifications;
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
/// Spec 020 Phase 3 batch 2 — customer-side reads + AttachDocument.
/// </summary>
public sealed class CustomerReadAndAttachTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_customer_reads_test")
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
    public async Task GetMyActive_returns_null_for_customer_with_no_verifications()
    {
        var customerId = Guid.NewGuid();

        await using var db = NewContext();
        var handler = new GetMyActiveVerificationHandler(
            db, new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)));
        var result = await handler.HandleAsync(customerId, CancellationToken.None);

        result.Should().BeNull(
            "no rows in DB → 200 with null body, NOT a 404 (per contract §2.2)");
    }

    [Fact]
    public async Task GetMyActive_returns_submitted_with_wait_for_review_action()
    {
        var (customerId, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new GetMyActiveVerificationHandler(
            db, new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)));
        var result = await handler.HandleAsync(customerId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(verificationId);
        result.State.Should().Be("submitted");
        result.NextAction.Should().Be("wait_for_review");
        result.RenewalOpen.Should().BeFalse();
        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task GetMyActive_returns_approved_with_renewal_open_when_inside_earliest_reminder_window()
    {
        // Approve a row, then check the active read 31+ days before expiry → renewal_open=false.
        // Then advance to within the earliest window (30 days) → renewal_open=true.
        var (customerId, verificationId, _) = await SubmitAsync();

        await using (var db = NewContext())
        {
            var approve = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideApproveHandler>.Instance);
            await approve.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
        }

        // 360 days post-approval → 5 days before expiry → inside 30-day reminder window → renewal_open.
        var snapshot = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero).AddDays(360);
        await using var db2 = NewContext();
        var handler = new GetMyActiveVerificationHandler(
            db2, new FakeTimeProvider(snapshot));
        var result = await handler.HandleAsync(customerId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.State.Should().Be("approved");
        result.RenewalOpen.Should().BeTrue(
            "with expires_at = approval+365d and earliest reminder window = 30d, snapshot at +360d is inside the window");
        result.NextAction.Should().Be("renew");
        result.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMyActive_prefers_non_terminal_over_approved_when_both_exist()
    {
        // Customer with an approved verification submits a renewal — non-terminal
        // renewal MUST be returned as the "active" row, not the prior approval.
        var (customerId, priorId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        await using (var db = NewContext())
        {
            var approve = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideApproveHandler>.Instance);
            await approve.HandleAsync(priorId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
        }

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

        await using var db2 = NewContext();
        var handler = new GetMyActiveVerificationHandler(
            db2, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)));
        var result = await handler.HandleAsync(customerId, CancellationToken.None);

        result!.Id.Should().Be(renewalId,
            "non-terminal renewal MUST take precedence over the prior approval as the customer's 'active' row");
        result.State.Should().Be("submitted");
    }

    [Fact]
    public async Task ListMy_paginates_and_orders_newest_first()
    {
        var customerId = Guid.NewGuid();

        // Submit 3 rows. The fixture's no-other-non-terminal guard means we can't
        // submit 3 standalone — so submit 1, approve, submit a renewal, approve,
        // submit a 3rd renewal.
        var first = await SubmitForCustomerAsync(customerId, supersedesId: null,
            submittedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        await ApproveAsync(first);
        var second = await SubmitForCustomerAsync(customerId, supersedesId: first,
            submittedAt: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        await ApproveAsync(second);
        var third = await SubmitForCustomerAsync(customerId, supersedesId: second,
            submittedAt: new DateTimeOffset(2027, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = new ListMyVerificationsHandler(db);
        var result = await handler.HandleAsync(
            customerId,
            new ListMyVerificationsQuery(Page: 1, PageSize: 25),
            CancellationToken.None);

        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(3);
        result.Items[0].Id.Should().Be(third, "newest-first sort");
        result.Items[1].Id.Should().Be(second);
        result.Items[2].Id.Should().Be(first);
    }

    [Fact]
    public async Task ListMy_only_returns_rows_owned_by_the_customer()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();

        await SubmitForCustomerAsync(customerA, null, new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        await SubmitForCustomerAsync(customerB, null, new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = new ListMyVerificationsHandler(db);
        var result = await handler.HandleAsync(
            customerA,
            new ListMyVerificationsQuery(Page: 1, PageSize: 25),
            CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetMyVerification_returns_NotFound_for_foreign_id()
    {
        var (_, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new GetMyVerificationHandler(db);
        var foreignCustomer = Guid.NewGuid();
        var result = await handler.HandleAsync(foreignCustomer, verificationId, CancellationToken.None);

        result.Should().BeNull("a different customer's verification id MUST return NotFound (404), not Forbidden, to avoid leaking row existence");
    }

    [Fact]
    public async Task GetMyVerification_includes_transitions_and_documents()
    {
        var (customerId, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new GetMyVerificationHandler(db);
        var result = await handler.HandleAsync(customerId, verificationId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Transitions.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Transitions[0].PriorState.Should().Be("__none__");
        result.Transitions[0].NewState.Should().Be("submitted");
        result.Documents.Should().BeEmpty(
            "no documents attached yet for a fresh submission");
    }

    [Fact]
    public async Task AttachDocument_records_metadata_and_returns_201()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var handler = new AttachDocumentHandler(
            db,
            clock,
            NullLogger<AttachDocumentHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, verificationId,
            new AttachDocumentRequest(
                StorageKey: "verifications/test-doc.pdf",
                ContentType: "application/pdf",
                SizeBytes: 1024 * 100, // 100 KB
                ScanStatus: "clean"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.ContentType.Should().Be("application/pdf");
        result.Response.ScanStatus.Should().Be("clean");

        await using var verify = NewContext();
        var docs = await verify.Documents.Where(d => d.VerificationId == verificationId).ToListAsync();
        docs.Should().ContainSingle();
        docs[0].StorageKey.Should().Be("verifications/test-doc.pdf");
        docs[0].ScanStatus.Should().Be("clean");
    }

    [Fact]
    public async Task AttachDocument_rejects_disallowed_mime()
    {
        var (customerId, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new AttachDocumentHandler(
            db,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<AttachDocumentHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, verificationId,
            new AttachDocumentRequest("verifications/test.exe", "application/x-msdownload", 1024, "clean"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.DocumentMimeForbidden);
    }

    [Fact]
    public async Task AttachDocument_rejects_oversized_document()
    {
        var (customerId, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new AttachDocumentHandler(
            db,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<AttachDocumentHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, verificationId,
            new AttachDocumentRequest("verifications/big.pdf", "application/pdf", 11 * 1024 * 1024, "clean"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.DocumentSizeExceeded);
    }

    [Fact]
    public async Task AttachDocument_rejects_infected_scan()
    {
        var (customerId, verificationId, _) = await SubmitAsync();

        await using var db = NewContext();
        var handler = new AttachDocumentHandler(
            db,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<AttachDocumentHandler>.Instance);

        var result = await handler.HandleAsync(
            customerId, verificationId,
            new AttachDocumentRequest("verifications/malware.pdf", "application/pdf", 1024, "infected"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.DocumentScanInfected);

        // No row should have been persisted.
        await using var verify = NewContext();
        var docs = await verify.Documents.Where(d => d.VerificationId == verificationId).ToListAsync();
        docs.Should().BeEmpty("infected scan rejects MUST NOT create a verification_documents row");
    }

    [Fact]
    public async Task AttachDocument_returns_NotFound_for_foreign_verification()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var foreignCustomer = Guid.NewGuid();

        await using var db = NewContext();
        var handler = new AttachDocumentHandler(
            db,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<AttachDocumentHandler>.Instance);

        var result = await handler.HandleAsync(
            foreignCustomer, verificationId,
            new AttachDocumentRequest("verifications/x.pdf", "application/pdf", 1024, "clean"),
            CancellationToken.None);

        result.IsNotFound.Should().BeTrue();
    }

    private async Task<(Guid CustomerId, Guid VerificationId, DateTimeOffset SubmittedAt)> SubmitAsync()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(
            customerId,
            supersedesId: null,
            submittedAt: new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        return (customerId, verificationId, new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
    }

    private async Task<Guid> SubmitForCustomerAsync(
        Guid customerId, Guid? supersedesId, DateTimeOffset submittedAt)
    {
        var clock = new FakeTimeProvider(submittedAt);
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant(),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: supersedesId),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Response!.Id;
    }

    private async Task ApproveAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var approve = new DecideApproveHandler(
            db, new EligibilityCacheInvalidator(),
            new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
            new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
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
