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

    /// <summary>Test-only — wipes all buckets so unit / contract tests start clean.</summary>
    public void ResetAll() => _buckets.Clear();

    private bool TryAcquire(string key, int capacity, TimeSpan window)
    {
        var nowUtc = _time.GetUtcNow();
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        lock (bucket.Gate)
        {
            // Drop entries older than the window — the standard "sliding count"
            // approach. Cheap when traffic is bursty + small windows.
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

    private sealed class Bucket
    {
        public readonly object Gate = new();
        public readonly Queue<DateTimeOffset> Timestamps = new();
    }
}
