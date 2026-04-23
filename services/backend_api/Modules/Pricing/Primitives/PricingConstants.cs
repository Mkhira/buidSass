namespace BackendApi.Modules.Pricing.Primitives;

public static class PricingConstants
{
    public const string DefaultMarketCode = "ksa";

    public static string ResolveCurrency(string marketCode) => marketCode.Trim().ToLowerInvariant() switch
    {
        "ksa" => "SAR",
        "eg" => "EGP",
        _ => throw new InvalidOperationException($"pricing.currency_mismatch: market={marketCode}"),
    };
}
