using PhoneNumbers;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class PhoneNormalizer
{
    private static readonly Dictionary<string, MarketCode> RegionToMarket = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SA"] = MarketCode.Ksa,
        ["EG"] = MarketCode.Eg,
    };

    private static readonly Dictionary<string, string> MarketToRegion = new(StringComparer.OrdinalIgnoreCase)
    {
        [MarketCode.Ksa.Value] = "SA",
        [MarketCode.Eg.Value] = "EG",
    };

    private readonly PhoneNumberUtil _phoneNumberUtil = PhoneNumberUtil.GetInstance();

    public NormalizedPhone Normalize(string rawPhone, MarketCode? marketHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPhone);

        var regionHint = marketHint is null
            ? null
            : MarketToRegion.GetValueOrDefault(marketHint.Value);

        PhoneNumber parsed;
        try
        {
            parsed = _phoneNumberUtil.Parse(rawPhone, regionHint);
        }
        catch (NumberParseException ex)
        {
            throw new InvalidOperationException("The provided phone number is invalid.", ex);
        }

        if (!_phoneNumberUtil.IsValidNumber(parsed))
        {
            throw new InvalidOperationException("The provided phone number is invalid.");
        }

        var region = _phoneNumberUtil.GetRegionCodeForNumber(parsed);
        if (string.IsNullOrWhiteSpace(region) || !RegionToMarket.TryGetValue(region, out var inferredMarket))
        {
            throw new InvalidOperationException("Could not infer the market code from the phone number.");
        }

        if (marketHint is not null && !string.Equals(inferredMarket.Value, marketHint.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Phone number market does not match the selected market.");
        }

        var e164 = _phoneNumberUtil.Format(parsed, PhoneNumberFormat.E164);
        return new NormalizedPhone(e164, inferredMarket);
    }
}

public sealed record NormalizedPhone(string E164, MarketCode InferredMarketCode);
