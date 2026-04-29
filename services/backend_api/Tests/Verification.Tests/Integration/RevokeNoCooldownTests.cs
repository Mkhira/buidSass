using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Admin.DecideReject;
using BackendApi.Modules.Verification.Admin.DecideRevoke;
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
/// Spec 020 task T105 / FR-009. A customer whose verification was REJECTED is
/// in cooldown for <c>cooldown_days</c>; a customer whose verification was
/// REVOKED may submit a new verification immediately. T107 in the
/// SubmitVerificationHandler implements the carve-out.
/// </summary>
public sealed class RevokeNoCooldownTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_revoke_cooldown_test")
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
    public async Task Revoked_customer_can_submit_immediately()
    {
        var customerId = Guid.NewGuid();
        await ApproveAndRevokeAsync(customerId);

        // Try to submit again the same day as the revoke — must succeed (FR-009).
        var snapshot = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var result = await SubmitAsync(customerId, snapshot);

        result.IsSuccess.Should().BeTrue(
            $"revoked customers face NO cooldown per FR-009; failure detail: {result.Detail}");
    }

    [Fact]
    public async Task Rejected_customer_inside_cooldown_is_blocked()
    {
        var customerId = Guid.NewGuid();
        await SubmitAndRejectAsync(customerId);

        // Submit one day after rejection — inside the 7-day KSA cooldown.
        var snapshot = new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero);
        var result = await SubmitAsync(customerId, snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(VerificationReasonCode.CooldownActive);
    }

    [Fact]
    public async Task Rejected_customer_after_cooldown_can_submit()
    {
        var customerId = Guid.NewGuid();
        await SubmitAndRejectAsync(customerId);

        // 8 days after rejection — past the 7-day KSA cooldown.
        var snapshot = new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero);
        var result = await SubmitAsync(customerId, snapshot);

        result.IsSuccess.Should().BeTrue($"cooldown elapsed: {result.Detail}");
    }

    // ────────────────────────── helpers ──────────────────────────

    private async Task<SubmitResult> SubmitAsync(Guid customerId, DateTimeOffset snapshot)
    {
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new FakeTimeProvider(snapshot),
            NullLogger<SubmitVerificationHandler>.Instance);
        return await submit.HandleAsync(customerId, "ksa",
            new SubmitVerificationRequest("dentist", "SCFHS-1234567", Array.Empty<Guid>(), null),
            CancellationToken.None);
    }

    private async Task ApproveAndRevokeAsync(Guid customerId)
    {
        Guid verificationId;
        await using (var db = NewContext())
        {
            var submit = new SubmitVerificationHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero)),
                NullLogger<SubmitVerificationHandler>.Instance);
            var result = await submit.HandleAsync(customerId, "ksa",
                new SubmitVerificationRequest("dentist", "SCFHS-1234567", Array.Empty<Guid>(), null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            verificationId = result.Response!.Id;
        }
        await using (var db = NewContext())
        {
            var approve = new DecideApproveHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideApproveHandler>.Instance);
            var result = await approve.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideApproveRequest(new ReviewerReason("Verified.", null)),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }
        await using (var db = NewContext())
        {
            var revoke = new DecideRevokeHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideRevokeHandler>.Instance);
            var result = await revoke.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideRevokeRequest(new ReviewerReason("Compliance issue.", null)),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }
    }

    private async Task SubmitAndRejectAsync(Guid customerId)
    {
        Guid verificationId;
        await using (var db = NewContext())
        {
            var submit = new SubmitVerificationHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero)),
                NullLogger<SubmitVerificationHandler>.Instance);
            var result = await submit.HandleAsync(customerId, "ksa",
                new SubmitVerificationRequest("dentist", "SCFHS-1234567", Array.Empty<Guid>(), null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            verificationId = result.Response!.Id;
        }
        await using (var db = NewContext())
        {
            var reject = new DecideRejectHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<DecideRejectHandler>.Instance);
            var result = await reject.HandleAsync(verificationId, Guid.NewGuid(),
                new DecideRejectRequest(new ReviewerReason("Documentation incomplete.", null)),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }
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

    private sealed class RecordingAuditPublisher : IAuditEventPublisher
    {
        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
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
