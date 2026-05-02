using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Subscribers;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Contract;

/// <summary>
/// Spec 022 T102 — subscribers fan out via the in-process bus rather than
/// over HTTP, so the "contract" we verify is the publisher → subscriber
/// wiring contract: when <see cref="IRefundCompletedPublisher.PublishAsync"/>
/// is called, every registered <see cref="IRefundCompletedSubscriber"/>
/// (including <see cref="RefundCompletedHandler"/>) sees the event.
///
/// Uses <see cref="FakeRefundCompletedPublisher"/> to simulate spec 013's
/// publisher fanning out without coupling to its merge state.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class RefundCompletedSubscriberContractTests
{
    private readonly ReviewsPostgresFixture _fx;

    public RefundCompletedSubscriberContractTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task PublishAsync_via_FakeRefundCompletedPublisher_routes_to_RefundCompletedHandler()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var orderLineId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var reviewId = await SeedVisibleReviewAsync(customerId, productId, orderLineId);

        // Compose the subscriber + the fake publisher that fans out to it —
        // exactly the wiring shape spec 013 will produce when its publisher
        // implementation lands on `main`.
        await using var db = _fx.NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var subscriber = new RefundCompletedHandler(db, aggregate, clock);
        var publisher = new FakeRefundCompletedPublisher(new[] { (IRefundCompletedSubscriber)subscriber });

        await publisher.PublishAsync(
            new RefundCompletedEvent(orderLineId, customerId, clock.GetUtcNow(), Guid.NewGuid()),
            CancellationToken.None);

        await using var verify = _fx.NewContext();
        var review = await verify.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Hidden);
        review.TriggeredBy.Should().Be(ReviewTriggerKind.RefundEvent);
    }

    [Fact]
    public async Task Publisher_fans_out_to_multiple_subscribers_in_order()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var orderLineId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await SeedVisibleReviewAsync(customerId, productId, orderLineId);

        // Two subscribers — the real one + a synthetic recorder. Both must fire.
        await using var db = _fx.NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var realSubscriber = new RefundCompletedHandler(db, aggregate, clock);
        var recorder = new RecordingSubscriber();
        var publisher = new FakeRefundCompletedPublisher(
            new IRefundCompletedSubscriber[] { realSubscriber, recorder });

        var evt = new RefundCompletedEvent(orderLineId, customerId, clock.GetUtcNow(), Guid.NewGuid());
        await publisher.PublishAsync(evt, CancellationToken.None);

        recorder.ReceivedEvents.Should().HaveCount(1);
        recorder.ReceivedEvents[0].OrderLineId.Should().Be(orderLineId);
    }

    private async Task<Guid> SeedVisibleReviewAsync(Guid customerId, Guid productId, Guid orderLineId)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        await using var db = _fx.NewContext();
        var review = new BackendApi.Modules.Reviews.Entities.Review
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ProductId = productId,
            OrderLineId = orderLineId,
            MarketCode = "SA",
            Rating = 5,
            Headline = "T102 contract",
            Body = "Body content for the T102 subscriber contract test.",
            Locale = "en",
            MediaUrlsJson = "[]",
            State = ReviewState.Visible,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = customerId,
            TriggeredBy = ReviewTriggerKind.CustomerSubmission,
            CreatedAtUtc = nowUtc,
            DeliveredAtUtc = nowUtc.AddDays(-1),
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    private sealed class RecordingSubscriber : IRefundCompletedSubscriber
    {
        public List<RefundCompletedEvent> ReceivedEvents { get; } = new();

        public Task OnRefundCompletedAsync(RefundCompletedEvent evt, CancellationToken ct)
        {
            ReceivedEvents.Add(evt);
            return Task.CompletedTask;
        }
    }
}
