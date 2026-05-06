namespace BackendApi.Modules.Support.Entities;

/// <summary>Append-only polymorphic link to cross-module entities.</summary>
public sealed class TicketLink
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid LinkedEntityId { get; set; }
    public string CreatedVia { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class TicketLinkCreatedVia
{
    public const string Submission = "submission";
    public const string Conversion = "conversion";
    public const string LeadLink = "lead_link";
}
