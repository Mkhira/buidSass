using BackendApi.Modules.B2B.Primitives;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

/// <summary>
/// Field-level validator for <see cref="RequestQuoteFromCartRequest"/>. Runs BEFORE the
/// handler — surfaces shape-only errors so the handler's pre-write rejection cascade
/// (account_inactive → rate_limit → membership → company_suspended → market_match →
/// po_required → po_already_used) doesn't have to defend against malformed input.
///
/// What lives here:
/// <list type="bullet">
///   <item><c>branch_id</c> requires <c>company_id</c> (the data-model invariant
///         <c>chk_quotes_branch_requires_company</c> mirrors this in PG; we surface
///         a 400 before reaching the DB).</item>
///   <item>When <c>message</c> is present, at least one of <c>en</c> / <c>ar</c> MUST
///         be non-empty (Principle 4 — bilingual parity; no silent locale defaults).</item>
///   <item>PO number, if supplied, is bounded to the 128-char column length.</item>
/// </list>
/// What does NOT live here (handler's job):
/// <list type="bullet">
///   <item>PO-required-when-company.po_required=true (needs DB lookup of the company).</item>
///   <item>PO-uniqueness when <c>company.unique_po_required=true</c> (needs a query
///         against <c>quotes</c>; race-protected by <c>UX_quotes_company_po</c>).</item>
///   <item>Cart-empty (needs <c>ICartSnapshotProvider</c>).</item>
///   <item>Membership / suspended-company / market-mismatch / account-inactive
///         (each needs a different cross-module read).</item>
/// </list>
///
/// Failure mapping: every <see cref="ValidationResult"/> failure is reported with a
/// stable <see cref="QuoteReasonCode"/> token via the validator's <c>WithErrorCode</c>
/// helper. The endpoint translates those codes 1:1 into problem-details
/// <c>reasonCode</c> extensions.
/// </summary>
public sealed class RequestQuoteFromCartValidator : AbstractValidator<RequestQuoteFromCartRequest>
{
    public const int PoNumberMaxLength = 128;

    public RequestQuoteFromCartValidator()
    {
        // chk_quotes_branch_requires_company — surface as 400 before the DB write.
        RuleFor(x => x.BranchId)
            .Must((req, _) => req.BranchId is null || req.CompanyId is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("branch_id requires company_id");

        // PO column is text(128) in the schema. Reject longer values up front.
        RuleFor(x => x.PoNumber)
            .MaximumLength(PoNumberMaxLength)
            .When(x => !string.IsNullOrEmpty(x.PoNumber))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"po_number must be at most {PoNumberMaxLength} characters");

        // Message: when present, at least one locale must be non-empty.
        RuleFor(x => x.Message)
            .Must(MessageHasAtLeastOneLocale)
            .When(x => x.Message is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("message must include at least one of {en, ar}");
    }

    private static bool MessageHasAtLeastOneLocale(LocalizedMessage? message)
    {
        if (message is null) return true; // covered by .When() above; defensive
        return !string.IsNullOrWhiteSpace(message.En) || !string.IsNullOrWhiteSpace(message.Ar);
    }
}
