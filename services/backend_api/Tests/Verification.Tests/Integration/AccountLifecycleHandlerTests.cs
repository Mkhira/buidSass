using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Customer.SubmitVerification;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Hooks;
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
/// Spec 020 task T112 / research §R6 + §R7 / FR-027 + FR-038. Verifies the
/// AccountLifecycleHandler correctly:
/// <list type="bullet">
///   <item>voids non-terminal verifications on lock/delete,</item>
///   <item>supersedes active approvals on lock/delete,</item>
///   <item>scopes market-changed effects to the FROM market only,</item>
///   <item>flips the eligibility cache to ineligible,</item>
///   <item>expedites doc purge on delete (privacy hard-stop).</item>
/// </list>
/// </summary>
public sealed class AccountLifecycleHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_lifecycle_handler_test")
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

    public async Task DisposeAsync()
    {
        foreach (var ctx in _trackedContexts)
        {
            await ctx.DisposeAsync();
        }
        await _postgres.DisposeAsync();
    }

    private VerificationDbContext NewContext() => new(
        new DbContextOptionsBuilder<VerificationDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact]
    public async Task OnAccountLocked_voids_non_terminal_and_supersedes_approval()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitAsync(customerId, "ksa", "dentist");
        await ApproveAsync(verificationId);

        // Now another submission goes pending — locked event should void it.
        // (One per market — the renewal flow controls the same-market case;
        //  for this test we use a separate market to avoid the AlreadyPending
        //  guard.)
        var pendingId = await SubmitAsync(customerId, "eg", "dentist");

        var snapshot = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var handler = BuildHandler(snapshot);

        await handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "admin_action", snapshot),
            CancellationToken.None);

        await using var db = NewContext();
        var approved = await db.Verifications.AsNoTracking().SingleAsync(v => v.Id == verificationId);
        approved.State.Should().Be(VerificationState.Superseded,
            "active approval is superseded on lock");
        var pending = await db.Verifications.AsNoTracking().SingleAsync(v => v.Id == pendingId);
        pending.State.Should().Be(VerificationState.Void,
            "in-flight submission is voided on lock");
        pending.VoidReason.Should().Be("account_inactive");
    }

    [Fact]
    public async Task OnAccountDeleted_expedites_document_purge()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitAsync(customerId, "ksa", "dentist");
        await using (var db = NewContext())
        {
            db.Documents.Add(new BackendApi.Modules.Verification.Entities.VerificationDocument
            {
                Id = Guid.NewGuid(),
                VerificationId = verificationId,
                MarketCode = "ksa",
                StorageKey = "test/key/lifecycle",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                ScanStatus = "clean",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var snapshot = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var handler = BuildHandler(snapshot);

        await handler.OnAccountDeletedAsync(
            new CustomerAccountDeleted(customerId, snapshot),
            CancellationToken.None);

        await using var ctx = NewContext();
        var doc = await ctx.Documents.AsNoTracking().SingleAsync(d => d.VerificationId == verificationId);
        doc.PurgeAfter.Should().Be(snapshot,
            "deleted accounts trigger immediate purge (privacy hard-stop)");
    }

    [Fact]
    public async Task OnMarketChanged_voids_only_the_from_market_rows()
    {
        var customerId = Guid.NewGuid();
        var ksaId = await SubmitAsync(customerId, "ksa", "dentist");
        await ApproveAsync(ksaId);
        var egId = await SubmitAsync(customerId, "eg", "dentist");

        var snapshot = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var handler = BuildHandler(snapshot);

        await handler.OnMarketChangedAsync(
            new CustomerMarketChanged(customerId, FromMarket: "ksa", ToMarket: "eg",
                ChangedBy: Guid.NewGuid(), OccurredAt: snapshot),
            CancellationToken.None);

        await using var db = NewContext();
        var ksa = await db.Verifications.AsNoTracking().SingleAsync(v => v.Id == ksaId);
        ksa.State.Should().Be(VerificationState.Superseded);
        var eg = await db.Verifications.AsNoTracking().SingleAsync(v => v.Id == egId);
        eg.State.Should().Be(VerificationState.Submitted,
            "EG row must be untouched — market-changed scope is FROM-market only");
    }

    [Fact]
    public async Task Idempotent_on_redelivered_event()
    {
        var customerId = Guid.NewGuid();
        var verificationId = await SubmitAsync(customerId, "ksa", "dentist");
        await ApproveAsync(verificationId);

        var snapshot = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var handler = BuildHandler(snapshot);

        await handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "admin_action", snapshot),
            CancellationToken.None);
        // Redelivery — must no-op.
        var act = async () => await handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "admin_action", snapshot.AddMinutes(5)),
            CancellationToken.None);

        await act.Should().NotThrowAsync(
            "redelivery hits already-terminal rows; the handler must idempotently no-op");

        await using var db = NewContext();
        var transitions = await db.StateTransitions.AsNoTracking()
            .Where(t => t.VerificationId == verificationId && t.NewState == "superseded")
            .CountAsync();
        transitions.Should().Be(1, "exactly one supersession ledger row, even after redelivery");
    }

    // ────────────────────────── helpers ──────────────────────────

    private readonly List<VerificationDbContext> _trackedContexts = new();

    private AccountLifecycleHandler BuildHandler(DateTimeOffset snapshot)
    {
        var db = NewContext();
        // Track for disposal in DisposeAsync — handlers retain the DbContext
        // for the duration of HandleAsync but the test owns the lifetime
        // (CR R1 Minor — connection-pool leak under parallel test runs).
        _trackedContexts.Add(db);
        return new AccountLifecycleHandler(
            db, new EligibilityCacheInvalidator(),
            new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
            new FakeTimeProvider(snapshot),
            NullLogger<AccountLifecycleHandler>.Instance);
    }

    private async Task<Guid> SubmitAsync(Guid customerId, string marketCode, string profession)
    {
        await using var db = NewContext();
        var submit = new SubmitVerificationHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<SubmitVerificationHandler>.Instance);
        var regulator = marketCode == "ksa" ? "SCFHS-1234567" : "EMS/12345/678";
        var result = await submit.HandleAsync(customerId, marketCode,
            new SubmitVerificationRequest(profession, regulator, Array.Empty<Guid>(), null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue($"submit failed: {result.Detail}");
        return result.Response!.Id;
    }

    private async Task ApproveAsync(Guid verificationId)
    {
        await using var db = NewContext();
        var approve = new DecideApproveHandler(
            db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
            new NullVerificationDomainEventPublisher(),
                new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DecideApproveHandler>.Instance);
        var result = await approve.HandleAsync(verificationId, Guid.NewGuid(),
            new DecideApproveRequest(new ReviewerReason("Verified.", null)),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
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
