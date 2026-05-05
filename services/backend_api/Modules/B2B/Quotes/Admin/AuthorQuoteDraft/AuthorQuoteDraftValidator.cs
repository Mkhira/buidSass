using BackendApi.Modules.B2B.Primitives;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Admin.AuthorQuoteDraft;

/// <summary>
/// Spec 021 T087 — pre-handler validator for the author-draft contract. Enforces the
/// 400 vocabulary distinct from the handler's state-machine gate:
/// <list type="bullet">
///   <item><c>quote.required_field_missing</c> — at least one line; non-empty
///         terms in at least one of {en, ar}.</item>
///   <item><c>quote.below_baseline_reason_required</c> — when ANY line carries an
///         <c>override_unit_price</c> AND no <c>override_reason</c> in either
///         locale (FR-040). Note: handler still re-checks against the actual
///         baseline; the validator just enforces shape — when an override is
///         present, the reason MUST be non-empty.</item>
/// </list>
/// </summary>
public sealed class AuthorQuoteDraftValidator : AbstractValidator<AuthorQuoteDraftRequest>
{
    public AuthorQuoteDraftValidator()
    {
        RuleFor(x => x.Lines)
            .Must(lines => lines is not null && lines.Count > 0)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("lines must contain at least one entry");

        RuleFor(x => x.TermsText)
            .Must(HasAtLeastOneLocale)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("terms_text must include at least one of {en, ar}");

        // FR-040 / contract §4.3: the BELOW-baseline reason check needs the actual
        // baseline from spec 007-a's pricing engine, so it lives in the handler —
        // a shape-only rule here would over-reject (an at-baseline or above-baseline
        // override doesn't need a reason). The handler emits
        // QuoteBelowBaselineReasonRequired only when (override < baseline) AND
        // (override_reason missing both locales).
    }

    private static bool HasAtLeastOneLocale(Customer.RequestQuoteFromCart.LocalizedMessage? msg)
    {
        if (msg is null) return false;
        return !string.IsNullOrWhiteSpace(msg.En) || !string.IsNullOrWhiteSpace(msg.Ar);
    }
}
