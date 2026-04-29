namespace BackendApi.Modules.Verification.Entities;

/// <summary>
/// One row per reminder emission (or skip). UNIQUE (verification_id, window_days)
/// is the deduplication invariant (FR-019, research §R5). See spec 020 data-model
/// §2.5.
/// </summary>
public sealed class VerificationReminder
{
    public Guid Id { get; set; }
    public Guid VerificationId { get; set; }

    /// <summary>Must match an entry in the verification's snapshotted <c>reminder_windows_days</c>.</summary>
    public int WindowDays { get; set; }

    public DateTimeOffset EmittedAt { get; set; }

    /// <summary>True for back-window skip-with-audit-note (research §R5).</summary>
    public bool Skipped { get; set; }

    /// <summary>Required when <see cref="Skipped"/> is true.</summary>
    public string? SkipReason { get; set; }
}
