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

    public async Task<TaxRateSnapshot?> GetAsync(string marketCode, string kind, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedKind = kind.Trim().ToLowerInvariant();
        var cacheKey = KeyFor(normalizedMarket, normalizedKind);

        // Cache is only safe for "now-ish" queries. If the caller passes a historical effectiveAt,
        // skip the cache and resolve against the effective-window directly (spec 013 returns rely on this).
        var cacheIsSafe = effectiveAt >= DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        if (cacheIsSafe && cache.TryGetValue<TaxRateSnapshot>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
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
        if (cacheIsSafe)
        {
            cache.Set(cacheKey, snapshot, Ttl);
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
}
