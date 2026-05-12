using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BackendApi.Modules.Pricing.Internal;

/// <summary>
/// Spec 007-b T141 — implementation of <see cref="ICheckoutGraceWindowProvider"/>
/// for spec 010 checkout's ad-hoc grace-window inspection. The hot path on
/// checkout consumes the grace value from the deactivation-event payload
/// (research §R4); this provider is for the rare lookup case.
///
/// Reads from <c>pricing.commercial_thresholds</c> with a 30-second in-process
/// cache keyed by <c>(rule_kind, market_code)</c> (data-model §10).
/// </summary>
public sealed class CheckoutGraceWindowProvider : ICheckoutGraceWindowProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly PricingDbContext _db;
    private readonly IMemoryCache _cache;

    public CheckoutGraceWindowProvider(PricingDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<int?> GetGraceSecondsAsync(string ruleKind, Guid ruleId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ruleKind))
        {
            return null;
        }

        // Resolve which market the rule belongs to. Coupon / Promotion have a
        // MarketCodes[] column. For multi-market rules we return the smallest
        // configured grace (most-restrictive wins, matching the gate).
        string[] marketCodes;
        switch (ruleKind.Trim().ToLowerInvariant())
        {
            case "coupon":
                var coupon = await _db.Coupons.AsNoTracking()
                    .Where(c => c.Id == ruleId)
                    .Select(c => c.MarketCodes)
                    .FirstOrDefaultAsync(ct);
                if (coupon is null) return null;
                marketCodes = coupon;
                return await ResolveAsync(marketCodes, isCoupon: true, ct);

            case "promotion":
                var promo = await _db.Promotions.AsNoTracking()
                    .Where(p => p.Id == ruleId)
                    .Select(p => p.MarketCodes)
                    .FirstOrDefaultAsync(ct);
                if (promo is null) return null;
                marketCodes = promo;
                return await ResolveAsync(marketCodes, isCoupon: false, ct);

            default:
                return null;
        }
    }

    private async Task<int?> ResolveAsync(string[] marketCodes, bool isCoupon, CancellationToken ct)
    {
        if (marketCodes.Length == 0) return null;
        var keys = marketCodes
            .Select(NormaliseMarket)
            .Where(m => m is not null)
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0) return null;

        int? smallest = null;
        foreach (var key in keys)
        {
            var cacheKey = $"pricing.grace.{(isCoupon ? "coupon" : "promo")}.{key}";
            var seconds = await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                var row = await _db.CommercialThresholds.AsNoTracking()
                    .Where(t => t.MarketCode == key)
                    .Select(t => isCoupon ? t.CouponInFlightGraceSeconds : t.PromotionInFlightGraceSeconds)
                    .FirstOrDefaultAsync(ct);
                return row > 0 ? row : 1800;
            });
            if (smallest is null || seconds < smallest) smallest = seconds;
        }
        return smallest;
    }

    private static string? NormaliseMarket(string m) => m?.Trim().ToUpperInvariant() switch
    {
        "SA" or "KSA" => "SA",
        "EG" => "EG",
        _ => null,
    };
}
