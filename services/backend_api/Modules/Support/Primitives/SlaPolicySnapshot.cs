namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Frozen-at-creation SLA snapshot per FR-022. Lives on the ticket row so
/// breach evaluation is deterministic regardless of subsequent policy edits.
/// </summary>
public sealed record SlaPolicySnapshot(
    string MarketCode,
    string Priority,
    int FirstResponseTargetMinutes,
    int ResolutionTargetMinutes)
{
    public DateTimeOffset FirstResponseDueUtc(DateTimeOffset reference) =>
        reference.AddMinutes(FirstResponseTargetMinutes);

    public DateTimeOffset ResolutionDueUtc(DateTimeOffset reference) =>
        reference.AddMinutes(ResolutionTargetMinutes);

    /// <summary>FR-021 default SLA grid for KSA + EG (in minutes).</summary>
    public static (int firstResponse, int resolution) DefaultFor(string priority) => priority switch
    {
        TicketPriorityNames.Urgent => (15, 240),
        TicketPriorityNames.High => (60, 720),
        TicketPriorityNames.Normal => (240, 2880),
        TicketPriorityNames.Low => (480, 5760),
        _ => throw new InvalidOperationException($"Unknown priority for SLA default: '{priority}'."),
    };
}
