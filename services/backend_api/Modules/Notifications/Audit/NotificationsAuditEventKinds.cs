namespace BackendApi.Modules.Notifications.Audit;

/// <summary>
/// T050 — canonical audit-event kinds emitted by the Notifications module
/// (data-model.md §audit table). 18 kinds covering template lifecycle,
/// campaign lifecycle, preference / opt-out, dead-letter operator actions,
/// provider routing changes, and secret-handling events.
///
/// Strings are stable contracts — never rename without bumping the contract
/// version and updating downstream audit-consumer dashboards.
/// </summary>
public static class NotificationsAuditEventKinds
{
    // Templates (T012–T016)
    public const string TemplateDraftCreated = "notifications.template.draft_created";
    public const string TemplateSubmittedForReview = "notifications.template.submitted_for_review";
    public const string TemplatePublished = "notifications.template.published";
    public const string TemplateRejected = "notifications.template.rejected";
    public const string TemplateArchived = "notifications.template.archived";

    // Campaigns (T036)
    public const string CampaignCreated = "notifications.campaign.created";
    public const string CampaignScheduled = "notifications.campaign.scheduled";
    public const string CampaignPaused = "notifications.campaign.paused";
    public const string CampaignResumed = "notifications.campaign.resumed";
    public const string CampaignCancelled = "notifications.campaign.cancelled";

    // Preferences + opt-out (T038, T039)
    public const string PreferenceChanged = "notifications.preference.changed";
    public const string CustomerUnsubscribed = "notifications.customer.unsubscribed";

    // Dead-letter operations (T045)
    public const string DeadLetterRetried = "notifications.dead_letter.retried";
    public const string DeadLetterDiscarded = "notifications.dead_letter.discarded";

    // Provider routing + health (T046, T047)
    public const string ProviderRoutingChanged = "notifications.provider_routing.changed";
    public const string ProviderFailoverTriggered = "notifications.provider.failover_triggered";
    public const string ProviderDegraded = "notifications.provider.degraded";

    // Secret handling (T011, T052)
    public const string SecretPlaceholderReplaced = "notifications.secret.placeholder_replaced";
}
