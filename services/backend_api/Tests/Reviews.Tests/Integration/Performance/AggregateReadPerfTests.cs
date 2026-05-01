using System.Diagnostics;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Performance;

/// <summary>
/// Spec 022 T115 — single-row PK lookup performance for the aggregate read.
/// Plan §Performance documents <c>p95 ≤ 50 ms</c> for the single-row read.
/// Wall-clock perf in CI is noisy; this test runs a soak loop and asserts a
/// generous upper bound rather than a strict p95 — its job is to surface
/// gross regressions (e.g. accidental N+1, missing index), not to certify
/// the SLA. Strict perf assertions belong in a dedicated benchmark project
/// alongside spec 020's; this test is the in-suite tripwire.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class AggregateReadPerfTests
{
    private readonly ReviewsPostgresFixture _fx;

    public AggregateReadPerfTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Single_row_PK_lookup_completes_well_under_one_second_per_call()
    {
        var productId = Guid.NewGuid();
        await using (var seed = _fx.NewContext())
        {
            seed.RatingAggregates.Add(new ProductRatingAggregate
            {
                ProductId = productId,
                MarketCode = "SA",
                AvgRating = 4.5m,
                ReviewCount = 47,
                Distribution1 = 1,
                Distribution2 = 2,
                Distribution3 = 5,
                Distribution4 = 14,
                Distribution5 = 25,
                LastUpdatedUtc = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.NewContext();
        var reader = new RatingAggregateReader(db);

        // Warm-up — first call pays connection establishment + JIT.
        await reader.GetAsync(productId, "SA", CancellationToken.None);

        const int iterations = 100;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var aggregate = await reader.GetAsync(productId, "SA", CancellationToken.None);
            aggregate.Should().NotBeNull();
        }
        stopwatch.Stop();

        var avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        // Generous bound — strict 50ms p95 lives in a benchmark project.
        // 200ms/call covers slow CI runners (10x the SLA) while still
        // catching gross regressions like N+1 or missing PK index.
        avgMs.Should().BeLessThan(200,
            $"single-row PK lookup average over {iterations} reads must stay well below 200ms/call (observed {avgMs:F2}ms)");
    }

    [Fact]
    public async Task Batch_PK_lookup_with_100_ids_completes_under_two_seconds()
    {
        var marketCode = "SA";
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();

        await using (var seed = _fx.NewContext())
        {
            foreach (var id in ids)
            {
                seed.RatingAggregates.Add(new ProductRatingAggregate
                {
                    ProductId = id,
                    MarketCode = marketCode,
                    AvgRating = 4.0m,
                    ReviewCount = 10,
                    Distribution1 = 0, Distribution2 = 1, Distribution3 = 2,
                    Distribution4 = 3, Distribution5 = 4,
                    LastUpdatedUtc = DateTimeOffset.UtcNow,
                });
            }
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.NewContext();
        var reader = new RatingAggregateReader(db);

        // Warm-up.
        await reader.GetManyAsync(ids, marketCode, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        var rows = await reader.GetManyAsync(ids, marketCode, CancellationToken.None);
        stopwatch.Stop();

        rows.Should().HaveCount(100);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            $"batch read of 100 ids must complete well under 2s (observed {stopwatch.ElapsedMilliseconds}ms)");
    }
}
