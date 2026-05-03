namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Append-only commercial-authoring audit row. Spec 007-b data-model §2.9.
/// Mutation/deletion is blocked by the <c>raise_immutable_audit_violation()</c> trigger.
/// </summary>
public sealed class CommercialAuditEvent
{
    public Guid Id { get; set; }
    public string TargetEntityKind { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    /// <summary>
    /// One of the 18 enum values listed in data-model §5.
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? DiffJson { get; set; }
    public string? ReasonNote { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public Guid? CorrelationId { get; set; }
}
