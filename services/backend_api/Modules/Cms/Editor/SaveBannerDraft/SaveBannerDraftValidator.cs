using BackendApi.Modules.Cms.Primitives;

namespace BackendApi.Modules.Cms.Editor.SaveBannerDraft;

/// <summary>
/// Save-time validator for banner-draft requests. Enforces the
/// banner-specific gates listed in contract §3.1 (schedule-window strictness,
/// CTA-kind/target shape coherence, external-url https requirement, headline
/// char caps). Surfaces stable reason codes from <see cref="CmsReasonCode"/>.
/// </summary>
public static class SaveBannerDraftValidator
{
    private static readonly string[] AllowedSlotKinds =
        { "hero_top", "category_strip", "footer_strip", "home_secondary" };

    private static readonly string[] AllowedCtaKinds =
        { "link", "category", "product", "bundle", "external_url", "none" };

    private static readonly string[] AllowedMarketCodes = { "EG", "KSA", "*" };

    public static (bool ok, string? reasonCode, string? detail) Validate(SaveBannerDraftRequest? body)
    {
        if (body is null)
        {
            return (false, CmsReasonCode.BannerScheduleWindowInvalid, "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(body.SlotKind) || Array.IndexOf(AllowedSlotKinds, body.SlotKind) < 0)
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch,
                $"slot_kind must be one of: {string.Join(", ", AllowedSlotKinds)}.");
        }

        if (string.IsNullOrWhiteSpace(body.MarketCode) || Array.IndexOf(AllowedMarketCodes, body.MarketCode) < 0)
        {
            return (false, CmsReasonCode.StorefrontMarketUnsupported,
                $"market_code must be one of: {string.Join(", ", AllowedMarketCodes)}.");
        }

        if (body.HeadlineAr is { Length: > 120 })
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch, "headline_ar must be ≤ 120 characters.");
        }

        if (body.HeadlineEn is { Length: > 120 })
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch, "headline_en must be ≤ 120 characters.");
        }

        if (body.SubheadAr is { Length: > 240 })
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch, "subhead_ar must be ≤ 240 characters.");
        }

        if (body.SubheadEn is { Length: > 240 })
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch, "subhead_en must be ≤ 240 characters.");
        }

        if (string.IsNullOrWhiteSpace(body.CtaKind) || Array.IndexOf(AllowedCtaKinds, body.CtaKind) < 0)
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch,
                $"cta_kind must be one of: {string.Join(", ", AllowedCtaKinds)}.");
        }

        var ctaShape = ValidateCtaShape(body.CtaKind, body.CtaTarget);
        if (!ctaShape.ok) return ctaShape;

        if (body.ScheduledStartUtc is not null
            && body.ScheduledEndUtc is not null
            && body.ScheduledStartUtc >= body.ScheduledEndUtc)
        {
            return (false, CmsReasonCode.BannerScheduleWindowInvalid,
                "scheduled_end_utc must be strictly after scheduled_start_utc.");
        }

        if (body.PriorityWithinSlot is < 0)
        {
            return (false, CmsReasonCode.BannerCtaKindTargetMismatch, "priority_within_slot must be ≥ 0.");
        }

        return (true, null, null);
    }

    private static (bool ok, string? reasonCode, string? detail) ValidateCtaShape(string ctaKind, string? ctaTarget)
    {
        switch (ctaKind)
        {
            case "none":
                if (!string.IsNullOrWhiteSpace(ctaTarget))
                {
                    return (false, CmsReasonCode.BannerCtaKindTargetMismatch,
                        "cta_target must be empty when cta_kind=none.");
                }
                return (true, null, null);

            case "link":
                if (string.IsNullOrWhiteSpace(ctaTarget))
                {
                    return (false, CmsReasonCode.BannerCtaKindTargetMismatch,
                        "cta_target is required for cta_kind=link.");
                }
                return (true, null, null);

            case "external_url":
                if (string.IsNullOrWhiteSpace(ctaTarget) ||
                    !Uri.TryCreate(ctaTarget, UriKind.Absolute, out var uri) ||
                    uri.Scheme != "https")
                {
                    return (false, CmsReasonCode.BannerExternalUrlHttpsRequired,
                        "cta_target must be an absolute https:// URL when cta_kind=external_url.");
                }
                return (true, null, null);

            case "category":
            case "product":
            case "bundle":
                if (string.IsNullOrWhiteSpace(ctaTarget) || !Guid.TryParse(ctaTarget, out _))
                {
                    return (false, CmsReasonCode.BannerCtaKindTargetMismatch,
                        $"cta_target must be a UUID when cta_kind={ctaKind}.");
                }
                return (true, null, null);

            default:
                return (false, CmsReasonCode.BannerCtaKindTargetMismatch,
                    $"Unknown cta_kind: {ctaKind}.");
        }
    }
}
