using BackendApi.Modules.Pricing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Pricing.Primitives.Caches;

public sealed class TaxRateCache(IServiceScopeFactory scopeFactory, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly string[] KnownMarkets = ["ksa", "eg"];
    private static readonly string[] KnownKinds = ["vat"];

    private static string KeyFor(string market, string kind) => $"pricing.tax_rate:{market}:{kind}";

    private sealed record CacheEntry(
        TaxRateSnapshot Snapshot,
        DateTimeOffset EffectiveFrom,
        DateTimeOffset? EffectiveTo);

    public async Task<TaxRateSnapshot?> GetAsync(string marketCode, string kind, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedKind = kind.Trim().ToLowerInvariant();
        var cacheKey = KeyFor(normalizedMarket, normalizedKind);

        if (cache.TryGetValue<CacheEntry>(cacheKey, out var cached) && cached is not null
            && IsEffective(cached, effectiveAt))
        {
            return cached.Snapshot;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var row = await db.TaxRates
            .AsNoTracking()
            .Where(t => t.MarketCode == normalizedMarket
                && t.Kind == normalizedKind
                && t.EffectiveFrom <= effectiveAt
                && (t.EffectiveTo == null || effectiveAt < t.EffectiveTo))
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var snapshot = new TaxRateSnapshot(row.Id, row.MarketCode, row.Kind, row.RateBps);
        // Only cache when the effectiveAt is in the recent window AND fits inside the row's validity.
        // Historical re-pricing (spec 013 returns) + scheduled-rate previews bypass the cache so callers
        // always get the rate that is actually effective at the requested moment in time.
        var now = DateTimeOffset.UtcNow;
        var isRecent = Math.Abs((effectiveAt - now).TotalMinutes) <= 1;
        if (isRecent)
        {
            cache.Set(cacheKey, new CacheEntry(snapshot, row.EffectiveFrom, row.EffectiveTo), Ttl);
        }
        return snapshot;
    }

    public void Invalidate(string marketCode, string kind)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedKind = kind.Trim().ToLowerInvariant();
        cache.Remove(KeyFor(normalizedMarket, normalizedKind));
    }

    /// <summary>
    /// Invalidate every known market × kind combo. Scoped to pricing keys only —
    /// MUST NOT touch other modules that share the singleton IMemoryCache.
    /// </summary>
    public void InvalidateAll()
    {
        foreach (var market in KnownMarkets)
        {
            foreach (var kind in KnownKinds)
            {
                cache.Remove(KeyFor(market, kind));
            }
        }
    }

    private static bool IsEffective(CacheEntry entry, DateTimeOffset at) =>
        entry.EffectiveFrom <= at && (entry.EffectiveTo is null || at < entry.EffectiveTo);
}
