using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Pricing.Primitives.Caches;

public sealed class TaxRateCache(IServiceScopeFactory scopeFactory, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly object LookupLock = new();

    public async Task<TaxRateSnapshot?> GetAsync(string marketCode, string kind, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedKind = kind.Trim().ToLowerInvariant();
        var cacheKey = $"pricing.tax_rate:{normalizedMarket}:{normalizedKind}";

        if (cache.TryGetValue<TaxRateSnapshot>(cacheKey, out var cached) && cached is not null
            && IsEffective(cached, effectiveAt))
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
        lock (LookupLock)
        {
            cache.Set(cacheKey, snapshot, Ttl);
        }
        return snapshot;
    }

    public void Invalidate(string marketCode, string kind)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedKind = kind.Trim().ToLowerInvariant();
        cache.Remove($"pricing.tax_rate:{normalizedMarket}:{normalizedKind}");
    }

    public void InvalidateAll()
    {
        // IMemoryCache has no built-in "clear" — compact fully.
        if (cache is MemoryCache memCache)
        {
            memCache.Compact(1.0);
        }
    }

    private static bool IsEffective(TaxRateSnapshot _, DateTimeOffset __) => true;
}
