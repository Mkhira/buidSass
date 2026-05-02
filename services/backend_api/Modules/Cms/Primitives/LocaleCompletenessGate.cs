namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Implements the FR-007 publish-time locale-completeness rule per data-model §4.
/// Returns <see cref="LocaleCompletenessResult.Allowed"/> or a
/// <see cref="LocaleCompletenessResult.Blocked"/> result with a stable reason
/// code + per-locale field-level breakdown.
/// </summary>
public static class LocaleCompletenessGate
{
    public static LocaleCompletenessResult CheckBanner(
        string? headlineAr, string? headlineEn,
        Guid? assetIdAr, Guid? assetIdEn)
    {
        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(headlineAr)) missing.Add("headline_ar");
        if (string.IsNullOrWhiteSpace(headlineEn)) missing.Add("headline_en");
        if (assetIdAr is null) missing.Add("asset_id_ar");
        if (assetIdEn is null) missing.Add("asset_id_en");
        return missing.Count == 0
            ? LocaleCompletenessResult.Allowed
            : LocaleCompletenessResult.Blocked(missing);
    }

    public static LocaleCompletenessResult CheckFeaturedSection(
        string? titleAr, string? titleEn,
        int referencesCount)
    {
        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(titleAr)) missing.Add("title_ar");
        if (string.IsNullOrWhiteSpace(titleEn)) missing.Add("title_en");
        if (referencesCount < 1) missing.Add("references");
        return missing.Count == 0
            ? LocaleCompletenessResult.Allowed
            : LocaleCompletenessResult.Blocked(missing);
    }

    public static LocaleCompletenessResult CheckFaqEntry(
        string? questionAr, string? questionEn,
        string? answerAr, string? answerEn)
    {
        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(questionAr)) missing.Add("question_ar");
        if (string.IsNullOrWhiteSpace(questionEn)) missing.Add("question_en");
        if (string.IsNullOrWhiteSpace(answerAr)) missing.Add("answer_ar");
        if (string.IsNullOrWhiteSpace(answerEn)) missing.Add("answer_en");
        return missing.Count == 0
            ? LocaleCompletenessResult.Allowed
            : LocaleCompletenessResult.Blocked(missing);
    }

    /// <summary>
    /// Blog allows single-locale per R17 — body in <paramref name="authoredLocale"/>
    /// must be present, and SEO fields are mandatory at publish.
    /// </summary>
    public static LocaleCompletenessResult CheckBlogArticle(
        string authoredLocale,
        string? body,
        string? seoMetaTitle,
        string? seoMetaDescription)
    {
        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(body)) missing.Add($"body[{authoredLocale}]");
        if (string.IsNullOrWhiteSpace(seoMetaTitle)) missing.Add("seo_meta_title");
        if (string.IsNullOrWhiteSpace(seoMetaDescription)) missing.Add("seo_meta_description");
        return missing.Count == 0
            ? LocaleCompletenessResult.Allowed
            : LocaleCompletenessResult.Blocked(missing);
    }

    public static LocaleCompletenessResult CheckLegalPageVersion(
        string? bodyAr, string? bodyEn, DateTimeOffset? effectiveAtUtc)
    {
        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(bodyAr)) missing.Add("body_ar");
        if (string.IsNullOrWhiteSpace(bodyEn)) missing.Add("body_en");
        if (effectiveAtUtc is null) missing.Add("effective_at_utc");
        return missing.Count == 0
            ? LocaleCompletenessResult.Allowed
            : LocaleCompletenessResult.Blocked(missing);
    }
}

/// <summary>
/// Outcome of <see cref="LocaleCompletenessGate"/> per spec 024 data-model §4.
/// </summary>
public sealed record LocaleCompletenessResult(
    bool IsAllowed,
    string? ReasonCode,
    IReadOnlyList<string> MissingFields)
{
    public static LocaleCompletenessResult Allowed { get; } = new(true, null, Array.Empty<string>());

    public static LocaleCompletenessResult Blocked(IReadOnlyList<string> missingFields) =>
        new(false, CmsReasonCode.PublishLocaleCompletenessMissing, missingFields);
}
