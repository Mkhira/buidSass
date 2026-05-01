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
/// T134 — distribution check; T135 — idempotency check.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class CmsV1DevSeederTests
{
    private readonly CmsPostgresFixture _fx;

    public CmsV1DevSeederTests(CmsPostgresFixture fx)
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

    private async Task SeedAsync(CancellationToken ct)
    {
        // Build a service provider that hands back a fresh CmsDbContext
        // backed by the Testcontainers Postgres connection.
        var connStr = _fx.ConnectionString;
        var services = new ServiceCollection()
            .AddDbContext<CmsDbContext>(opt => opt.UseNpgsql(connStr))
            .BuildServiceProvider();

        // SeedContext requires AppDbContext but the CMS seeders only touch
        // CmsDbContext via ctx.Services.GetRequiredService<CmsDbContext>().
        // We pass null! for the Db slot since the CMS seeders do not read
        // it (verified by inspection of CmsReferenceDataSeeder + CmsV1DevSeeder).
        await using (var scope = services.CreateAsyncScope())
        {
            var seedCtx = new SeedContext(
                Db: null!,
                Services: scope.ServiceProvider,
                Size: DatasetSize.Small,
                Env: new TestHostEnv(),
                Logger: NullLogger.Instance);

            var refSeeder = new CmsReferenceDataSeeder();
            await refSeeder.ApplyAsync(seedCtx, ct);
            var devSeeder = new CmsV1DevSeeder();
            await devSeeder.ApplyAsync(seedCtx, ct);
        }
    }

    [Fact]
    public async Task Seeder_populates_all_5_entity_kinds_with_bilingual_coverage()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        await SeedAsync(ct);

        await using var verify = _fx.NewContext();
        (await verify.BannerSlots.CountAsync(ct)).Should().BeGreaterThan(0);
        (await verify.FeaturedSections.CountAsync(ct)).Should().BeGreaterThan(0);
        (await verify.FaqEntries.CountAsync(ct)).Should().BeGreaterThan(0);
        (await verify.BlogArticles.CountAsync(ct)).Should().BeGreaterThan(0);
        (await verify.LegalPageVersions.CountAsync(ct)).Should().BeGreaterThan(0);

        // Bilingual: at least one banner has both ar + en headlines.
        var bilingualBanner = await verify.BannerSlots
            .AnyAsync(b => b.HeadlineAr != null && b.HeadlineEn != null, ct);
        bilingualBanner.Should().BeTrue();

        // Per-(kind, market) legal page version-history has 2 versions per pair.
        var pairs = await verify.LegalPageVersions
            .GroupBy(l => new { l.LegalPageKindWire, l.MarketCode })
            .Select(g => g.Count())
            .ToListAsync(ct);
        pairs.Should().NotBeEmpty();
        pairs.Should().AllSatisfy(c => c.Should().BeGreaterOrEqualTo(2));

        // Some legal versions are live, some superseded.
        var liveCount = await verify.LegalPageVersions
            .CountAsync(l => l.StateWire == "live", ct);
        var supersededCount = await verify.LegalPageVersions
            .CountAsync(l => l.StateWire == "superseded", ct);
        liveCount.Should().BeGreaterThan(0);
        supersededCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Seeder_is_idempotent_across_runs()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        await SeedAsync(ct);

        await using var firstRun = _fx.NewContext();
        var firstBanner = await firstRun.BannerSlots.CountAsync(ct);
        var firstFeatured = await firstRun.FeaturedSections.CountAsync(ct);
        var firstFaq = await firstRun.FaqEntries.CountAsync(ct);
        var firstBlog = await firstRun.BlogArticles.CountAsync(ct);
        var firstLegal = await firstRun.LegalPageVersions.CountAsync(ct);

        // Run a second time without resetting — must be a no-op per kind.
        await SeedAsync(ct);

        await using var secondRun = _fx.NewContext();
        (await secondRun.BannerSlots.CountAsync(ct)).Should().Be(firstBanner);
        (await secondRun.FeaturedSections.CountAsync(ct)).Should().Be(firstFeatured);
        (await secondRun.FaqEntries.CountAsync(ct)).Should().Be(firstFaq);
        (await secondRun.BlogArticles.CountAsync(ct)).Should().Be(firstBlog);
        (await secondRun.LegalPageVersions.CountAsync(ct)).Should().Be(firstLegal);
    }
}
