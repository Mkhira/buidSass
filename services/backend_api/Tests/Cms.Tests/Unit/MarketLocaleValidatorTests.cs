using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Storefront;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class MarketLocaleValidatorTests
{
    [Theory]
    [InlineData("EG", "ar")]
    [InlineData("EG", "en")]
    [InlineData("KSA", "ar")]
    [InlineData("KSA", "en")]
    public void Supported_pairs_pass(string market, string locale)
    {
        var (ok, _, _) = MarketLocaleValidator.ValidateStorefront(market, locale);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Star_market_rejected_on_storefront()
    {
        // `*` is admin-only; storefront must reject (Principle 3 ambiguity guard).
        var (ok, reason, _) = MarketLocaleValidator.ValidateStorefront("*", "ar");
        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.StorefrontMarketUnsupported);
    }

    [Fact]
    public void Unknown_market_rejected()
    {
        var (ok, reason, _) = MarketLocaleValidator.ValidateStorefront("FR", "ar");
        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.StorefrontMarketUnsupported);
    }

    [Fact]
    public void Unknown_locale_rejected()
    {
        var (ok, reason, _) = MarketLocaleValidator.ValidateStorefront("EG", "fr");
        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.StorefrontLocaleUnsupported);
    }

    [Fact]
    public void Empty_inputs_rejected()
    {
        MarketLocaleValidator.ValidateStorefront("", "ar").ok.Should().BeFalse();
        MarketLocaleValidator.ValidateStorefront(null, "ar").ok.Should().BeFalse();
        MarketLocaleValidator.ValidateStorefront("EG", "").ok.Should().BeFalse();
        MarketLocaleValidator.ValidateStorefront("EG", null).ok.Should().BeFalse();
    }

    [Fact]
    public void Admin_set_includes_star()
    {
        MarketLocaleValidator.SupportedAdminMarkets.Should().Contain("*");
        MarketLocaleValidator.SupportedStorefrontMarkets.Should().NotContain("*");
    }
}
