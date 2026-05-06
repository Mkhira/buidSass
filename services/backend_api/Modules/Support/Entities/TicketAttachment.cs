namespace BackendApi.Modules.Support.Entities;

/// <summary>Append-only attachment metadata + redaction tombstone (FR-012a).</summary>
public sealed class TicketAttachment
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid TicketId { get; set; }
    public string? StorageObjectId { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string OriginalFilename { get; set; } = string.Empty;
    public string State { get; set; } = "active";
    public DateTimeOffset? RedactedAtUtc { get; set; }
    public string? RedactedByRole { get; set; }
    public string? RedactionReasonNote { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class TicketAttachmentState
{
    public const string Active = "active";
    public const string Redacted = "redacted";
}
