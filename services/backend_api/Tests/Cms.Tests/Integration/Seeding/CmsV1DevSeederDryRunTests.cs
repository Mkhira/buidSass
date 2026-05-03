using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Seeding;
using Cms.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cms.Tests.Integration.Seeding;

/// <summary>
/// T136 — dry-run mode for <c>CmsV1DevSeeder</c>. Asserts that
/// <c>--mode=dry-run</c> (modelled here as a direct call to
/// <see cref="CmsV1DevSeeder.ComputeDryRunAsync"/>) writes nothing to the DB
/// and returns a planned-changes report enumerating per-kind insert + present
/// counts.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class CmsV1DevSeederDryRunTests
{
    private readonly CmsPostgresFixture _fx;

    public CmsV1DevSeederDryRunTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Cms.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private SeedContext BuildContext(IServiceProvider sp) =>
        new(
            Db: null!,
            Services: sp,
            Size: DatasetSize.Small,
            Env: new TestHostEnv(),
            Logger: NullLogger.Instance);

    [Fact]
    public async Task DryRun_on_empty_db_reports_full_pending_insert_set_and_writes_nothing()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();

        var connStr = _fx.ConnectionString;
        var services = new ServiceCollection()
            .AddDbContext<CmsDbContext>(opt => opt.UseNpgsql(connStr))
            .BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
        {
            var sut = new CmsV1DevSeeder();
            var report = await sut.ComputeDryRunAsync(BuildContext(scope.ServiceProvider), ct);

            // Every kind reports pending inserts; nothing already-present.
            report.BannerSlots.WouldInsertCount.Should().BeGreaterThan(0);
            report.BannerSlots.AlreadyPresentCount.Should().Be(0);

            report.FeaturedSections.WouldInsertCount.Should().BeGreaterThan(0);
            report.FeaturedSections.AlreadyPresentCount.Should().Be(0);

            report.FaqEntries.WouldInsertCount.Should().BeGreaterThan(0);
            report.FaqEntries.AlreadyPresentCount.Should().Be(0);

            report.LegalPageVersions.WouldInsertCount.Should().BeGreaterThan(0);
            report.LegalPageVersions.AlreadyPresentCount.Should().Be(0);

            report.BlogArticles.WouldInsertCount.Should().BeGreaterThan(0);
            report.BlogArticles.AlreadyPresentCount.Should().Be(0);

            report.TotalWouldInsert.Should().BeGreaterThan(0);
            report.TotalAlreadyPresent.Should().Be(0);
        }

        // Side-effect-free guarantee: every CMS table is still empty.
        await using var verify = _fx.NewContext();
        (await verify.BannerSlots.CountAsync(ct)).Should().Be(0);
        (await verify.FeaturedSections.CountAsync(ct)).Should().Be(0);
        (await verify.FaqEntries.CountAsync(ct)).Should().Be(0);
        (await verify.BlogArticles.CountAsync(ct)).Should().Be(0);
        (await verify.LegalPageVersions.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task DryRun_after_apply_reports_zero_pending_inserts()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();

        var connStr = _fx.ConnectionString;
        var services = new ServiceCollection()
            .AddDbContext<CmsDbContext>(opt => opt.UseNpgsql(connStr))
            .BuildServiceProvider();

        // Step 1 — actually apply.
        await using (var scope = services.CreateAsyncScope())
        {
            var sut = new CmsV1DevSeeder();
            await sut.ApplyAsync(BuildContext(scope.ServiceProvider), ct);
        }

        // Step 2 — dry-run on the populated DB. Every kind should report
        // zero pending inserts (idempotent convergence).
        await using (var scope = services.CreateAsyncScope())
        {
            var sut = new CmsV1DevSeeder();
            var report = await sut.ComputeDryRunAsync(BuildContext(scope.ServiceProvider), ct);

            report.BannerSlots.WouldInsertCount.Should().Be(0);
            report.FeaturedSections.WouldInsertCount.Should().Be(0);
            report.FaqEntries.WouldInsertCount.Should().Be(0);
            report.LegalPageVersions.WouldInsertCount.Should().Be(0);
            report.BlogArticles.WouldInsertCount.Should().Be(0);
            report.TotalWouldInsert.Should().Be(0);
            report.TotalAlreadyPresent.Should().BeGreaterThan(0);
        }
    }
}
