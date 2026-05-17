namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Cold archival mirror of <see cref="DeadLetterEntry"/>. Rows older than 30
/// days are moved here by <c>DeadLetterArchiver</c>. State retained for query
/// per AC-29. (Same shape as <see cref="DeadLetterEntry"/>.)
/// </summary>
public sealed class DeadLetterArchive
{
    public Guid NotificationId { get; set; }
    public string? LastErrorMessageRedacted { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
    public Guid? ResolvedBy { get; set; }

    public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;
}
