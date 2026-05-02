using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Implements the FR-021a banner-slot capacity rule. Counts <c>live</c> banners
/// per <c>(slot_kind, market_code)</c> including <c>*</c>-scoped banners
/// against every per-market cap they appear in (research §R6).
/// </summary>
public sealed class BannerCapacityCalculator
{
    /// <summary>
    /// Returns the count of currently-<c>live</c> banners that count toward the
    /// cap for <paramref name="marketCode"/> + <paramref name="slotKind"/> at
    /// <paramref name="nowUtc"/> (window-aware).
    /// </summary>
    /// <remarks>
    /// `*`-scoped banners count against EVERY per-market cap (R6). When
    /// <paramref name="marketCode"/> is itself <c>*</c>, only `*`-scoped
    /// banners count.
    /// </remarks>
    public async Task<int> CountLiveAsync(
        IQueryable<BannerSlot> banners,
        BannerSlotKind slotKind,
        string marketCode,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var slotKindWire = slotKind.ToWire();
        var liveWire = ContentLifecycleState.Live.ToWire();

        var query = banners
            .Where(b => b.SlotKindWire == slotKindWire)
            .Where(b => b.StateWire == liveWire)
            .Where(b => b.ScheduledStartUtc == null || b.ScheduledStartUtc <= nowUtc)
            .Where(b => b.ScheduledEndUtc == null || b.ScheduledEndUtc > nowUtc);

        if (marketCode == "*")
        {
            query = query.Where(b => b.MarketCode == "*");
        }
        else
        {
            query = query.Where(b => b.MarketCode == marketCode || b.MarketCode == "*");
        }

        return await query.CountAsync(ct);
    }

    /// <summary>
    /// Throws <see cref="BannerSlotCapacityExceededException"/> if publishing
    /// one more banner into the slot would exceed the per-market cap.
    /// </summary>
    public async Task AssertCanPublishAsync(
        IQueryable<BannerSlot> banners,
        BannerSlotKind slotKind,
        string marketCode,
        CmsMarketPolicy marketPolicy,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var current = await CountLiveAsync(banners, slotKind, marketCode, nowUtc, ct);
        if (current + 1 > marketPolicy.BannerMaxLivePerSlot)
        {
            throw new BannerSlotCapacityExceededException(slotKind, marketCode, current, marketPolicy.BannerMaxLivePerSlot);
        }
    }
}

/// <summary>Thrown when a publish would exceed <see cref="CmsMarketPolicy.BannerMaxLivePerSlot"/>.</summary>
public sealed class BannerSlotCapacityExceededException(
    BannerSlotKind slotKind,
    string marketCode,
    int currentLiveCount,
    int cap)
    : Exception($"Banner slot capacity exceeded for ({slotKind.ToWire()}, {marketCode}): {currentLiveCount}/{cap}.")
{
    public BannerSlotKind SlotKind { get; } = slotKind;
    public string MarketCode { get; } = marketCode;
    public int CurrentLiveCount { get; } = currentLiveCount;
    public int Cap { get; } = cap;
    public string ReasonCode => CmsReasonCode.BannerSlotCapacityExceeded;
}
