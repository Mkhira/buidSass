namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// One <see cref="Notification"/> per <c>(recipient × channel)</c> per event
/// (or per <c>campaign × recipient</c>). Per <c>data-model.md §notifications.notifications</c>.
/// State machine: <c>pending → queued → sending → delivered | failed →
/// retrying → (loop) → dead_letter | skipped</c>.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? RecipientId { get; set; }
    public string RecipientKind { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string EventKind { get; set; } = string.Empty;

    /// <summary>Snapshot reference (BR-8). NEVER null after queueing.</summary>
    public Guid? TemplateVersionId { get; set; }

    public string MarketCode { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? SkippedReason { get; set; }
    public string? FailedReason { get; set; }
    public string? ProviderId { get; set; }
    public string? ProviderMessageId { get; set; }
    public int Attempts { get; set; }

    /// <summary>SHA-256 derived from event correlation + channel + recipient.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Rendered + PII-redacted (AC-27).</summary>
    public string PayloadRedactedJson { get; set; } = "{}";

    public Guid? CampaignId { get; set; }

    /// <summary>For OTP / quiet-hours / scheduled-campaign deferral.</summary>
    public DateTimeOffset? NotBefore { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>EF Core row-version mapping (xmin) per project pattern.</summary>
    public uint Xmin { get; set; }
}
