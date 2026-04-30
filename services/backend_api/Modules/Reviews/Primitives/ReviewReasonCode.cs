namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Canonical owned reason-code surface for spec 022 per contract §10. Every
/// constant here MUST have matching keys in <c>reviews.en.icu</c> and
/// <c>reviews.ar.icu</c>; the parity is asserted by
/// <c>ReviewReasonCodeIcuKeyTests</c>.
/// </summary>
/// <remarks>
/// Namespace convention (deliberate per contract §10):
/// <list type="bullet">
///   <item><c>review.*</c> = entity-level codes for a single Review row.</item>
///   <item><c>reviews.*</c> = module-level codes (RBAC, market-schema, wordlist).</item>
/// </list>
/// </remarks>
public static class ReviewReasonCode
{
    // Eligibility (entity)
    public const string EligibilityNoDeliveredPurchase = "review.eligibility.no_delivered_purchase";
    public const string EligibilityRefunded = "review.eligibility.refunded";
    public const string EligibilityWindowClosed = "review.eligibility.window_closed";
    public const string EligibilityAlreadyReviewed = "review.eligibility.already_reviewed";

    // Field validation (entity)
    public const string HeadlineLengthInvalid = "review.headline.length_invalid";
    public const string BodyLengthInvalid = "review.body.length_invalid";
    public const string RatingOutOfRange = "review.rating.out_of_range";
    public const string LocaleInvalid = "review.locale.invalid";
    public const string MediaTooMany = "review.media.too_many";
    public const string MediaInvalidSignedUrl = "review.media.invalid_signed_url";

    // Edit (entity)
    public const string EditWindowClosed = "review.edit.window_closed";
    public const string EditNotAuthor = "review.edit.not_author";
    public const string EditDeletedTerminal = "review.edit.deleted_terminal";

    // Row-level (entity)
    public const string RowVersionConflict = "review.row.version_conflict";
    public const string RowDeleteForbidden = "review.row.delete_forbidden";

    // Reporting (entity)
    public const string ReportCannotReportOwnReview = "review.report.cannot_report_own_review";
    public const string ReportReasonInvalid = "review.report.reason_invalid";
    public const string ReportNoteRequired = "review.report.note_required";
    public const string ReportAlreadyReportedByActor = "review.report.already_reported_by_actor";
    public const string ReportUnauthenticated = "review.report.unauthenticated";

    // Rate-limit (entity)
    public const string RateLimitSubmissionExceeded = "review.rate_limit.submission_exceeded";
    public const string RateLimitEditExceeded = "review.rate_limit.edit_exceeded";
    public const string RateLimitReportExceeded = "review.rate_limit.report_exceeded";

    // Moderation (module)
    public const string ModerationForbidden = "reviews.moderation.forbidden";
    public const string ModerationDeleteRequiresSuperAdmin = "reviews.moderation.delete_requires_super_admin";
    public const string ModerationReasonRequired = "reviews.moderation.reason_required";
    public const string ModerationInvalidState = "reviews.moderation.invalid_state";
    public const string ModerationDeleteTerminal = "reviews.moderation.delete_terminal";
    public const string ModerationVersionConflict = "reviews.moderation.version_conflict";
    public const string ModerationRateLimitExceeded = "reviews.moderation.rate_limit_exceeded";

    // Policy admin (module)
    public const string PolicyForbidden = "reviews.policy.forbidden";
    public const string PolicyWordlistTermInvalid = "reviews.policy.wordlist.term_invalid";
    public const string PolicyMarketValueOutOfRange = "reviews.policy.market.value_out_of_range";
    public const string PolicyBodyRequired = "reviews.policy.body_required";

    // Aggregate (module)
    public const string AggregateMarketInvalid = "reviews.aggregate.market_invalid";

    /// <summary>Every code declared above; used by ICU-key parity tests.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        EligibilityNoDeliveredPurchase, EligibilityRefunded, EligibilityWindowClosed, EligibilityAlreadyReviewed,
        HeadlineLengthInvalid, BodyLengthInvalid, RatingOutOfRange, LocaleInvalid, MediaTooMany, MediaInvalidSignedUrl,
        EditWindowClosed, EditNotAuthor, EditDeletedTerminal,
        RowVersionConflict, RowDeleteForbidden,
        ReportCannotReportOwnReview, ReportReasonInvalid, ReportNoteRequired, ReportAlreadyReportedByActor, ReportUnauthenticated,
        RateLimitSubmissionExceeded, RateLimitEditExceeded, RateLimitReportExceeded,
        ModerationForbidden, ModerationDeleteRequiresSuperAdmin, ModerationReasonRequired, ModerationInvalidState,
        ModerationDeleteTerminal, ModerationVersionConflict, ModerationRateLimitExceeded,
        PolicyForbidden, PolicyWordlistTermInvalid, PolicyMarketValueOutOfRange, PolicyBodyRequired,
        AggregateMarketInvalid,
    };
}
