using BackendApi.Modules.Cms.Editor.SaveBlogArticleDraft;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class SaveBlogArticleDraftValidatorTests
{
    private static SaveBlogArticleDraftRequest Valid(
        string slug = "ramadan-tips-2026",
        string locale = "ar",
        string market = "EG",
        string title = "Ramadan Tips",
        string? body = "body",
        string? metaTitle = null,
        string? metaDescription = null) =>
        new("tips", slug, locale, title, "summary", body,
            CoverAssetId: null, metaTitle, metaDescription, null,
            "BlogPosting", null, market, Xmin: null);

    [Fact]
    public void Accepts_well_formed_request()
    {
        var (ok, _, _) = SaveBlogArticleDraftValidator.Validate(Valid());
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("Ramadan-Tips")]
    [InlineData("ramadan tips")]
    [InlineData("-leading-dash")]
    [InlineData("trailing-dash-")]
    [InlineData("double--dash")]
    public void Rejects_invalid_slug_pattern(string badSlug)
    {
        var (ok, reason, _) = SaveBlogArticleDraftValidator.Validate(Valid(slug: badSlug));
        ok.Should().BeFalse();
        reason.Should().Be("cms.blog.slug_invalid_pattern");
    }

    [Fact]
    public void Rejects_unsupported_locale()
    {
        var (ok, reason, _) = SaveBlogArticleDraftValidator.Validate(Valid(locale: "fr"));
        ok.Should().BeFalse();
        reason.Should().Be("cms.storefront.locale_unsupported");
    }

    [Fact]
    public void Rejects_unsupported_market()
    {
        var (ok, reason, _) = SaveBlogArticleDraftValidator.Validate(Valid(market: "FR"));
        ok.Should().BeFalse();
        reason.Should().Be("cms.storefront.market_unsupported");
    }

    [Fact]
    public void Rejects_blank_title()
    {
        var (ok, _, _) = SaveBlogArticleDraftValidator.Validate(Valid(title: " "));
        ok.Should().BeFalse();
    }

    [Fact]
    public void Rejects_oversized_body()
    {
        var huge = new string('x', 60_001);
        var (ok, reason, _) = SaveBlogArticleDraftValidator.Validate(Valid(body: huge));
        ok.Should().BeFalse();
        reason.Should().Be("cms.blog.body_too_long");
    }

    [Fact]
    public void Rejects_oversized_seo_meta_title()
    {
        var (ok, _, _) = SaveBlogArticleDraftValidator.Validate(
            Valid(metaTitle: new string('x', 71)));
        ok.Should().BeFalse();
    }

    [Fact]
    public void Rejects_oversized_seo_meta_description()
    {
        var (ok, _, _) = SaveBlogArticleDraftValidator.Validate(
            Valid(metaDescription: new string('x', 161)));
        ok.Should().BeFalse();
    }
}
