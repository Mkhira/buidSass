using BackendApi.Modules.B2B.Primitives;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.SubmitAcceptance;

/// <summary>
/// Validator for <see cref="SubmitAcceptanceRequest"/>. Shape-only checks per contract
/// §2.7. The contract-level rules (PO collision, eligibility, tax-drift, state-machine,
/// approver routing) live in the HANDLER because they require I/O and must be sequenced
/// transactionally.
///
/// <list type="bullet">
///   <item><c>po_number</c>, when supplied, is bounded to the same 64-character ceiling
///         used by <c>RequestQuoteFromCart</c> so the wire shape is symmetric across the
///         intake → acceptance journey.</item>
/// </list>
///
/// Idempotency-Key header presence is checked at the endpoint layer (§1 — every
/// state-transitioning POST requires it), NOT here. The validator runs after the
/// header gate.
/// </summary>
public sealed class SubmitAcceptanceValidator : AbstractValidator<SubmitAcceptanceRequest>
{
    public const int PoNumberMaxLength = 64;

    public SubmitAcceptanceValidator()
    {
        RuleFor(x => x.PoNumber)
            .MaximumLength(PoNumberMaxLength)
            .When(x => !string.IsNullOrEmpty(x.PoNumber))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"po_number must be at most {PoNumberMaxLength} characters");
    }
}
