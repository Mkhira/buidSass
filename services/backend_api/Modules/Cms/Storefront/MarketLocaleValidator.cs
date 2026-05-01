using BackendApi.Modules.Cms.Primitives;

namespace BackendApi.Modules.Cms.Storefront;

/// <summary>
/// Storefront-side market and locale validation per spec 024 contract §7.1.
/// Storefront reads MUST reject unsupported markets / locales with the
/// stable reason codes <see cref="CmsReasonCode.StorefrontMarketUnsupported"/>
/// and <see cref="CmsReasonCode.StorefrontLocaleUnsupported"/>.
/// </summary>
public static class MarketLocaleValidator
{
    /// <summary>Markets accepted on storefront query strings.</summary>
    public static readonly IReadOnlySet<string> SupportedStorefrontMarkets =
        new HashSet<string>(StringComparer.Ordinal) { "EG", "KSA" };

    /// <summary>Markets accepted on admin query strings (includes <c>*</c>).</summary>
    public static readonly IReadOnlySet<string> SupportedAdminMarkets =
        new HashSet<string>(StringComparer.Ordinal) { "EG", "KSA", "*" };

    public static readonly IReadOnlySet<string> SupportedLocales =
        new HashSet<string>(StringComparer.Ordinal) { "ar", "en" };

    public static (bool ok, string? reasonCode, string? detail) ValidateStorefront(string? market, string? locale)
    {
        if (string.IsNullOrWhiteSpace(market) || !SupportedStorefrontMarkets.Contains(market))
        {
            return (false, CmsReasonCode.StorefrontMarketUnsupported,
                $"market must be one of: {string.Join(", ", SupportedStorefrontMarkets)}.");
        }
        if (string.IsNullOrWhiteSpace(locale) || !SupportedLocales.Contains(locale))
        {
            return (false, CmsReasonCode.StorefrontLocaleUnsupported,
                $"locale must be one of: {string.Join(", ", SupportedLocales)}.");
        }
        return (true, null, null);
    }
}
