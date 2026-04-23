using System.Text.Json;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Layers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Pricing.Primitives.Caches;

public sealed class PromotionCache(IServiceScopeFactory scopeFactory, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const string CacheKey = "pricing.promotions.all";

    public async Task<IReadOnlyList<PromotionSnapshot>> GetActivePromotionsAsync(string marketCode, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<IReadOnlyList<PromotionSnapshot>>(CacheKey, out var cached) && cached is not null)
        {
            return cached.Where(p => p.MarketCodes.Contains(marketCode, StringComparer.OrdinalIgnoreCase)).ToArray();
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        var rows = await db.Promotions
            .AsNoTracking()
            .Where(p => p.IsActive && p.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var snapshots = rows.Select(MapToSnapshot).ToArray();
        cache.Set(CacheKey, (IReadOnlyList<PromotionSnapshot>)snapshots, Ttl);
        return snapshots.Where(p => p.MarketCodes.Contains(marketCode, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    public void Invalidate() => cache.Remove(CacheKey);

    internal static PromotionSnapshot MapToSnapshot(Promotion p)
    {
        var config = string.IsNullOrWhiteSpace(p.ConfigJson)
            ? new JsonDocument?[] { null }[0]
            : JsonDocument.Parse(p.ConfigJson);

        int? percentBps = null;
        long? amountMinor = null;
        Guid? bogoQualifyingPid = null;
        Guid? bogoRewardPid = null;
        int? bogoQualifyQty = null;
        int? bogoRewardQty = null;
        int? bogoRewardPctBps = null;

        if (config is not null)
        {
            var root = config.RootElement;
            if (root.TryGetProperty("percentBps", out var pctEl) && pctEl.TryGetInt32(out var pct))
            {
                percentBps = pct;
            }
            if (root.TryGetProperty("amountMinor", out var amtEl) && amtEl.TryGetInt64(out var amt))
            {
                amountMinor = amt;
            }
            if (root.TryGetProperty("qualifyingProductId", out var qpEl) && Guid.TryParse(qpEl.GetString(), out var qp))
            {
                bogoQualifyingPid = qp;
            }
            if (root.TryGetProperty("rewardProductId", out var rpEl) && Guid.TryParse(rpEl.GetString(), out var rp))
            {
                bogoRewardPid = rp;
            }
            if (root.TryGetProperty("qualifyQty", out var qqEl) && qqEl.TryGetInt32(out var qq))
            {
                bogoQualifyQty = qq;
            }
            if (root.TryGetProperty("rewardQty", out var rqEl) && rqEl.TryGetInt32(out var rq))
            {
                bogoRewardQty = rq;
            }
            if (root.TryGetProperty("rewardPercentBps", out var rpbEl) && rpbEl.TryGetInt32(out var rpb))
            {
                bogoRewardPctBps = rpb;
            }
        }

        return new PromotionSnapshot(
            Id: p.Id,
            Kind: p.Kind,
            Priority: p.Priority,
            IsActive: p.IsActive,
            StartsAt: p.StartsAt,
            EndsAt: p.EndsAt,
            MarketCodes: p.MarketCodes,
            AppliesToProductIds: p.AppliesToProductIds?.Length > 0 ? p.AppliesToProductIds : null,
            AppliesToCategoryIds: p.AppliesToCategoryIds?.Length > 0 ? p.AppliesToCategoryIds : null,
            PercentBps: percentBps,
            AmountMinor: amountMinor,
            BogoQualifyingProductId: bogoQualifyingPid,
            BogoRewardProductId: bogoRewardPid,
            BogoQualifyQty: bogoQualifyQty,
            BogoRewardQty: bogoRewardQty,
            BogoRewardPercentBps: bogoRewardPctBps);
    }
}
