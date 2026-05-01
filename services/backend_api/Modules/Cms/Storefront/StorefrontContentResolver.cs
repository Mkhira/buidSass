using BackendApi.Modules.Cms.Primitives;

namespace BackendApi.Modules.Cms.Storefront;

/// <summary>
/// The single leak-safe gate for every storefront read endpoint per
/// research §R13. Composes the FR-017 filter (state == live AND scheduling
/// window open) AND the FR-021 / R8 two-tier sort (specific market first,
/// then `*`). MUST be the only path used by storefront slices.
/// </summary>
public sealed class StorefrontContentResolver
{
    private static readonly string LiveWire = ContentLifecycleState.Live.ToWire();

    /// <summary>
    /// Applies the live + scheduling-window filter and the two-tier
    /// market sort. Caller composes additional per-kind ordering after this
    /// (e.g., <c>priority_within_slot ASC, created_at_utc ASC</c>).
    /// </summary>
    public IOrderedQueryable<T> ApplyStorefrontFilter<T>(
        IQueryable<T> source,
        string marketCode,
        DateTimeOffset nowUtc) where T : class, ICmsContentRow
    {
        if (string.IsNullOrWhiteSpace(marketCode))
        {
            throw new ArgumentException("Market code is required.", nameof(marketCode));
        }

        // FR-017 filter — live state + market-or-`*` + window open.
        var filtered = source
            .Where(r => r.StateWire == LiveWire)
            .Where(r => r.MarketCode == marketCode || r.MarketCode == "*")
            .Where(r => r.ScheduledStartUtc == null || r.ScheduledStartUtc <= nowUtc)
            .Where(r => r.ScheduledEndUtc == null || r.ScheduledEndUtc > nowUtc);

        // R8 two-tier sort — specific-market first, then `*`. Caller composes
        // additional ThenBy keys for the per-kind sort (priority / order /
        // published_at_desc).
        return filtered.OrderBy(r => r.MarketCode == marketCode ? 0 : 1);
    }

    /// <summary>
    /// State + market-only overload for kinds that don't expose
    /// scheduled_start/end columns (FAQ entries, featured sections, blog
    /// articles). Their window check is implicit in <c>state=live</c>: the
    /// scheduled-publish worker only flips them to <c>live</c> at or after
    /// the scheduled time. This overload's WHERE clauses translate cleanly
    /// to SQL because <c>StateWire</c> + <c>MarketCode</c> are real columns
    /// on every entity, unlike the explicit-interface scheduled getters.
    /// </summary>
    public IOrderedQueryable<T> ApplyStorefrontFilterStateOnly<T>(
        IQueryable<T> source,
        string marketCode) where T : class, ICmsContentRow
    {
        if (string.IsNullOrWhiteSpace(marketCode))
        {
            throw new ArgumentException("Market code is required.", nameof(marketCode));
        }
        return source
            .Where(r => r.StateWire == LiveWire)
            .Where(r => r.MarketCode == marketCode || r.MarketCode == "*")
            .OrderBy(r => r.MarketCode == marketCode ? 0 : 1);
    }
}
