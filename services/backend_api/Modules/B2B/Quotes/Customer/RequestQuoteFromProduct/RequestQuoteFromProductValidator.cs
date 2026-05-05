using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Field-level validator for <see cref="RequestQuoteFromProductRequest"/>. Mirrors
/// <see cref="RequestQuoteFromCartValidator"/> for the shared fields and adds the
/// product-specific shape rules required by contract §2.2.
///
/// What lives here (§2.2):
/// <list type="bullet">
///   <item><c>product_id</c> is REQUIRED and must be a non-empty Guid.</item>
///   <item><c>quantity</c> is REQUIRED and must be &gt; 0. The schema does not cap
///         quantity (commercial decisions on max-quantity-per-line happen later in
///         the admin authoring step), so we only enforce positivity here.</item>
///   <item><c>branch_id</c> requires <c>company_id</c> (data-model invariant
///         <c>chk_quotes_branch_requires_company</c>).</item>
///   <item><c>message</c>, when present, has at least one of <c>en</c> / <c>ar</c>
///         non-empty (Principle 4 — bilingual parity).</item>
///   <item><c>po_number</c>, when supplied, is bounded to the 128-char column length
///         (matches the cart-intake validator's ceiling).</item>
/// </list>
///
/// What does NOT live here (handler's job — needs I/O):
/// <list type="bullet">
///   <item>Product quotability lookup (<c>IProductCatalogQuery.IsQuotableAsync</c>).</item>
///   <item>Account / membership / suspension / market / PO collision checks.</item>
///   <item>Rate-limit windowing.</item>
/// </list>
/// </summary>
public sealed class RequestQuoteFromProductValidator : AbstractValidator<RequestQuoteFromProductRequest>
{
    public const int PoNumberMaxLength = RequestQuoteFromCartValidator.PoNumberMaxLength;

    public RequestQuoteFromProductValidator()
    {
        // product_id — required, non-empty.
        RuleFor(x => x.ProductId)
            .NotNull()
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("product_id is required");
        RuleFor(x => x.ProductId!.Value)
            .Must(id => id != Guid.Empty)
            .When(x => x.ProductId.HasValue)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("product_id must be a non-empty Guid");

        // quantity — required, > 0. Spec 021 doesn't fix an upper bound at intake;
        // admin authoring sets the line totals against pricing baselines later.
        RuleFor(x => x.Quantity)
            .NotNull()
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("quantity is required");
        RuleFor(x => x.Quantity!.Value)
            .GreaterThan(0)
            .When(x => x.Quantity.HasValue)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("quantity must be greater than zero");

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
        if (message is null) return true;
        return !string.IsNullOrWhiteSpace(message.En) || !string.IsNullOrWhiteSpace(message.Ar);
    }
}
