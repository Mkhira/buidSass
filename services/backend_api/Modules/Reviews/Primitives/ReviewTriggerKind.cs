namespace BackendApi.Modules.Reviews.Primitives;

/// <summary>
/// Discriminator written to <c>reviews.review_moderation_decisions.triggered_by</c>
/// and to the <c>triggered_by</c> column on the review row itself.
/// String values are the canonical wire form (snake_case).
/// </summary>
public static class ReviewTriggerKind
{
    public const string CustomerSubmission = "customer_submission";
    public const string CustomerEdit = "customer_edit";
    public const string CommunityReportThreshold = "community_report_threshold";
    public const string RefundEvent = "refund_event";
    public const string AccountLocked = "account_locked";
    public const string AccountDeleted = "account_deleted";
    public const string ModeratorAction = "moderator_action";
    public const string ManualSuperAdmin = "manual_super_admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CustomerSubmission,
        CustomerEdit,
        CommunityReportThreshold,
        RefundEvent,
        AccountLocked,
        AccountDeleted,
        ModeratorAction,
        ManualSuperAdmin,
    };
}
