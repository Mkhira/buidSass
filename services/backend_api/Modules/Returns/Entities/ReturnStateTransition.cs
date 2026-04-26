namespace BackendApi.Modules.Returns.Entities;

/// <summary>State-machine transition trail for <c>ReturnRequest</c>, <c>Refund</c> and
/// <c>Inspection</c>. Mirrors spec 011's pattern; spec 003's <c>audit_log_entries</c>
/// additionally records admin-driven mutations.</summary>
public sealed class ReturnStateTransition
{
    public const string MachineReturn = "return";
    public const string MachineRefund = "refund";
    public const string MachineInspection = "inspection";

    public long Id { get; set; }
    public Guid ReturnRequestId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010) — denormalised so audit
    /// queries can scope by market without joining the parent return request.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public Guid? RefundId { get; set; }
    public Guid? InspectionId { get; set; }
    public string Machine { get; set; } = string.Empty;
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public Guid? ActorAccountId { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ContextJson { get; set; }
}
