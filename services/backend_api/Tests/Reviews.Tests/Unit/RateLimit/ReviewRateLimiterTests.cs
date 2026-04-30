using BackendApi.Modules.Reviews.RateLimit;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Reviews.Tests.Unit.RateLimit;

/// <summary>
/// Spec 022 T061 / T081 / T093 — token-bucket rate limiter for the
/// customer + moderator endpoints. Tests the rolling-window semantics with
/// FakeTimeProvider; the per-endpoint integration is covered by the contract
/// suites.
/// </summary>
public sealed class ReviewRateLimiterTests
{
    [Fact]
    public void First_capacity_acquires_succeed_then_next_one_fails()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new ReviewRateLimiter(clock);
        var actor = Guid.NewGuid();

        for (var i = 0; i < ReviewRateLimits.CustomerCapacityPerHour; i++)
        {
            limiter.TryAcquire(ReviewRateLimits.Submission, actor,
                ReviewRateLimits.CustomerCapacityPerHour, ReviewRateLimits.Window).Should().BeTrue();
        }

        limiter.TryAcquire(ReviewRateLimits.Submission, actor,
            ReviewRateLimits.CustomerCapacityPerHour, ReviewRateLimits.Window).Should().BeFalse();
    }

    [Fact]
    public void Window_advance_drops_old_entries_and_reopens_capacity()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new ReviewRateLimiter(clock);
        var actor = Guid.NewGuid();

        for (var i = 0; i < ReviewRateLimits.CustomerCapacityPerHour; i++)
        {
            limiter.TryAcquire(ReviewRateLimits.Submission, actor, 5, ReviewRateLimits.Window).Should().BeTrue();
        }
        limiter.TryAcquire(ReviewRateLimits.Submission, actor, 5, ReviewRateLimits.Window).Should().BeFalse();

        // Roll the wall clock forward by exactly the window — entries fall out.
        clock.Advance(ReviewRateLimits.Window + TimeSpan.FromSeconds(1));

        limiter.TryAcquire(ReviewRateLimits.Submission, actor, 5, ReviewRateLimits.Window).Should().BeTrue();
    }

    [Fact]
    public void Buckets_are_keyed_per_actor_and_per_bucket_name()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new ReviewRateLimiter(clock);
        var actorA = Guid.NewGuid();
        var actorB = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire(ReviewRateLimits.Submission, actorA, 5, ReviewRateLimits.Window).Should().BeTrue();
        }
        // ActorA exhausted submission, but actorB has its own bucket and actorA still has the edit/report buckets.
        limiter.TryAcquire(ReviewRateLimits.Submission, actorA, 5, ReviewRateLimits.Window).Should().BeFalse();
        limiter.TryAcquire(ReviewRateLimits.Submission, actorB, 5, ReviewRateLimits.Window).Should().BeTrue();
        limiter.TryAcquire(ReviewRateLimits.Edit, actorA, 5, ReviewRateLimits.Window).Should().BeTrue();
        limiter.TryAcquire(ReviewRateLimits.Report, actorA, 5, ReviewRateLimits.Window).Should().BeTrue();
    }

    [Fact]
    public void Moderator_bucket_uses_higher_capacity()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new ReviewRateLimiter(clock);
        var actor = Guid.NewGuid();

        for (var i = 0; i < ReviewRateLimits.ModeratorCapacityPerHour; i++)
        {
            limiter.TryAcquire(ReviewRateLimits.ModerationDecision, actor,
                ReviewRateLimits.ModeratorCapacityPerHour, ReviewRateLimits.Window).Should().BeTrue();
        }
        limiter.TryAcquire(ReviewRateLimits.ModerationDecision, actor,
            ReviewRateLimits.ModeratorCapacityPerHour, ReviewRateLimits.Window).Should().BeFalse();
    }

    [Fact]
    public void Reset_clears_a_single_actor_bucket()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new ReviewRateLimiter(clock);
        var actor = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire(ReviewRateLimits.Submission, actor, 5, ReviewRateLimits.Window);
        }
        limiter.TryAcquire(ReviewRateLimits.Submission, actor, 5, ReviewRateLimits.Window).Should().BeFalse();

        limiter.Reset(ReviewRateLimits.Submission, actor);
        limiter.TryAcquire(ReviewRateLimits.Submission, actor, 5, ReviewRateLimits.Window).Should().BeTrue();
    }
}
