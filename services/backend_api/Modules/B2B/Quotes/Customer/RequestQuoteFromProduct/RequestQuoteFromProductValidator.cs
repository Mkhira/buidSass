using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Field-level validator for <see cref="RequestQuoteFromProductRequest"/>. Mirrors the
/// from-cart validator's shape rules (branch-requires-company, PO length, message
/// at-least-one-locale) and adds the from-product-specific shape gates:
/// <list type="bullet">
///   <item><c>product_id</c> is required.</item>
///   <item><c>quantity</c> is required and MUST be ≥ 1 (no zero / negative orders).</item>
/// </list>
/// What does NOT live here: the <c>quote.product_not_quotable</c> branch — that
/// requires a cross-module read against <see cref="BackendApi.Modules.Shared.IProductCatalogQuery"/>
/// and stays in the handler.
/// </summary>
public sealed class RequestQuoteFromProductValidator : AbstractValidator<RequestQuoteFromProductRequest>
{
    public const int PoNumberMaxLength = 128;
    public const int QuantityMinimum = 1;

    public RequestQuoteFromProductValidator()
    {
        RuleFor(x => x.ProductId)
            .NotNull()
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("product_id is required");

        RuleFor(x => x.Quantity)
            .NotNull()
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("quantity is required");

        RuleFor(x => x.Quantity!.Value)
            .GreaterThanOrEqualTo(QuantityMinimum)
            .When(x => x.Quantity is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"quantity must be ≥ {QuantityMinimum}");

        // chk_quotes_branch_requires_company — surface as 400 before the DB write.
        RuleFor(x => x.BranchId)
            .Must((req, _) => req.BranchId is null || req.CompanyId is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("branch_id requires company_id");

        RuleFor(x => x.PoNumber)
            .MaximumLength(PoNumberMaxLength)
            .When(x => !string.IsNullOrEmpty(x.PoNumber))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"po_number must be at most {PoNumberMaxLength} characters");

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
