using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideRevoke;
using BackendApi.Modules.Verification.Admin.DecideReject;
using BackendApi.Modules.Verification.Admin.DecideRequestInfo;
using BackendApi.Modules.Verification.Customer.RequestRenewal;
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
/// Spec 020 task T080 — verifies every state transition rebuilds the
/// eligibility cache row inside the same Tx so the read-side answer never
/// drifts from authoritative state.
///
/// <para>The "in same Tx" invariant is exercised by reading the cache row
/// straight after a successful handler call. If the rebuild were post-commit
/// the row would briefly be stale; observed pre-commit drift would be a bug.</para>
/// </summary>
public sealed class EligibilityCacheInvalidationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_cache_invalidation_test")
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
    public async Task Submit_writes_pending_cache_row()
    {
        var customerId = Guid.NewGuid();
        await SubmitForCustomerAsync(customerId);

        var cache = await ReadCacheAsync(customerId, "ksa");
        cache.Should().NotBeNull();
        cache!.EligibilityClass.Should().Be("ineligible");
        cache.ReasonCode.Should().Be("VerificationPending");
    }

    [Fact]
    public async Task Approve_flips_cache_to_eligible()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId);
        await ApproveAsync(verificationId);

        var cache = await ReadCacheAsync(customerId, "ksa");
        cache!.EligibilityClass.Should().Be("eligible");
        cache.ReasonCode.Should().BeNull();
        cache.ExpiresAt.Should().NotBeNull();
        cache.ProfessionsJson.Should().Contain("dentist");
    }

    [Fact]
    public async Task Reject_flips_cache_to_ineligible_rejected()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId);
        await RejectAsync(verificationId);

        var cache = await ReadCacheAsync(customerId, "ksa");
        cache!.EligibilityClass.Should().Be("ineligible");
        cache.ReasonCode.Should().Be("VerificationRejected");
    }

    [Fact]
    public async Task RequestInfo_flips_cache_to_ineligible_info_requested()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId);
        await RequestInfoAsync(verificationId);

        var cache = await ReadCacheAsync(customerId, "ksa");
        cache!.EligibilityClass.Should().Be("ineligible");
        cache.ReasonCode.Should().Be("VerificationInfoRequested");
    }

    [Fact]
    public async Task Revoke_flips_eligible_cache_to_revoked()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId);
        await ApproveAsync(verificationId);
        await RevokeAsync(verificationId);

        var cache = await ReadCacheAsync(customerId, "ksa");
        cache!.EligibilityClass.Should().Be("ineligible");
        cache.ReasonCode.Should().Be("VerificationRevoked");
    }

    [Fact]
    public async Task Renewal_request_keeps_customer_eligible_via_active_approval()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(
            customerId, submittedAt: new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        await ApproveAsync(verificationId, decidedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

        // 2027-04-15 is inside the 30-day reminder window before 2027-05-01 expiry.
        await RenewAsync(customerId, snapshot: new DateTimeOffset(2027, 4, 15, 9, 0, 0, TimeSpan.Zero));

        var cache = await ReadCacheAsync(customerId, "ksa");
        cache!.EligibilityClass.Should().Be("eligible",
            "renewal request creates a new submitted row but the prior approval is still active");
        cache.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task Cache_is_market_partitioned_separate_rows_per_market()
    {
        var customerId = Guid.NewGuid();
        // Approve in KSA.
        var ksaId = await SubmitForCustomerAsync(customerId, marketCode: "ksa");
        await ApproveAsync(ksaId);

        // Submit in EG (no approval yet).
        await SubmitForCustomerAsync(customerId, marketCode: "eg",
            // Different submittedAt to avoid the AlreadyPending guard collision in tests
            submittedAt: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));

        var ksaCache = await ReadCacheAsync(customerId, "ksa");
        var egCache = await ReadCacheAsync(customerId, "eg");

        ksaCache!.EligibilityClass.Should().Be("eligible",
            "KSA approval is independent of EG state");
        egCache!.EligibilityClass.Should().Be("ineligible");
        egCache.ReasonCode.Should().Be("VerificationPending");
    }

    // ────────────────────────── helpers ──────────────────────────

    private record CacheRow(string EligibilityClass, string? ReasonCode, DateTimeOffset? ExpiresAt, string ProfessionsJson);

    private async Task<CacheRow?> ReadCacheAsync(Guid customerId, string marketCode)
    {
        await using var db = NewContext();
        return await db.EligibilityCache
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.MarketCode == marketCode)
            .Select(c => new CacheRow(c.EligibilityClass, c.ReasonCode, c.ExpiresAt, c.ProfessionsJson))
            .SingleOrDefaultAsync();
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

    private async Task<Guid> SubmitForCustomerAsync(
        Guid customerId,
        string marketCode = "ksa",
        DateTimeOffset? submittedAt = null)
    {
        var clock = new FakeTimeProvider(submittedAt ?? new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);
        var regulator = marketCode == "ksa" ? "SCFHS-1234567" : "EMS-1234567";
        var result = await submit.HandleAsync(customerId, marketCode,
            new SubmitVerificationRequest(
                Profession: "dentist",
                RegulatorIdentifier: regulator,
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"submit failed: {result.Detail}");
        return result.Response!.Id;
    }

    private async Task ApproveAsync(Guid verificationId, DateTimeOffset? decidedAt = null)
    {
        await using var db = NewContext();
        var approve = new DecideApproveHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(decidedAt ?? new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideApproveHandler>.Instance);
        var result = await approve.HandleAsync(verificationId, Guid.NewGuid(),
            new DecideApproveRequest(new ReviewerReason("Verified.", null)),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task RejectAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var reject = new DecideRejectHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideRejectHandler>.Instance);
        var result = await reject.HandleAsync(verificationId, Guid.NewGuid(),
            new DecideRejectRequest(new ReviewerReason("Not approved.", null)),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task RevokeAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var revoke = new DecideRevokeHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideRevokeHandler>.Instance);
        var result = await revoke.HandleAsync(verificationId, Guid.NewGuid(),
            new DecideRevokeRequest(new ReviewerReason("Compliance issue.", null)),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task RequestInfoAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var info = new DecideRequestInfoHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideRequestInfoHandler>.Instance);
        var result = await info.HandleAsync(verificationId, Guid.NewGuid(),
            new DecideRequestInfoRequest(new ReviewerReason("Need clearer scan.", null)),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task RenewAsync(Guid customerId, DateTimeOffset snapshot)
    {
        await using var db = NewContext();
        var renew = new RequestRenewalHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new FakeTimeProvider(snapshot),
            NullLogger<RequestRenewalHandler>.Instance);
        var result = await renew.HandleAsync(customerId, "ksa",
            new RequestRenewalRequest(null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"renew failed: {result.Detail}");
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
