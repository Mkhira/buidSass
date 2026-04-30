using BackendApi.Modules.Reviews.Primitives;

namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Customer review entity per spec 022 data-model §2.1. One row per
/// (customer, product) live review (terminal <see cref="ReviewState.Deleted"/>
/// rows are excluded from the unique constraint).
/// </summary>
public sealed class Review
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public Guid OrderLineId { get; set; }
    public string MarketCode { get; set; } = string.Empty;

    public int Rating { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;

    /// <summary>jsonb array of signed media URLs (max 4 enforced by CHECK).</summary>
    public string MediaUrlsJson { get; set; } = "[]";

    public ReviewState State { get; set; }
    public DateTimeOffset StateChangedAtUtc { get; set; }
    public Guid StateChangedByActorId { get; set; }
    public string? StateChangedReasonNote { get; set; }
    public string? StateChangedAdminNote { get; set; }

    /// <summary>Discriminator value from <see cref="ReviewTriggerKind"/>.</summary>
    public string TriggeredBy { get; set; } = ReviewTriggerKind.CustomerSubmission;

    public DateTimeOffset? PendingModerationStartedAt { get; set; }
    public string[] FilterTripTerms { get; set; } = Array.Empty<string>();
    public bool MediaAttachmentReviewRequired { get; set; }

    public int EditCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Snapshot of the order-line delivery time captured at submission for eligibility-window audit.</summary>
    public DateTimeOffset DeliveredAtUtc { get; set; }

    /// <summary>Multi-vendor slot reserved per Principle 6; always null in V1.</summary>
    public Guid? VendorId { get; set; }

    /// <summary>Postgres xmin mapped via EF <c>IsRowVersion()</c> for optimistic concurrency.</summary>
    public uint Xmin { get; set; }
}
