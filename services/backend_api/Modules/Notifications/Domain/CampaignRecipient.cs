namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Materialized recipient slot for a campaign per
/// <c>data-model.md §notifications.campaign_recipients</c>. One row per
/// <c>(campaign_id, recipient_id)</c>. Populated at the
/// <c>scheduled → sending</c> transition. <see cref="NotificationId"/> is null
/// when the recipient is skipped (rate-limited, channel-disabled, deactivated).
/// </summary>
public sealed class CampaignRecipient
{
    public Guid CampaignId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid? NotificationId { get; set; }
    public string? SkippedReason { get; set; }
    public DateTimeOffset MaterializedAt { get; set; } = DateTimeOffset.UtcNow;
}
