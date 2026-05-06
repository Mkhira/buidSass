namespace BackendApi.Modules.Support.Entities;

/// <summary>
/// Append-only breach detection rows. Composite PK
/// (<see cref="TicketId"/>, <see cref="BreachKind"/>, <see cref="DetectedAtUtc"/>)
/// gives natural reopen-tolerance while keeping idempotency at the worker
/// tick level (data-model §4).
/// </summary>
public sealed class TicketSlaBreachEvent
{
    public Guid TicketId { get; set; }
    public string BreachKind { get; set; } = string.Empty;
    public DateTimeOffset DetectedAtUtc { get; set; }
    public DateTimeOffset TargetDueUtc { get; set; }
    public bool EventEmitted { get; set; }
    public Guid? SupersededByEventId { get; set; }
    public Guid Id { get; set; } // surrogate for cross-row referencing on supersede
}

public static class TicketSlaBreachKind
{
    public const string FirstResponse = "first_response";
    public const string Resolution = "resolution";
}
