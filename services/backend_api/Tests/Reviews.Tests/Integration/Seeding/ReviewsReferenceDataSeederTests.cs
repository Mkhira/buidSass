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
/// Spec 022 T052 — running the reference seeder twice converges to the same
/// row counts (idempotency); both KSA + EG market schemas are present with
/// the documented defaults.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class ReviewsReferenceDataSeederTests
{
    private readonly ReviewsPostgresFixture _fx;

    public ReviewsReferenceDataSeederTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Seeder_idempotent_across_two_runs()
    {
        var seeder = new ReviewsReferenceDataSeeder();
        var ctx = MakeSeedContext();

        await seeder.ApplyAsync(ctx, CancellationToken.None);
        var (firstSchemas, firstWordlists) = await CountAsync();

        await seeder.ApplyAsync(ctx, CancellationToken.None);
        var (secondSchemas, secondWordlists) = await CountAsync();

        secondSchemas.Should().Be(firstSchemas);
        secondWordlists.Should().Be(firstWordlists);
        firstSchemas.Should().Be(2, "KSA + EG market schemas seeded");
        firstWordlists.Should().BeGreaterThanOrEqualTo(4, "seed wordlist contains at least 4 entries");
    }

    [Fact]
    public async Task Seeder_writes_documented_default_policy_values()
    {
        var seeder = new ReviewsReferenceDataSeeder();
        await seeder.ApplyAsync(MakeSeedContext(), CancellationToken.None);

        await using var db = _fx.NewContext();
        foreach (var marketCode in new[] { "SA", "EG" })
        {
            var row = await db.MarketSchemas.AsNoTracking()
                .FirstAsync(s => s.MarketCode == marketCode);
            row.EligibilityWindowDays.Should().Be(180);
            row.EditWindowDays.Should().Be(30);
            row.CommunityReportThreshold.Should().Be(3);
            row.ReportQualifyingAccountAgeDays.Should().Be(14);
            row.ReportQualifyingRequiresVerifiedBuyer.Should().BeTrue();
        }
    }

    private SeedContext MakeSeedContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        var provider = services.BuildServiceProvider();
        return new SeedContext(
            Db: null!,
            Services: provider,
            Size: DatasetSize.Small,
            Env: new TestHostEnv(),
            Logger: NullLogger.Instance);
    }

    private async Task<(int schemas, int wordlists)> CountAsync()
    {
        await using var db = _fx.NewContext();
        return (
            await db.MarketSchemas.CountAsync(),
            await db.Wordlists.CountAsync());
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
