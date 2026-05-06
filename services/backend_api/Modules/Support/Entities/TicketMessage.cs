namespace BackendApi.Modules.Support.Entities;

/// <summary>Append-only message thread row per data-model §4.</summary>
public sealed class TicketMessage
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? BodyLocale { get; set; }
    public bool LeadIntervention { get; set; }
    public DateTimeOffset? RedactedAtUtc { get; set; }
    public string? RedactedByRole { get; set; }
    public Guid? OriginatingRedactionRequestTicketId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
