namespace BackendApi.Modules.Support.Workers;

/// <summary>Options for the Support module hosted workers (Phase N per spec 023 §6).</summary>
public sealed class SupportWorkerOptions
{
    public const string SectionName = "Support:Workers";

    /// <summary>SLA breach detection — runs every 60s per FR-022.</summary>
    public SupportWorkerSchedule SlaBreachWatch { get; set; } = new()
    {
        Period = TimeSpan.FromSeconds(60),
        InitialDelay = TimeSpan.FromSeconds(15),
    };

    /// <summary>Auto-close resolved tickets past per-market grace window — hourly.</summary>
    public SupportWorkerSchedule AutoCloseResolutionWindow { get; set; } = new()
    {
        Period = TimeSpan.FromHours(1),
        InitialDelay = TimeSpan.FromMinutes(2),
    };

    /// <summary>Reclaim tickets whose assigned agent is offboarded — nightly.</summary>
    public SupportWorkerSchedule OrphanedAssignmentReclaim { get; set; } = new()
    {
        Period = TimeSpan.FromHours(24),
        InitialDelay = TimeSpan.FromMinutes(5),
    };
}

public sealed class SupportWorkerSchedule
{
    public TimeSpan Period { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Validates the schedule. Period must be strictly positive (zero would
    /// cause a hot loop); InitialDelay must be non-negative. Called at worker
    /// registration time so misconfig fails fast at startup.
    /// </summary>
    public void Validate(string sectionPath)
    {
        if (Period <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{sectionPath}.Period must be > 0 (got {Period}); zero/negative would cause a hot loop.");
        }
        if (InitialDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{sectionPath}.InitialDelay must be >= 0 (got {InitialDelay}).");
        }
    }
}
