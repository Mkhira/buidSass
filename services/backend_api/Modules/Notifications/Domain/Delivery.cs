namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// One row per delivery-attempt per <see cref="Notification"/>, capturing
/// provider attribution + outcome per <c>data-model.md §notifications.deliveries</c>.
/// Used for 90-day audit queries (AC-24) and dead-letter forensics.
/// </summary>
public sealed class Delivery
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }

    /// <summary>1-based attempt counter.</summary>
    public int AttemptNo { get; set; }

    public string ProviderId { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string Status { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }
    public string? ErrorMessageRedacted { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
