using BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class SaveLegalPageVersionDraftValidatorTests
{
    private static SaveLegalPageVersionDraftRequest Valid(
        string? versionLabel = "v1",
        string market = "EG",
        string? bodyAr = "ar",
        string? bodyEn = "en",
        DateTimeOffset? effective = null) =>
        new(versionLabel ?? "v1", bodyAr, bodyEn, effective, market, Xmin: null);

    [Theory]
    [InlineData("terms")]
    [InlineData("privacy")]
    [InlineData("returns")]
    [InlineData("cookies")]
    public void Accepts_all_supported_kinds(string kind)
    {
        var (ok, _, _) = SaveLegalPageVersionDraftValidator.Validate(kind, Valid());
        ok.Should().BeTrue();
    }

    [Fact]
    public void Rejects_unsupported_kind()
    {
        var (ok, reason, _) = SaveLegalPageVersionDraftValidator.Validate("eula", Valid());
        ok.Should().BeFalse();
        reason.Should().Be("cms.legal_page.not_found_for_market");
    }

    [Fact]
    public void Rejects_null_body()
    {
        var (ok, _, _) = SaveLegalPageVersionDraftValidator.Validate("terms", null);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Rejects_blank_version_label()
    {
        var (ok, _, _) = SaveLegalPageVersionDraftValidator.Validate(
            "terms", Valid(versionLabel: "  "));
        ok.Should().BeFalse();
    }

    [Fact]
    public void Rejects_unsupported_market()
    {
        var (ok, reason, _) = SaveLegalPageVersionDraftValidator.Validate(
            "terms", Valid(market: "FR"));
        ok.Should().BeFalse();
        reason.Should().Be("cms.storefront.market_unsupported");
    }

    [Fact]
    public void Accepts_cross_market_star()
    {
        var (ok, _, _) = SaveLegalPageVersionDraftValidator.Validate(
            "terms", Valid(market: "*"));
        ok.Should().BeTrue();
    }

    [Fact]
    public void Allows_blank_body_at_save_time()
    {
        // Both bodies are required at PUBLISH (LocaleCompletenessGate), not at save.
        var (ok, _, _) = SaveLegalPageVersionDraftValidator.Validate(
            "terms", Valid(bodyAr: null, bodyEn: null));
        ok.Should().BeTrue();
    }

    [Fact]
    public void Rejects_oversized_body()
    {
        var huge = new string('x', 200_001);
        var (ok, reason, _) = SaveLegalPageVersionDraftValidator.Validate(
            "terms", Valid(bodyEn: huge));
        ok.Should().BeFalse();
        reason.Should().Be("cms.blog.body_too_long");
    }
}
