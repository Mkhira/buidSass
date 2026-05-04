using System.Collections.Concurrent;

namespace BackendApi.Modules.B2B.RateLimit;

/// <summary>
/// Spec 021 FR-045 — per-customer + per-company sliding-window rate limiter for
/// quote-request endpoints (<c>POST /api/customer/quotes/from-cart</c> and
/// <c>POST /api/customer/quotes/from-product</c>).
///
/// Same architecture as the Reviews module's <c>ReviewRateLimiter</c>: an in-process
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of timestamp queues keyed by
/// (bucket, actor). Single-instance only — cross-replica fanout is a Phase 1.5
/// concern. The customer cap and company cap come from
/// <c>quote_market_schemas.rate_limit_per_customer_per_hour</c> and
/// <c>rate_limit_per_company_per_hour</c> respectively (research §R8 default 10/50).
///
/// Distinct buckets per scope:
/// <list type="bullet">
///   <item><c>customer:{guid}</c> — caps a single customer's hourly quote-creation rate.</item>
///   <item><c>company:{guid}</c> — caps a single company's aggregate hourly rate
///         across all of its buyers. T061a integration test verifies that 50
///         requests from 50 different buyers in one company still trip this bucket.</item>
/// </list>
///
/// The <see cref="TryAcquireCustomer"/> / <see cref="TryAcquireCompany"/> separation
/// lets the handler check both buckets in sequence: customer first, then company.
/// Both must succeed for the request to proceed; failing either returns
/// <c>429 quote.rate_limit_exceeded</c>.
/// </summary>
public sealed class QuoteRequestRateLimiter
{
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public QuoteRequestRateLimiter(TimeProvider time)
    {
        _time = time;
    }

    public bool TryAcquireCustomer(Guid customerId, int capacity, TimeSpan window) =>
        TryAcquire($"customer:{customerId:N}", capacity, window);

    public bool TryAcquireCompany(Guid companyId, int capacity, TimeSpan window) =>
        TryAcquire($"company:{companyId:N}", capacity, window);

    /// <summary>
    /// Refunds the most-recent customer-bucket token. Called when a downstream
    /// gate (per-company rate limit, write-time validation) rejects a request
    /// AFTER the per-customer token was acquired — without the refund the customer
    /// would be charged a slot for a request that never proceeded
    /// (CodeRabbit Round 1).
    /// </summary>
    public void ReleaseCustomer(Guid customerId) =>
        Release($"customer:{customerId:N}");

    /// <summary>Test-only — wipes all buckets so unit / contract tests start clean.</summary>
    public void ResetAll() => _buckets.Clear();

    private bool TryAcquire(string key, int capacity, TimeSpan window)
    {
        var nowUtc = _time.GetUtcNow();
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        lock (bucket.Gate)
        {
            Trim(bucket, nowUtc, window);
            if (bucket.Timestamps.Count >= capacity)
            {
                // Bucket may now be empty after Trim — best-effort drop the key
                // to bound _buckets memory under churn (CodeRabbit Round 1).
                MaybeEvict(key, bucket);
                return false;
            }
            bucket.Timestamps.Enqueue(nowUtc);
            return true;
        }
    }

    private void Release(string key)
    {
        if (!_buckets.TryGetValue(key, out var bucket)) return;
        lock (bucket.Gate)
        {
            // Drop the latest enqueue — best-effort; if the queue is already
            // empty (window rolled past in the meantime), the release is a no-op.
            if (bucket.Timestamps.Count > 0)
            {
                var preserved = new Queue<DateTimeOffset>(bucket.Timestamps.Count - 1);
                var snapshot = bucket.Timestamps.ToArray();
                for (var i = 0; i < snapshot.Length - 1; i++)
                {
                    preserved.Enqueue(snapshot[i]);
                }
                bucket.Timestamps.Clear();
                foreach (var ts in preserved)
                {
                    bucket.Timestamps.Enqueue(ts);
                }
            }
            MaybeEvict(key, bucket);
        }
    }

    private static void Trim(Bucket bucket, DateTimeOffset nowUtc, TimeSpan window)
    {
        // Drop entries older than the window — standard sliding-count approach.
        while (bucket.Timestamps.TryPeek(out var oldest) && oldest + window <= nowUtc)
        {
            bucket.Timestamps.Dequeue();
        }
    }

    private void MaybeEvict(string key, Bucket bucket)
    {
        // Caller MUST hold bucket.Gate. Empty queue → drop the key so the
        // dictionary doesn't grow unbounded as new tenant/customer ids appear.
        // Concurrent acquires that race past the eviction simply re-create the
        // bucket via GetOrAdd — correctness is preserved.
        if (bucket.Timestamps.Count == 0)
        {
            _buckets.TryRemove(KeyValuePair.Create(key, bucket));
        }
    }

    private sealed class Bucket
    {
        public readonly object Gate = new();
        public readonly Queue<DateTimeOffset> Timestamps = new();
    }
}
