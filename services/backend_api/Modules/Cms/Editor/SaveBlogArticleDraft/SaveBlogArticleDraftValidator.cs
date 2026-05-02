using System.Text.RegularExpressions;
using BackendApi.Modules.Cms.Primitives;

namespace BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;

public static partial class SaveBlogArticleDraftValidator
{
    private static readonly HashSet<string> SupportedMarkets = new(StringComparer.Ordinal)
        { "EG", "KSA", "*" };

    private static readonly HashSet<string> SupportedLocales = new(StringComparer.Ordinal)
        { "ar", "en" };

    public static (bool Ok, string? ReasonCode, string? Detail) Validate(
        SaveBlogArticleDraftRequest? body)
    {
        if (body is null)
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing, "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(body.Category))
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing, "category is required.");
        }

        if (string.IsNullOrWhiteSpace(body.Slug) || !SlugRegex().IsMatch(body.Slug))
        {
            return (false, CmsReasonCode.BlogSlugInvalidPattern,
                "slug must match ^[a-z0-9]+(-[a-z0-9]+)*$.");
        }

        if (!SupportedLocales.Contains(body.AuthoredLocale))
        {
            return (false, CmsReasonCode.StorefrontLocaleUnsupported,
                $"Unsupported authored_locale '{body.AuthoredLocale}'.");
        }

        if (!SupportedMarkets.Contains(body.MarketCode))
        {
            return (false, CmsReasonCode.StorefrontMarketUnsupported,
                $"Unsupported market_code '{body.MarketCode}'.");
        }

        if (string.IsNullOrWhiteSpace(body.Title) || body.Title.Length > 240)
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing,
                "title is required (≤ 240 chars).");
        }

        if (body.Body is { Length: > 60_000 })
        {
            return (false, CmsReasonCode.BlogBodyTooLong,
                "body must be ≤ 60000 chars.");
        }

        if (body.SeoMetaTitle is { Length: > 70 })
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing,
                "seo_meta_title must be ≤ 70 chars.");
        }
        if (body.SeoMetaDescription is { Length: > 160 })
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing,
                "seo_meta_description must be ≤ 160 chars.");
        }

        return (true, null, null);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex SlugRegex();
}
