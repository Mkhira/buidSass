namespace BackendApi.Modules.Support.Entities;

/// <summary>
/// Lifecycled support ticket per data-model §4.
/// State + content writes use Postgres <c>xmin</c> for optimistic concurrency.
/// </summary>
public sealed class SupportTicket
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? CompanyId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // Polymorphic linked entity (no DB-level FK — loose-coupling).
    public string? LinkedEntityKind { get; set; }
    public Guid? LinkedEntityId { get; set; }

    /// <summary>P6 multi-vendor slot; null in V1.</summary>
    public Guid? VendorId { get; set; }

    /// <summary>Denormalized cache of the active assignment row's <c>agent_id</c>.</summary>
    public Guid? AssignedAgentId { get; set; }

    // SLA snapshot (frozen at creation) -- FR-022.
    public int FirstResponseTargetMinutesSnapshot { get; set; }
    public int ResolutionTargetMinutesSnapshot { get; set; }
    public DateTimeOffset FirstResponseDueUtc { get; set; }
    public DateTimeOffset ResolutionDueUtc { get; set; }
    public DateTimeOffset? BreachAcknowledgedAtFirstResponse { get; set; }
    public DateTimeOffset? BreachAcknowledgedAtResolution { get; set; }

    public int ReopenCount { get; set; }
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>EF Core RowVersion mapping (<c>xmin</c>) per project pattern.</summary>
    public uint Xmin { get; set; }
}
