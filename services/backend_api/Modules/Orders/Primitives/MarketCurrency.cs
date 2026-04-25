namespace BackendApi.Modules.Orders.Primitives;

/// <summary>
/// Per-market default currency. Until a richer market-config service ships, this is the
/// single source of truth used by quotation conversion (no explanation row to read currency
/// from). Production checkouts already carry currency via the Pricing explanation, so this
/// map is only consulted by quote-originated orders.
/// </summary>
public static class MarketCurrency
{
    private const string DefaultCurrency = "SAR";

    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KSA"] = "SAR",
            ["EG"] = "EGP",
        };

    /// <summary>
    /// Resolve a market code → currency. CR review round 2 (Major): fail closed on unknown
    /// non-empty codes so a typo can't silently mint orders in the wrong currency. Empty /
    /// whitespace input still falls back to the platform default (KSA = SAR), matching the
    /// "no market specified yet" pre-checkout state.
    /// </summary>
    public static string Resolve(string marketCode)
    {
        var normalized = (marketCode ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return DefaultCurrency;
        }
        if (Map.TryGetValue(normalized, out var currency))
        {
            return currency;
        }
        throw new ArgumentOutOfRangeException(
            nameof(marketCode),
            normalized,
            "Unsupported market code for currency resolution.");
    }
}
