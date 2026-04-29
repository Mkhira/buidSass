namespace BackendApi.Modules.Verification.Entities;

/// <summary>
/// One uploaded artifact per row. Survives the parent verification's terminal
/// transition (audit linkage; documents purged by
/// <c>VerificationDocumentPurgeWorker</c> after the market's retention window).
/// See spec 020 data-model §2.2.
/// </summary>
public sealed class VerificationDocument
{
    public Guid Id { get; set; }
    public Guid VerificationId { get; set; }

    /// <summary>Storage abstraction key. NULL once purged; the row remains.</summary>
    public string? StorageKey { get; set; }

    /// <summary>Allowlist: pdf / jpeg / png / heic.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>One of pending / clean / infected / error. Submission only valid when all docs are <c>clean</c>.</summary>
    public string ScanStatus { get; set; } = "pending";

    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Set when parent enters terminal state: <c>terminal_at + market.retention_months</c>.</summary>
    public DateTimeOffset? PurgeAfter { get; set; }

    /// <summary>Set by the purge worker; <see cref="StorageKey"/> simultaneously cleared.</summary>
    public DateTimeOffset? PurgedAt { get; set; }
}
