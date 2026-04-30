using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;

namespace Reviews.Tests.Unit.Primitives;

/// <summary>
/// Spec 022 T047 — every owned reason code follows the contract namespace
/// convention from contract §10. ICU-key parity (T047 proper) lands once
/// the AR + EN dictionaries are authored in <c>Modules/Reviews/Messages/</c>.
/// </summary>
public sealed class ReviewReasonCodeTests
{
    [Fact]
    public void All_codes_are_unique_and_non_empty()
    {
        ReviewReasonCode.All.Should().NotBeEmpty();
        ReviewReasonCode.All.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c));
        ReviewReasonCode.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Codes_use_module_or_entity_namespace_prefix()
    {
        // Per contract §10 — review.* for entity-level, reviews.* for module-level.
        // Allowed prefixes: "review.eligibility", "review.headline", "review.body",
        // "review.rating", "review.locale", "review.media", "review.edit",
        // "review.row", "review.report", "review.rate_limit", "reviews.moderation",
        // "reviews.policy", "reviews.aggregate".
        var allowed = new[]
        {
            "review.eligibility.", "review.headline.", "review.body.",
            "review.rating.", "review.locale.", "review.media.", "review.edit.",
            "review.row.", "review.report.", "review.rate_limit.",
            "reviews.moderation.", "reviews.policy.", "reviews.aggregate.",
        };
        foreach (var code in ReviewReasonCode.All)
        {
            allowed.Any(p => code.StartsWith(p, StringComparison.Ordinal))
                .Should().BeTrue($"reason code '{code}' must match the spec 022 namespace convention");
        }
    }

    [Fact]
    public void Critical_contract_codes_are_present()
    {
        ReviewReasonCode.All.Should().Contain(new[]
        {
            ReviewReasonCode.EligibilityNoDeliveredPurchase,
            ReviewReasonCode.EligibilityWindowClosed,
            ReviewReasonCode.RatingOutOfRange,
            ReviewReasonCode.RowVersionConflict,
            ReviewReasonCode.RowDeleteForbidden,
            ReviewReasonCode.ModerationDeleteRequiresSuperAdmin,
            ReviewReasonCode.ModerationVersionConflict,
            ReviewReasonCode.ModerationDeleteTerminal,
            ReviewReasonCode.PolicyForbidden,
            ReviewReasonCode.AggregateMarketInvalid,
        });
    }
}
