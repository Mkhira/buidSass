using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Storage;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideRevoke;
using BackendApi.Modules.Verification.Admin.OpenHistoricalDocument;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using BackendApi.Modules.Verification.Seeding;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 US2 batch 3 — DecideRevoke + OpenHistoricalDocument handlers.
/// Asserts:
/// <list type="bullet">
///   <item>Revoke flips Approved → Revoked, no cool-down (FR-009), eligibility
///         cache rebuilt to ineligible, audit captures the revocation reason;</item>
///   <item>Revoke on a non-approved row returns InvalidStateForAction;</item>
///   <item>OpenHistoricalDocument returns a signed URL + writes ONE PII audit
///         event for non-terminal/approved parents;</item>
///   <item>OpenHistoricalDocument writes TWO audit events for terminal parents
///         (body-read + historical_open per spec 020 contracts §3.7);</item>
///   <item>Purged document returns the Purged result with the timestamp.</item>
/// </list>
/// </summary>
public sealed class AdminRevokeAndOpenHistoricalDocTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_revoke_open_test")
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
    public async Task Revoke_flips_approved_to_revoked_and_rebuilds_cache_to_ineligible()
    {
        var (customerId, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        // Approve first.
        await using (var db = NewContext())
        {
            var approve = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(), new NullVerificationDomainEventPublisher(), clock,
                NullLogger<DecideApproveHandler>.Instance);
            await approve.HandleAsync(verificationId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
        }

        // Now revoke. Use a later instant to make the audit trail readable.
        var revokeAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        await using (var db = NewContext())
        {
            var audit = new RecordingAuditPublisher();
            var revoke = new DecideRevokeHandler(
                db, new EligibilityCacheInvalidator(), audit,
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(revokeAt),
                NullLogger<DecideRevokeHandler>.Instance);

            var result = await revoke.HandleAsync(verificationId, reviewerId,
                new DecideRevokeRequest(new ReviewerReason(
                    "License revoked by SCFHS notice 2026-06-01.",
                    "تم إلغاء الترخيص بإشعار الهيئة 2026-06-01.")),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Response!.State.Should().Be("revoked");
            result.Response.RevokedAt.Should().Be(revokeAt);
            audit.Captured.Should().ContainSingle();
            audit.Captured[0].Reason.Should().Be("reviewer_revoke");
        }

        await using var verify = NewContext();
        var row = await verify.Verifications.SingleAsync(v => v.Id == verificationId);
        row.State.Should().Be(VerificationState.Revoked);

        var cache = await verify.EligibilityCache.SingleAsync(c => c.CustomerId == customerId);
        cache.EligibilityClass.Should().Be("ineligible",
            "revoked is terminal — cache MUST flip back to ineligible");
        cache.ProfessionsJson.Should().Be("[]");

        var transitions = await verify.StateTransitions
            .Where(t => t.VerificationId == verificationId)
            .OrderBy(t => t.OccurredAt)
            .ToListAsync();
        transitions.Should().HaveCount(3, "submission + approval + revocation");
        transitions[2].PriorState.Should().Be("approved");
        transitions[2].NewState.Should().Be("revoked");
        transitions[2].ActorKind.Should().Be("reviewer");
    }

    [Fact]
    public async Task Revoke_on_non_approved_row_returns_InvalidStateForAction()
    {
        // Try to revoke a row in `submitted` state.
        var (_, verificationId, _) = await SubmitAsync();
        var reviewerId = Guid.NewGuid();

        await using var db = NewContext();
        var revoke = new DecideRevokeHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideRevokeHandler>.Instance);

        var result = await revoke.HandleAsync(verificationId, reviewerId,
            new DecideRevokeRequest(new ReviewerReason("Trying to revoke a fresh submission.", null)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.InvalidStateForAction);
    }

    [Fact]
    public async Task OpenHistoricalDocument_emits_one_audit_event_for_non_terminal_parent()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var documentId = await InsertDocumentAsync(verificationId, "verifications/doc-001.pdf");

        var audit = new RecordingAuditPublisher();
        var (handler, _) = NewOpenHandler(audit, isAdminPath: true);

        await using var db = NewContext();
        var bound = new OpenHistoricalDocumentHandler(db, FakeStorage.Instance, NewPiiRecorder(audit, isAdminPath: true));
        var result = await bound.HandleAsync(
            verificationId, documentId,
            new HashSet<string> { "ksa" },
            CancellationToken.None);

        result.Exists.Should().BeTrue();
        result.IsPurged.Should().BeFalse();
        result.Response.Should().NotBeNull();
        result.Response!.IsHistoricalOpen.Should().BeFalse(
            "submitted is non-terminal; historical_open flag should be false");

        audit.Captured.Should().ContainSingle(
            "non-terminal parent emits exactly one PII audit event (body read)");
        audit.Captured[0].Action.Should().Be("verification.pii_access");
    }

    [Fact]
    public async Task OpenHistoricalDocument_emits_two_audit_events_for_terminal_parent()
    {
        // Approve then revoke so the parent reaches a terminal state.
        var (_, verificationId, _) = await SubmitAsync();
        var documentId = await InsertDocumentAsync(verificationId, "verifications/doc-002.pdf");
        var reviewerId = Guid.NewGuid();

        await using (var db = NewContext())
        {
            var approve = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideApproveHandler>.Instance);
            await approve.HandleAsync(verificationId, reviewerId,
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
        }

        await using (var db = NewContext())
        {
            var revoke = new DecideRevokeHandler(
                db, new EligibilityCacheInvalidator(),
                new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideRevokeHandler>.Instance);
            await revoke.HandleAsync(verificationId, reviewerId,
                new DecideRevokeRequest(new ReviewerReason("Regulatory revocation.", null)),
                CancellationToken.None);
        }

        var audit = new RecordingAuditPublisher();
        await using var db2 = NewContext();
        var open = new OpenHistoricalDocumentHandler(
            db2, FakeStorage.Instance,
            NewPiiRecorder(audit, isAdminPath: true));

        var result = await open.HandleAsync(
            verificationId, documentId,
            new HashSet<string> { "ksa" },
            CancellationToken.None);

        result.Exists.Should().BeTrue();
        result.IsPurged.Should().BeFalse();
        result.Response!.IsHistoricalOpen.Should().BeTrue(
            "revoked is terminal — historical_open flag = true");

        audit.Captured.Should().HaveCount(2,
            "terminal-state opens MUST emit TWO audit events: body-read + historical-open");
        audit.Captured[0].Action.Should().Be("verification.pii_access");
        audit.Captured[1].Action.Should().Be("verification.pii_access.historical_open");
    }

    [Fact]
    public async Task OpenHistoricalDocument_returns_purged_for_purged_document()
    {
        var (_, verificationId, _) = await SubmitAsync();
        var purgedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var documentId = await InsertDocumentAsync(
            verificationId,
            storageKey: null,        // null = body purged
            purgedAt: purgedAt);

        var audit = new RecordingAuditPublisher();
        await using var db = NewContext();
        var open = new OpenHistoricalDocumentHandler(
            db, FakeStorage.Instance,
            NewPiiRecorder(audit, isAdminPath: true));

        var result = await open.HandleAsync(
            verificationId, documentId,
            new HashSet<string> { "ksa" },
            CancellationToken.None);

        result.Exists.Should().BeTrue();
        result.IsPurged.Should().BeTrue();
        result.PurgedAt.Should().Be(purgedAt);
        audit.Captured.Should().BeEmpty(
            "purged-body opens emit no PII events — there's nothing to read");
    }

    [Fact]
    public async Task OpenHistoricalDocument_returns_NotFound_for_foreign_market()
    {
        var (_, verificationId, _) = await SubmitAsync(market: "ksa");
        var documentId = await InsertDocumentAsync(verificationId, "verifications/doc-003.pdf");

        await using var db = NewContext();
        var open = new OpenHistoricalDocumentHandler(
            db, FakeStorage.Instance,
            NewPiiRecorder(new RecordingAuditPublisher(), isAdminPath: true));

        var result = await open.HandleAsync(
            verificationId, documentId,
            new HashSet<string> { "eg" },
            CancellationToken.None);

        result.Exists.Should().BeFalse(
            "an EG-only reviewer MUST see NotFound (not Forbidden) for a KSA document");
    }

    private async Task<Guid> InsertDocumentAsync(
        Guid verificationId,
        string? storageKey,
        DateTimeOffset? purgedAt = null)
    {
        await using var db = NewContext();
        var doc = new VerificationDocument
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = "ksa",
            StorageKey = storageKey,
            ContentType = "application/pdf",
            SizeBytes = 1024,
            ScanStatus = "clean",
            UploadedAt = DateTimeOffset.UtcNow,
            PurgeAfter = null,
            PurgedAt = purgedAt,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private async Task<(Guid CustomerId, Guid VerificationId, DateTimeOffset SubmittedAt)> SubmitAsync(
        string market = "ksa")
    {
        var customerId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero));

        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);
        var result = await submit.HandleAsync(customerId, market,
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: $"SCFHS-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant(),
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, result.Response.SubmittedAt);
    }

    private (DecideRevokeHandler, RecordingAuditPublisher) NewOpenHandler(
        RecordingAuditPublisher audit, bool isAdminPath)
    {
        // Helper to keep call sites symmetric — Open handler also needs an audit
        // publisher via PiiAccessRecorder.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var db = NewContext();
        var revoke = new DecideRevokeHandler(
            db, new EligibilityCacheInvalidator(), audit, new NullVerificationDomainEventPublisher(), clock,
            NullLogger<DecideRevokeHandler>.Instance);
        return (revoke, audit);
    }

    private static PiiAccessRecorder NewPiiRecorder(IAuditEventPublisher audit, bool isAdminPath)
    {
        var contextAccessor = new HttpContextAccessor();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = isAdminPath
            ? "/api/admin/verifications/test"
            : "/api/customer/verifications/test";
        contextAccessor.HttpContext = ctx;
        return new PiiAccessRecorder(audit, contextAccessor);
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

    private sealed class FakeStorage : IStorageService
    {
        public static readonly FakeStorage Instance = new();
        public Task<StoredFileResult> UploadAsync(Stream content, string fileName, string mimeType, MarketCode market, CancellationToken cancellationToken)
            => throw new NotImplementedException("Fake storage — upload not exercised by the open path.");
        public Task<Uri> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken cancellationToken)
            => Task.FromResult(new Uri($"https://storage.test/{fileId}?signed=1&expires={expiry.TotalSeconds}"));
        public Task DeleteAsync(string fileId, CancellationToken cancellationToken)
            => throw new NotImplementedException("Fake storage — delete not exercised.");
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
