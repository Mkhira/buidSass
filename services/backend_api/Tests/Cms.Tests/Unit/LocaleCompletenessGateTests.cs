using BackendApi.Modules.Cms.Primitives;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class LocaleCompletenessGateTests
{
    [Fact]
    public void Banner_publish_requires_both_headlines_and_assets()
    {
        var assetAr = Guid.NewGuid();
        var assetEn = Guid.NewGuid();

        var ok = LocaleCompletenessGate.CheckBanner("AR", "EN", assetAr, assetEn);
        ok.IsAllowed.Should().BeTrue();

        LocaleCompletenessGate.CheckBanner(null, "EN", assetAr, assetEn).IsAllowed.Should().BeFalse();
        LocaleCompletenessGate.CheckBanner("AR", null, assetAr, assetEn).IsAllowed.Should().BeFalse();
        LocaleCompletenessGate.CheckBanner("AR", "EN", null, assetEn).IsAllowed.Should().BeFalse();
        LocaleCompletenessGate.CheckBanner("AR", "EN", assetAr, null).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Banner_block_lists_missing_fields()
    {
        var blocked = LocaleCompletenessGate.CheckBanner(null, null, null, null);
        blocked.IsAllowed.Should().BeFalse();
        blocked.MissingFields.Should().BeEquivalentTo(new[]
        {
            "headline_ar", "headline_en", "asset_id_ar", "asset_id_en",
        });
        blocked.ReasonCode.Should().Be(CmsReasonCode.PublishLocaleCompletenessMissing);
    }

    [Fact]
    public void Featured_section_requires_both_titles_and_at_least_one_ref()
    {
        LocaleCompletenessGate.CheckFeaturedSection("AR", "EN", referencesCount: 1)
            .IsAllowed.Should().BeTrue();
        LocaleCompletenessGate.CheckFeaturedSection("AR", "EN", referencesCount: 0)
            .IsAllowed.Should().BeFalse();
        LocaleCompletenessGate.CheckFeaturedSection(null, "EN", referencesCount: 1)
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Faq_requires_both_q_and_a_in_both_locales()
    {
        LocaleCompletenessGate.CheckFaqEntry("Q-AR", "Q-EN", "A-AR", "A-EN")
            .IsAllowed.Should().BeTrue();
        LocaleCompletenessGate.CheckFaqEntry("Q-AR", null, "A-AR", "A-EN")
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Blog_allows_single_locale()
    {
        // Authored AR; English body is null — still allowed (R17).
        LocaleCompletenessGate.CheckBlogArticle("ar", body: "AR body", "title", "desc")
            .IsAllowed.Should().BeTrue();

        // Missing body in authored locale → blocked.
        LocaleCompletenessGate.CheckBlogArticle("ar", body: null, "title", "desc")
            .IsAllowed.Should().BeFalse();

        // Missing SEO blocks publishing.
        LocaleCompletenessGate.CheckBlogArticle("ar", body: "AR", null, "desc")
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Legal_page_requires_both_bodies_and_effective_at()
    {
        LocaleCompletenessGate.CheckLegalPageVersion("AR", "EN", DateTimeOffset.UtcNow.AddDays(1))
            .IsAllowed.Should().BeTrue();
        LocaleCompletenessGate.CheckLegalPageVersion("AR", "EN", null)
            .IsAllowed.Should().BeFalse();
        LocaleCompletenessGate.CheckLegalPageVersion(null, "EN", DateTimeOffset.UtcNow.AddDays(1))
            .IsAllowed.Should().BeFalse();
    }
}
