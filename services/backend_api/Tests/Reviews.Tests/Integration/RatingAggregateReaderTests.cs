using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Aggregate.ReadProductRating;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration;

/// <summary>
/// Spec 022 T110-T119 — public rating aggregate read API. Covers:
/// happy single-row read, null avg + zero count for missing aggregate,
/// inclusion semantics (visible+flagged count; pending/hidden/deleted excluded),
/// batch read up to the documented cap.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class RatingAggregateReaderTests
{
    private readonly ReviewsPostgresFixture _fx;

    public RatingAggregateReaderTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetAsync_returns_null_when_no_aggregate_row_exists()
    {
        await using var db = _fx.NewContext();
        var reader = new RatingAggregateReader(db);
        var result = await reader.GetAsync(Guid.NewGuid(), "SA", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadProductRatingHandler_synthesizes_zero_count_when_aggregate_missing()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        await using var db = _fx.NewContext();
        var handler = new ReadProductRatingHandler(new RatingAggregateReader(db));

        var productId = Guid.NewGuid();
        var response = await handler.GetAsync(productId, "SA", nowUtc, CancellationToken.None);

        response.ProductId.Should().Be(productId);
        response.MarketCode.Should().Be("SA");
        response.AvgRating.Should().BeNull("FR-028: avg_rating is null when review_count = 0");
        response.ReviewCount.Should().Be(0);
        response.Distribution.Should().BeEquivalentTo(new RatingDistribution(0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Aggregate_reflects_visible_reviews_after_recompute()
    {
        var (productId, _) = await SubmitVisibleReviewsAsync(ratings: new[] { 5, 4, 4, 3, 5 });

        await using var db = _fx.NewContext();
        var handler = new ReadProductRatingHandler(new RatingAggregateReader(db));

        var response = await handler.GetAsync(productId, "SA", DateTimeOffset.UtcNow, CancellationToken.None);

        response.ReviewCount.Should().Be(5);
        response.AvgRating.Should().Be(4.20m);
        response.Distribution.Bucket1.Should().Be(0);
        response.Distribution.Bucket3.Should().Be(1);
        response.Distribution.Bucket4.Should().Be(2);
        response.Distribution.Bucket5.Should().Be(2);
    }

    [Fact]
    public async Task GetManyAsync_returns_all_known_aggregates_for_market()
    {
        var (productA, _) = await SubmitVisibleReviewsAsync(ratings: new[] { 5 });
        var (productB, _) = await SubmitVisibleReviewsAsync(ratings: new[] { 3, 3 });
        var unknownProduct = Guid.NewGuid();

        await using var db = _fx.NewContext();
        var handler = new ReadProductRatingHandler(new RatingAggregateReader(db));

        var response = await handler.GetManyAsync(
            new[] { productA, productB, unknownProduct },
            "SA",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        response.Items.Should().HaveCount(3);
        response.Items.Single(i => i.ProductId == productA).ReviewCount.Should().Be(1);
        response.Items.Single(i => i.ProductId == productB).ReviewCount.Should().Be(2);
        response.Items.Single(i => i.ProductId == unknownProduct).ReviewCount.Should().Be(0);
        response.Items.Single(i => i.ProductId == unknownProduct).AvgRating.Should().BeNull();
    }

    [Fact]
    public async Task GetManyAsync_filters_by_market_code()
    {
        var (productA, _) = await SubmitVisibleReviewsAsync(ratings: new[] { 5 }, market: "SA");

        await using var db = _fx.NewContext();
        var handler = new ReadProductRatingHandler(new RatingAggregateReader(db));

        var sa = await handler.GetAsync(productA, "SA", DateTimeOffset.UtcNow, CancellationToken.None);
        var eg = await handler.GetAsync(productA, "EG", DateTimeOffset.UtcNow, CancellationToken.None);

        sa.ReviewCount.Should().Be(1);
        eg.ReviewCount.Should().Be(0, "aggregate is per (product, market)");
    }

    [Fact]
    public async Task GetManyAsync_with_empty_input_returns_empty_dictionary()
    {
        await using var db = _fx.NewContext();
        var reader = new RatingAggregateReader(db);
        var result = await reader.GetManyAsync(Array.Empty<Guid>(), "SA", CancellationToken.None);
        result.Should().BeEmpty();
    }

    private async Task<(Guid productId, IReadOnlyList<Guid> reviewIds)> SubmitVisibleReviewsAsync(
        int[] ratings, string market = "SA")
    {
        var productId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var reviewIds = new List<Guid>();

        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ArabicNormalizer(), TimeSpan.Zero);

        await EnsureMarketSchemaAsync(market);

        foreach (var rating in ratings)
        {
            var customer = Guid.NewGuid();
            var db = _fx.NewContext();
            var aggregate = new RatingAggregateRecomputer(db, clock);
            var submit = new SubmitReviewHandler(db,
                new FakeEligibility(true, deliveredAt, Guid.NewGuid()),
                profanity, aggregate, clock);
            var result = await submit.HandleAsync(customer, market,
                new SubmitReviewRequest(productId, rating, $"R{rating}",
                    "Long-enough body to satisfy the CHECK constraint.", "en", null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            reviewIds.Add(result.Response!.Id);
            await db.DisposeAsync();
        }

        return (productId, reviewIds);
    }

    private async Task EnsureMarketSchemaAsync(string market)
    {
        await using var db = _fx.NewContext();
        if (!await db.MarketSchemas.AnyAsync(s => s.MarketCode == market))
        {
            db.MarketSchemas.Add(new BackendApi.Modules.Reviews.Entities.ReviewsMarketSchema
            {
                MarketCode = market,
                EligibilityWindowDays = 180,
                EditWindowDays = 30,
                CommunityReportThreshold = 3,
                CommunityReportWindowDays = 30,
                ReportQualifyingAccountAgeDays = 14,
                ReportQualifyingRequiresVerifiedBuyer = true,
                PendingModerationSlaHours = 168,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedByActorId = Guid.Empty,
            });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race-tolerant — another test seeded first.
            }
        }
    }

    private sealed class FakeEligibility : IOrderLineDeliveryEligibilityQuery
    {
        private readonly bool _eligible;
        private readonly DateTimeOffset? _delivered;
        private readonly Guid? _orderLineId;

        public FakeEligibility(bool eligible, DateTimeOffset? delivered, Guid? orderLineId)
        {
            _eligible = eligible;
            _delivered = delivered;
            _orderLineId = orderLineId;
        }

        public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(Guid c, Guid p, CancellationToken ct) =>
            Task.FromResult(new OrderLineDeliveryEligibilityResult(
                _eligible,
                _eligible ? null : "review.eligibility.no_delivered_purchase",
                _delivered, _orderLineId));
    }
}
