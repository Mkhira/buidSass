using System.Collections.Concurrent;

namespace BackendApi.Modules.Reviews.RateLimit;

/// <summary>
/// In-process per-(actor, bucket) token bucket. Sized for the buckets in spec
/// 022's contract preamble: 5/hour for customer submission/edit/report
/// (separate buckets), 60/hour for admin moderation decisions. Cross-replica
/// fanout is a Phase 1.5 concern; this implementation is single-instance and
/// suffices for the V1 SLA.
///
/// <para>The limiter exposes <see cref="TryAcquire"/> rather than throwing —
/// the caller's endpoint filter turns the failure into a 429. The
/// <c>FakeTimeProvider</c> can fast-forward an hour to test the rolling window.</para>
/// </summary>
public sealed class ReviewRateLimiter
{
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public ReviewRateLimiter(TimeProvider time) => _time = time;

    /// <summary>
    /// Tries to consume one token from the named bucket for the actor.
    /// Returns <see langword="true"/> on success, <see langword="false"/> when
    /// the bucket is empty for the current rolling window.
    /// </summary>
    public bool TryAcquire(string bucketName, Guid actorId, int capacity, TimeSpan window)
    {
        var key = $"{bucketName}:{actorId:N}";
        var nowUtc = _time.GetUtcNow();

        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        lock (bucket.Gate)
        {
            // Drop entries older than the window.
            while (bucket.Timestamps.TryPeek(out var oldest) && oldest + window <= nowUtc)
            {
                bucket.Timestamps.Dequeue();
            }
            if (bucket.Timestamps.Count >= capacity)
            {
                return false;
            }
            bucket.Timestamps.Enqueue(nowUtc);
            return true;
        }
    }

    /// <summary>Wipes a single (bucket, actor) entry — test-only utility.</summary>
    public void Reset(string bucketName, Guid actorId) =>
        _buckets.TryRemove($"{bucketName}:{actorId:N}", out _);

    /// <summary>Wipes everything — test-only utility.</summary>
    public void ResetAll() => _buckets.Clear();

    private sealed class Bucket
    {
        public readonly object Gate = new();
        public readonly Queue<DateTimeOffset> Timestamps = new();
    }
}

/// <summary>Fixed bucket configuration constants — match contract preamble §1.</summary>
public static class ReviewRateLimits
{
    public const string Submission = "review.submit";
    public const string Edit = "review.edit";
    public const string Report = "review.report";
    public const string ModerationDecision = "reviews.moderation.decide";

    public const int CustomerCapacityPerHour = 5;
    public const int ModeratorCapacityPerHour = 60;

    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
}
