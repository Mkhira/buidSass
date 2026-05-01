using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.PolicyAdmin.UpsertWordlistTerm;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Wordlist;

/// <summary>
/// Spec 022 T072 — admin upserts a new term via the policy-admin handler;
/// the next submission containing that term lands in
/// <see cref="ReviewState.PendingModeration"/>. Verifies the in-process
/// <see cref="ProfanityFilter"/> cache is invalidated by the upsert path
/// (research §R13). Spec text mentions a 60s SLA; this test asserts the
/// stronger property — invalidation is immediate on the same instance.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class WordlistRefreshTests
{
    private readonly ReviewsPostgresFixture _fx;

    public WordlistRefreshTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Upserting_a_new_term_trips_subsequent_submission_immediately()
    {
        // Use a unique market-specific term so it can't collide with prior tests.
        const string newTerm = "uniquetestterm" + "abc123";
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var normalizer = new ArabicNormalizer();
        var filter = new ProfanityFilter(BuildScopeFactory(), normalizer, TimeSpan.FromMinutes(5));

        // Warm the cache for SA, then verify the term is NOT yet in the wordlist.
        var beforeUpsert = filter.Evaluate("SA",
            $"this body contains the {newTerm} substring");
        beforeUpsert.Tripped.Should().BeFalse("term not yet in the wordlist");

        // Admin upserts the new term.
        await using (var upsertDb = _fx.NewContext())
        {
            var upsertHandler = new UpsertWordlistTermHandler(upsertDb, normalizer, filter, clock);
            var upsertResult = await upsertHandler.HandleAsync(
                Guid.NewGuid(),
                new UpsertWordlistTermRequest("SA", newTerm, null),
                CancellationToken.None);
            upsertResult.IsSuccess.Should().BeTrue();
        }

        // Next submission with that term must trip the filter end-to-end.
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var submitDb = _fx.NewContext();
        var aggregate = new RatingAggregateRecomputer(submitDb, clock);
        var eligibility = new FakeOrderLineDeliveryEligibilityQuery(
            eligible: true,
            deliveredAt: clock.GetUtcNow().AddDays(-2),
            orderLineId: Guid.NewGuid());
        var submit = new SubmitReviewHandler(submitDb, eligibility, filter, aggregate, clock);

        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 5, "Headline",
                $"Body contains {newTerm} now", "en", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        result.Response.PendingReview.Should().BeTrue();

        await using var verify = _fx.NewContext();
        var review = await verify.Reviews.AsNoTracking().FirstAsync(r => r.Id == result.Response.Id);
        review.FilterTripTerms.Should().Contain(newTerm.ToLowerInvariant());
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
