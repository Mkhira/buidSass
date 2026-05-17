namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// One row per dead-lettered notification per
/// <c>data-model.md §notifications.dead_letter_queue</c>. <see cref="ResolvedAt"/>
/// populates when an operator runs Retry / Discard. 30-day retention is
/// clarify-locked; older rows are archived to
/// <c>notifications.dead_letter_queue_archive</c> by
/// <c>DeadLetterArchiver</c>.
/// </summary>
public sealed class DeadLetterEntry
{
    public Guid NotificationId { get; set; }
    public string? LastErrorMessageRedacted { get; set; }
    public DateTimeOffset EnteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
    public Guid? ResolvedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
