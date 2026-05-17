namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Targeted broadcast unit per <c>data-model.md §notifications.campaigns</c>.
/// State machine: <c>draft → scheduled → sending → completed | paused →
/// sending | cancelled</c>. NEVER carries channel='otp' (DB check constraint).
/// </summary>
public sealed class Campaign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;

    public Guid TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>jsonb segment definition (market + last-purchase + locale + opt-in).</summary>
    public string TargetCriteriaJson { get; set; } = "{}";

    public DateTimeOffset? SendAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Populated at <c>scheduled → sending</c>.</summary>
    public int? RecipientCountSnapshot { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public uint Xmin { get; set; }
}
