using BackendApi.Modules.Cms.Editor.SaveBannerDraft;
using BackendApi.Modules.Cms.Primitives;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class SaveBannerDraftValidatorTests
{
    private static SaveBannerDraftRequest Valid() => new(
        SlotKind: "hero_top",
        HeadlineAr: "العرض الكبير",
        HeadlineEn: "Big Sale",
        SubheadAr: null,
        SubheadEn: null,
        AssetIdAr: Guid.NewGuid(),
        AssetIdEn: Guid.NewGuid(),
        CtaKind: "category",
        CtaTarget: Guid.NewGuid().ToString(),
        ScheduledStartUtc: DateTimeOffset.UtcNow.AddHours(1),
        ScheduledEndUtc: DateTimeOffset.UtcNow.AddHours(2),
        MarketCode: "KSA",
        PriorityWithinSlot: 100,
        Xmin: null);

    [Fact]
    public void Happy_path_passes()
    {
        var (ok, _, _) = SaveBannerDraftValidator.Validate(Valid());
        ok.Should().BeTrue();
    }

    [Fact]
    public void Null_body_rejected()
    {
        var (ok, _, _) = SaveBannerDraftValidator.Validate(null);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Reversed_window_rejected_with_schedule_window_invalid()
    {
        var bad = Valid() with
        {
            ScheduledStartUtc = DateTimeOffset.UtcNow.AddHours(2),
            ScheduledEndUtc = DateTimeOffset.UtcNow.AddHours(1),
        };

        var (ok, reason, _) = SaveBannerDraftValidator.Validate(bad);

        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.BannerScheduleWindowInvalid);
    }

    [Fact]
    public void External_url_must_be_https()
    {
        var bad = Valid() with { CtaKind = "external_url", CtaTarget = "http://example.com/promo" };

        var (ok, reason, _) = SaveBannerDraftValidator.Validate(bad);

        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.BannerExternalUrlHttpsRequired);
    }

    [Fact]
    public void Catalog_cta_target_must_be_uuid()
    {
        var bad = Valid() with { CtaKind = "product", CtaTarget = "not-a-uuid" };

        var (ok, reason, _) = SaveBannerDraftValidator.Validate(bad);

        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.BannerCtaKindTargetMismatch);
    }

    [Fact]
    public void Cta_none_must_have_empty_target()
    {
        var bad = Valid() with { CtaKind = "none", CtaTarget = "leftover" };

        var (ok, reason, _) = SaveBannerDraftValidator.Validate(bad);

        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.BannerCtaKindTargetMismatch);
    }

    [Fact]
    public void Headline_over_120_chars_rejected()
    {
        var bad = Valid() with { HeadlineEn = new string('x', 121) };

        var (ok, _, _) = SaveBannerDraftValidator.Validate(bad);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Unknown_market_rejected()
    {
        var bad = Valid() with { MarketCode = "FR" };

        var (ok, reason, _) = SaveBannerDraftValidator.Validate(bad);

        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.StorefrontMarketUnsupported);
    }
}
