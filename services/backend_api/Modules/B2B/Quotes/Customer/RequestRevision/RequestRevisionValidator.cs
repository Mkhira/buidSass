using BackendApi.Modules.B2B.Primitives;
using FluentValidation;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestRevision;

/// <summary>
/// Validator for <see cref="RequestRevisionRequest"/>. Per contract §2.6:
/// <list type="bullet">
///   <item>The body MUST contain a <c>comment</c> object — empty body or
///         <c>{}</c> emits <c>quote.no_changes_provided</c> per the §9
///         vocabulary (the customer is "asking for revision without saying what").</item>
///   <item>The <c>comment</c> object MUST carry at least one of <c>{en, ar}</c>
///         non-empty (Principle 4: bilingual parity, no machine translation;
///         silent locale defaults are non-compliant).</item>
///   <item>Per-locale text is bounded to a generous limit so the comment fits
///         downstream notification + PDF templates.</item>
/// </list>
///
/// Failure mapping uses two distinct reason codes:
/// <list type="bullet">
///   <item><c>quote.no_changes_provided</c> — when neither comment is present at all
///         (the buyer is hitting the endpoint without saying what to change).</item>
///   <item><c>quote.required_field_missing</c> — when the comment object is present
///         but malformed (over-length / both locales empty strings).</item>
/// </list>
/// </summary>
public sealed class RequestRevisionValidator : AbstractValidator<RequestRevisionRequest>
{
    public const int CommentMaxLength = 2000;

    public RequestRevisionValidator()
    {
        // Whole-comment-missing: emits no_changes_provided (the spec's distinct
        // §9 token for "buyer didn't tell us anything").
        RuleFor(x => x.Comment)
            .NotNull()
            .WithErrorCode(QuoteReasonCode.QuoteNoChangesProvided.ToToken())
            .WithMessage("comment is required");

        // Once we know comment is non-null, locale-presence + bounds checks. The
        // .When clause defends each rule from a null comment so they don't fire
        // alongside the NotNull above.
        RuleFor(x => x.Comment!)
            .Must(CommentHasAtLeastOneLocale)
            .When(x => x.Comment is not null)
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage("comment must include at least one of {en, ar} non-empty");

        RuleFor(x => x.Comment!.En)
            .MaximumLength(CommentMaxLength)
            .When(x => x.Comment is not null && !string.IsNullOrEmpty(x.Comment.En))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"comment.en must be at most {CommentMaxLength} characters");

        RuleFor(x => x.Comment!.Ar)
            .MaximumLength(CommentMaxLength)
            .When(x => x.Comment is not null && !string.IsNullOrEmpty(x.Comment.Ar))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken())
            .WithMessage($"comment.ar must be at most {CommentMaxLength} characters");
    }

    private static bool CommentHasAtLeastOneLocale(LocalizedComment comment)
    {
        return !string.IsNullOrWhiteSpace(comment.En)
            || !string.IsNullOrWhiteSpace(comment.Ar);
    }
}
