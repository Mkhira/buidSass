namespace BackendApi.Modules.Support.Entities;

/// <summary>Per-market support knobs row.</summary>
public sealed class SupportMarketSchema
{
    public string MarketCode { get; set; } = string.Empty;
    public bool AutoAssignmentEnabled { get; set; }
    public int ReopenWindowDays { get; set; }
    public int MaxReopenCount { get; set; }
    public int AutoCloseAfterResolvedDays { get; set; }
    public int AttachmentMaxPerTicket { get; set; }
    public int AttachmentMaxSizeMb { get; set; }
    public int AttachmentCumulativeMaxMb { get; set; }
    public string[] AllowedMimeTypes { get; set; } = Array.Empty<string>();
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid? UpdatedByActorId { get; set; }
}
