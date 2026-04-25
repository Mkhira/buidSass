namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// Per-state-machine transition trail. Every state change writes a row here; spec 003's
/// <c>audit_log_entries</c> additionally records admin-driven mutations (research R12 — two
/// consumers, two stores).
/// </summary>
public sealed class OrderStateTransition
{
    public const string MachineOrder = "order";
    public const string MachinePayment = "payment";
    public const string MachineFulfillment = "fulfillment";
    public const string MachineRefund = "refund";

    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public string Machine { get; set; } = string.Empty;
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public Guid? ActorAccountId { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ContextJson { get; set; }
}
