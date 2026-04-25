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

    public static string Resolve(string marketCode) =>
        Map.TryGetValue(marketCode?.Trim() ?? string.Empty, out var currency)
            ? currency
            : DefaultCurrency;
}
