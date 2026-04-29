using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideRevoke;
using BackendApi.Modules.Verification.Admin.DecideReject;
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
/// Spec 020 task T079 — synthetic matrix asserting every
/// <see cref="EligibilityReasonCode"/> is exercised at least once (SC-008),
/// plus the cross-market edge cases from <c>spec.md §Edge Cases</c>.
///
/// SKU prefix conventions (encoded by <see cref="StubProductRestrictionPolicy"/>):
/// <list type="bullet">
///   <item><c>UN-*</c> — unrestricted everywhere</item>
///   <item><c>KSA-*</c> — restricted in KSA only</item>
///   <item><c>EG-*</c> — restricted in EG only</item>
///   <item><c>BOTH-*</c> — restricted in both markets</item>
///   <item><c>DENTIST-BOTH-*</c> — restricted in both, requires dentist profession</item>
/// </list>
/// </summary>
public sealed class EligibilityQueryMatrixTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_eligibility_matrix_test")
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

    private CustomerVerificationEligibilityQuery NewQuery(VerificationDbContext db)
        => new(db, new StubProductRestrictionPolicy());

    [Fact]
    public async Task Unrestricted_sku_returns_unrestricted_silent_path()
    {
        var customerId = Guid.NewGuid();
        await using var db = NewContext();
        var query = NewQuery(db);

        var result = await query.EvaluateAsync(customerId, "ksa", "UN-tongue-depressor", default);

        result.Class.Should().Be(EligibilityClass.Unrestricted);
        result.ReasonCode.Should().Be(EligibilityReasonCode.Unrestricted);
        result.MessageKey.Should().Be("verification.eligibility.unrestricted");
        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Eligible_when_customer_market_matches_approved_verification_market()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId, "ksa", "dentist");

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Eligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.Eligible);
        result.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarketMismatch_when_verification_in_different_market_than_customer_current()
    {
        // Edge case from spec.md: verification_state=approved, verification_market=eg,
        // customer_current_market=ksa, sku_restricted_in=[ksa] → Ineligible: MarketMismatch.
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId, "eg", "dentist");

        await using var db = NewContext();
        var query = NewQuery(db);
        // Customer's current market is ksa (no cache row exists for ksa).
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.MarketMismatch,
            "customer is approved in EG but currently in KSA buying a KSA-restricted SKU — spec.md §Edge Cases mandates MarketMismatch (not VerificationRequired)");
    }

    [Fact]
    public async Task Cross_market_unrestricted_when_sku_restricted_only_in_other_market()
    {
        // Inverse edge case: verification_market=ksa, customer_current_market=ksa,
        // sku_restricted_in=[eg] → Eligible (silent path — restriction does not apply
        // in customer's market).
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId, "ksa", "dentist");

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "EG-only-restricted", default);

        result.Class.Should().Be(EligibilityClass.Unrestricted,
            "restriction applies only in EG; customer is in KSA — silent path");
        result.ReasonCode.Should().Be(EligibilityReasonCode.Unrestricted);
    }

    [Fact]
    public async Task ProfessionMismatch_when_verification_profession_does_not_satisfy_sku_required()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId, "ksa", profession: "dental_lab_tech");

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "DENTIST-BOTH-implant", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.ProfessionMismatch);
    }

    [Fact]
    public async Task VerificationRequired_when_no_cache_row_exists_for_market()
    {
        var customerId = Guid.NewGuid();

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.VerificationRequired);
    }

    [Fact]
    public async Task VerificationPending_when_customer_is_in_submitted_state()
    {
        var customerId = Guid.NewGuid();
        await SubmitForCustomerAsync(customerId, "ksa", "dentist");

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.VerificationPending,
            "submitted state in cache should map to VerificationPending reason code");
    }

    [Fact]
    public async Task VerificationRevoked_when_customer_was_revoked()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId, "ksa", "dentist");
        await ApproveAsync(verificationId);
        await RevokeAsync(verificationId);

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.VerificationRevoked);
    }

    [Fact]
    public async Task VerificationRejected_when_customer_was_rejected()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId, "ksa", "dentist");
        await RejectAsync(verificationId);

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.VerificationRejected);
    }

    [Fact]
    public async Task VerificationInfoRequested_when_reviewer_asked_for_more_info()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitForCustomerAsync(customerId, "ksa", "dentist");
        await RequestInfoAsync(verificationId);

        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateAsync(customerId, "ksa", "KSA-restricted-anesthetic", default);

        result.Class.Should().Be(EligibilityClass.Ineligible);
        result.ReasonCode.Should().Be(EligibilityReasonCode.VerificationInfoRequested);
    }

    // ────────────────────────── helpers ──────────────────────────

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

    private async Task ApproveCustomerAsync(Guid customerId, string marketCode, string profession)
    {
        var verificationId = await SubmitForCustomerAsync(customerId, marketCode, profession);
        await ApproveAsync(verificationId);
    }

    private async Task<Guid> SubmitForCustomerAsync(Guid customerId, string marketCode, string profession)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            clock, NullLogger<SubmitVerificationHandler>.Instance);
        var regulator = marketCode == "ksa" ? "SCFHS-1234567" : "EMS-1234567";
        var result = await submit.HandleAsync(customerId, marketCode,
            new SubmitVerificationRequest(
                Profession: profession,
                RegulatorIdentifier: regulator,
                DocumentIds: Array.Empty<Guid>(),
                SupersedesId: null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"submit must succeed for matrix setup; detail={result.Detail}");
        return result.Response!.Id;
    }

    private async Task ApproveAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var approve = new DecideApproveHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
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
                new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
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
