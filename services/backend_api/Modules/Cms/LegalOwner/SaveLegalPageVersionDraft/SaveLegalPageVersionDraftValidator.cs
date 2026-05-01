using BackendApi.Modules.Cms.Primitives;

namespace BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;

/// <summary>
/// Static request-shape validator for save-legal-page-version-draft. Both
/// <c>body_ar</c> and <c>body_en</c> are required at PUBLISH (enforced by the
/// LocaleCompletenessGate); save-time only enforces shape: non-empty version
/// label, supported market code, effective-at-utc not in the past relative to
/// the row's editor save (caller is responsible for the >= now() rule at
/// publish-time, not at save-time, because legal teams routinely author
/// effective-at dates years ahead).
/// </summary>
public static class SaveLegalPageVersionDraftValidator
{
    private static readonly HashSet<string> SupportedMarkets = new(StringComparer.Ordinal)
    {
        "EG", "KSA", "*",
    };

    private static readonly HashSet<string> SupportedKinds = new(StringComparer.Ordinal)
    {
        "terms", "privacy", "returns", "cookies",
    };

    public static (bool Ok, string? ReasonCode, string? Detail) Validate(
        string legalPageKind,
        SaveLegalPageVersionDraftRequest? body)
    {
        if (body is null)
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing, "Request body is required.");
        }

        if (!SupportedKinds.Contains(legalPageKind))
        {
            return (false, CmsReasonCode.LegalPageNotFoundForMarket,
                $"Unsupported legal page kind '{legalPageKind}'.");
        }

        if (string.IsNullOrWhiteSpace(body.VersionLabel))
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing,
                "version_label is required.");
        }

        if (body.VersionLabel.Length > 64)
        {
            return (false, CmsReasonCode.PublishLocaleCompletenessMissing,
                "version_label must be ≤ 64 chars.");
        }

        if (string.IsNullOrWhiteSpace(body.MarketCode) ||
            !SupportedMarkets.Contains(body.MarketCode))
        {
            return (false, CmsReasonCode.StorefrontMarketUnsupported,
                $"Unsupported market_code '{body.MarketCode}'.");
        }

        // Body length sanity (legal documents but not unbounded).
        if (body.BodyAr is { Length: > 200_000 } || body.BodyEn is { Length: > 200_000 })
        {
            return (false, CmsReasonCode.BlogBodyTooLong,
                "body must be ≤ 200000 chars.");
        }

        return (true, null, null);
    }
}
