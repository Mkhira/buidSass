using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Admin.DecideApprove;
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
/// Spec 020 task T081 — verifies <c>EvaluateManyAsync</c> returns the same
/// answer per SKU as N sequential <c>EvaluateAsync</c> calls, including a
/// 50-SKU catalog-list-page-shape fixture.
/// </summary>
public sealed class EligibilityBulkQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verification_eligibility_bulk_test")
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
    public async Task EvaluateMany_returns_per_sku_answer_matching_sequential_evaluate()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId, "ksa", "dentist");

        var skus = new[]
        {
            "UN-tongue-depressor",            // unrestricted
            "KSA-restricted-anesthetic",      // restricted in customer's market — eligible (approved)
            "EG-only-restricted",             // restricted only in EG — silent path
            "DENTIST-BOTH-implant",           // requires dentist — match
            "BOTH-restricted-instrument",     // restricted both markets, no profession req — eligible
        };

        await using var db = NewContext();
        var query = NewQuery(db);

        var bulk = await query.EvaluateManyAsync(customerId, "ksa", skus, default);

        foreach (var sku in skus)
        {
            var single = await query.EvaluateAsync(customerId, "ksa", sku, default);
            bulk.Should().ContainKey(sku);
            bulk[sku].Class.Should().Be(single.Class, $"bulk vs single divergence for sku={sku}");
            bulk[sku].ReasonCode.Should().Be(single.ReasonCode, $"bulk vs single ReasonCode divergence for sku={sku}");
            bulk[sku].MessageKey.Should().Be(single.MessageKey);
        }
    }

    [Fact]
    public async Task EvaluateMany_handles_empty_input_returns_empty_dict()
    {
        await using var db = NewContext();
        var query = NewQuery(db);
        var result = await query.EvaluateManyAsync(Guid.NewGuid(), "ksa", Array.Empty<string>(), default);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateMany_handles_50_sku_catalog_page()
    {
        var customerId = Guid.NewGuid();
        await ApproveCustomerAsync(customerId, "ksa", "dentist");

        // 50 SKUs: mix of unrestricted, KSA-restricted, BOTH-restricted, dentist-required.
        var skus = new List<string>(50);
        for (var i = 0; i < 50; i++)
        {
            var prefix = (i % 4) switch
            {
                0 => "UN-",
                1 => "KSA-",
                2 => "BOTH-",
                _ => "DENTIST-BOTH-",
            };
            skus.Add($"{prefix}sku-{i:D3}");
        }

        await using var db = NewContext();
        var query = NewQuery(db);

        var bulk = await query.EvaluateManyAsync(customerId, "ksa", skus, default);

        bulk.Should().HaveCount(50);

        // Spot-check distribution:
        bulk.Where(kv => kv.Key.StartsWith("UN-")).Should().AllSatisfy(kv =>
            kv.Value.Class.Should().Be(EligibilityClass.Unrestricted));
        bulk.Where(kv => kv.Key.StartsWith("KSA-")).Should().AllSatisfy(kv =>
            kv.Value.Class.Should().Be(EligibilityClass.Eligible));
        bulk.Where(kv => kv.Key.StartsWith("BOTH-")).Should().AllSatisfy(kv =>
            kv.Value.Class.Should().Be(EligibilityClass.Eligible));
        bulk.Where(kv => kv.Key.StartsWith("DENTIST-BOTH-")).Should().AllSatisfy(kv =>
            kv.Value.Class.Should().Be(EligibilityClass.Eligible));
    }

    [Fact]
    public async Task EvaluateMany_dedup_protects_duplicate_skus_in_input()
    {
        var customerId = Guid.NewGuid();
        await using var db = NewContext();
        var query = NewQuery(db);

        var skus = new[] { "UN-x", "UN-x", "UN-y", "UN-x" };
        var bulk = await query.EvaluateManyAsync(customerId, "ksa", skus, default);

        bulk.Should().HaveCount(2, "duplicates collapse into the dictionary's key set");
        bulk.Should().ContainKey("UN-x");
        bulk.Should().ContainKey("UN-y");
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        Guid verificationId;
        await using (var db = NewContext())
        {
            var submit = new SubmitVerificationHandler(
                db, new EligibilityCacheInvalidator(), new RecordingAuditPublisher(),
                clock, NullLogger<SubmitVerificationHandler>.Instance);
            var regulator = marketCode == "ksa" ? "SCFHS-1234567" : "EMS-1234567";
            var result = await submit.HandleAsync(customerId, marketCode,
                new SubmitVerificationRequest(profession, regulator, Array.Empty<Guid>(), null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            verificationId = result.Response!.Id;
        }
        await using (var db = NewContext())
        {
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
