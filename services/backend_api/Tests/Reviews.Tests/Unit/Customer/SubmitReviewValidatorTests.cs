using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;

namespace Reviews.Tests.Unit.Customer;

/// <summary>
/// Spec 022 T056 — every <see cref="SubmitReviewValidator"/> branch.
/// Pure unit test, no DB. The integration-layer tests
/// (<c>SubmitReviewHandlerTests</c>) cover the eligibility / filter / persist
/// pipeline; this suite isolates field-level validation so a regression here
/// is unambiguous.
/// </summary>
public sealed class SubmitReviewValidatorTests
{
    private const string ValidHeadline = "Headline";
    private const string ValidBody = "Body content of sufficient length to satisfy the validator's lower bound.";

    [Fact]
    public void Null_request_is_rejected()
    {
        var (ok, code, _) = SubmitReviewValidator.Validate(null);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.BodyLengthInvalid);
    }

    [Fact]
    public void Empty_product_id_is_rejected()
    {
        var req = new SubmitReviewRequest(Guid.Empty, 5, ValidHeadline, ValidBody, "en", null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.BodyLengthInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public void Rating_outside_one_to_five_is_rejected(int rating)
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), rating, ValidHeadline, ValidBody, "en", null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.RatingOutOfRange);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Rating_inside_one_to_five_passes(int rating)
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), rating, ValidHeadline, ValidBody, "en", null);
        var (ok, _, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_headline_is_rejected(string headline)
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, headline, ValidBody, "en", null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.HeadlineLengthInvalid);
    }

    [Fact]
    public void Headline_over_100_chars_is_rejected()
    {
        var headline = new string('A', 101);
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, headline, ValidBody, "en", null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.HeadlineLengthInvalid);
    }

    [Fact]
    public void Headline_at_100_chars_passes()
    {
        var headline = new string('A', 100);
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, headline, ValidBody, "en", null);
        var (ok, _, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_body_is_rejected(string body)
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, body, "en", null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.BodyLengthInvalid);
    }

    [Fact]
    public void Body_over_4000_chars_is_rejected()
    {
        var body = new string('A', 4001);
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, body, "en", null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.BodyLengthInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("EN")]    // case-sensitive — only lowercase ar/en accepted
    [InlineData("ar-SA")] // sub-tag not supported
    [InlineData("fr")]
    public void Locale_outside_ar_or_en_is_rejected(string? locale)
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, ValidBody, locale!, null);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.LocaleInvalid);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Locale_ar_or_en_passes(string locale)
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, ValidBody, locale, null);
        var (ok, _, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeTrue();
    }

    [Fact]
    public void More_than_four_media_urls_is_rejected()
    {
        var media = Enumerable.Range(0, 5).Select(_ => "https://storage.test/abc").ToArray();
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, ValidBody, "en", media);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.MediaTooMany);
    }

    [Fact]
    public void Exactly_four_media_urls_passes()
    {
        var media = Enumerable.Range(0, 4).Select(_ => "https://storage.test/abc").ToArray();
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, ValidBody, "en", media);
        var (ok, _, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://storage.test/abc")]   // not https
    [InlineData("not-a-url")]                  // not parseable
    [InlineData("")]                           // empty entry
    [InlineData("   ")]                        // whitespace
    [InlineData("ftp://storage.test/abc")]     // wrong scheme
    public void Invalid_signed_url_in_media_is_rejected(string url)
    {
        var media = new[] { url };
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, ValidBody, "en", media);
        var (ok, code, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.MediaInvalidSignedUrl);
    }

    [Fact]
    public void Null_media_collection_passes()
    {
        var req = new SubmitReviewRequest(Guid.NewGuid(), 4, ValidHeadline, ValidBody, "en", null);
        var (ok, _, _) = SubmitReviewValidator.Validate(req);
        ok.Should().BeTrue();
    }
}
