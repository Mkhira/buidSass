using BackendApi.Modules.B2B.Primitives;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.ListMyQuotes;

/// <summary>
/// Validator for <see cref="ListMyQuotesRequest"/>. Shape-only checks per contract §2.3:
/// <list type="bullet">
///   <item><c>page</c> ≥ 1 when supplied (default 1).</item>
///   <item><c>page_size</c> in [1, 50] when supplied (default 20, hard cap 50 per §2.3).</item>
///   <item><c>sort</c> ∈ <c>{newest, oldest}</c> when supplied (default <c>newest</c>).</item>
///   <item><c>state</c> CSV — every token must be a valid <see cref="QuoteState"/>
///         (deferred to handler-side parser; surfaced as
///         <c>quote.required_field_missing</c> if any token is unknown).</item>
/// </list>
/// All failures emit <c>quote.required_field_missing</c> per the §9 vocabulary —
/// query-string-level errors don't have a finer-grained reason code in spec 021.
/// </summary>
public sealed class ListMyQuotesValidator : AbstractValidator<ListMyQuotesRequest>
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public ListMyQuotesValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .When(x => x.Page is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("page must be ≥ 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .When(x => x.PageSize is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"page_size must be in [1, {MaxPageSize}]");

        RuleFor(x => x.Sort)
            .Must(s => s is null || ListMyQuotesSort.IsValid(s))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("sort must be one of {newest, oldest}");

        RuleFor(x => x.State)
            .Must(StateCsvIsValid)
            .When(x => !string.IsNullOrWhiteSpace(x.State))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("state must be a comma-separated list of QuoteState tokens");
    }

    private static bool StateCsvIsValid(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return true;
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!QuoteStateExtensions.TryParseToken(token, out _)) return false;
        }
        return true;
    }
}
