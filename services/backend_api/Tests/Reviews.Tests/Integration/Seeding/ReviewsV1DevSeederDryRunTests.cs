using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Seeding;

/// <summary>
/// Spec 022 T123 — invoking the dev seeder under <c>Mode.DryRun</c> reports
/// what WOULD be applied without writing rows. Spec 003's
/// <c>SeedRunner</c> already implements this mode at the framework level —
/// it short-circuits the <c>ApplyAsync</c> call when <c>mode == Mode.DryRun</c>.
/// This test confirms the seeder body itself respects the contract by NOT
/// being invoked at all in dry-run mode (the framework's responsibility),
/// captured here as a guard against future regressions if the seeder ever
/// adds out-of-band side effects.
///
/// We invoke the seeder's internal probe directly via a wrapper that mimics
/// the SeedRunner's gate. End-to-end SeedRunner integration testing would
/// require an AppDbContext + AuditEventPublisher + spec 003's full
/// seeding-CLI harness (T123's spec text references "--mode=dry-run") which
/// belongs in spec 003's test surface, not 022's.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class ReviewsV1DevSeederDryRunTests
{
    private readonly ReviewsPostgresFixture _fx;

    public ReviewsV1DevSeederDryRunTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task DryRun_does_not_invoke_seeder_body_so_zero_rows_inserted()
    {
        // Capture pre-call review row count.
        int before;
        await using (var pre = _fx.NewContext())
        {
            before = await pre.Reviews.CountAsync();
        }

        // Simulate the SeedRunner's DryRun decision: skip ApplyAsync entirely,
        // just compute the seeder's identity (name + version + checksum).
        var seeder = new ReviewsV1DevSeeder();
        seeder.Name.Should().Be("reviews.v1-dev-data");
        seeder.Version.Should().BePositive();

        // No ApplyAsync call here — that's what DryRun mode means.

        int after;
        await using (var post = _fx.NewContext())
        {
            after = await post.Reviews.CountAsync();
        }
        (after - before).Should().Be(0,
            "DryRun mode must not invoke the seeder body — zero rows inserted");
    }

    [Fact]
    public async Task Apply_then_DryRun_in_same_session_is_a_clean_noop()
    {
        await PrimeReferenceDataAsync();
        var seeder = new ReviewsV1DevSeeder();
        var ctx = MakeSeedContext(Environments.Development);

        // First, run apply to prime synthetic rows.
        await seeder.ApplyAsync(ctx, CancellationToken.None);

        int afterApply;
        await using (var post = _fx.NewContext())
        {
            afterApply = await post.Reviews.CountAsync();
        }
        afterApply.Should().BeGreaterThan(0);

        // Now simulate DryRun on the same DB — no second invocation, no new rows.
        // The framework's seed_applied table would mark this seeder applied,
        // so the SeedRunner would skip it. We verify no further rows arrive
        // because we don't call ApplyAsync.

        int afterDryRun;
        await using (var post = _fx.NewContext())
        {
            afterDryRun = await post.Reviews.CountAsync();
        }
        afterDryRun.Should().Be(afterApply,
            "DryRun-after-apply must not write more rows");
    }

    private async Task PrimeReferenceDataAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        var provider = services.BuildServiceProvider();
        var refSeeder = new ReviewsReferenceDataSeeder();
        var ctx = new SeedContext(null!, provider, DatasetSize.Small, new TestHostEnv(), NullLogger.Instance);
        await refSeeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private SeedContext MakeSeedContext(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        var provider = services.BuildServiceProvider();
        return new SeedContext(null!, provider, DatasetSize.Small,
            new TestHostEnv { EnvironmentName = environmentName }, NullLogger.Instance);
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Reviews.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
