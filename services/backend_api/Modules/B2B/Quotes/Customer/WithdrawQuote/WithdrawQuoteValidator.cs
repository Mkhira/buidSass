using BackendApi.Modules.B2B.Primitives;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.WithdrawQuote;

/// <summary>
/// Validator for <see cref="WithdrawQuoteRequest"/>. Shape-only checks per contract §2.5:
/// <list type="bullet">
///   <item><c>reason</c>, when supplied, is bounded to 64 characters (it's a stable
///         enum-ish token, not free-form copy — short prevents abuse and keeps
///         spec 025 notification templates small).</item>
/// </list>
///
/// Idempotency-Key header presence is checked at the endpoint layer (§1 — every
/// state-transitioning POST requires it), NOT here. The validator runs after the
/// header gate so validation failures still surface a stable
/// <c>quote.required_field_missing</c> token.
/// </summary>
public sealed class WithdrawQuoteValidator : AbstractValidator<WithdrawQuoteRequest>
{
    public const int ReasonMaxLength = 64;

    public WithdrawQuoteValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(ReasonMaxLength)
            .When(x => !string.IsNullOrEmpty(x.Reason))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"reason must be at most {ReasonMaxLength} characters");
    }
}
