namespace BackendApi.Modules.Verification.Workers;

/// <summary>
/// Configuration root bound to <c>Verification:Workers</c> per research §R12 and
/// task T097. Each worker has independent <c>Period</c> + <c>StartUtc</c> so the
/// daily ordering (Expiry → Reminder → DocumentPurge) can be tweaked without
/// code changes.
/// </summary>
public sealed class VerificationWorkerOptions
{
    public WorkerSchedule Expiry { get; set; } = new() { Period = TimeSpan.FromDays(1), StartUtc = new TimeOnly(3, 0) };
    public WorkerSchedule Reminder { get; set; } = new() { Period = TimeSpan.FromDays(1), StartUtc = new TimeOnly(3, 30) };
    public WorkerSchedule DocumentPurge { get; set; } = new() { Period = TimeSpan.FromDays(1), StartUtc = new TimeOnly(4, 0) };
}

public sealed class WorkerSchedule
{
    /// <summary>How long between passes. Production defaults to 1 day.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Wall-clock UTC time of day for the first pass.</summary>
    public TimeOnly StartUtc { get; set; } = new TimeOnly(3, 0);

    /// <summary>
    /// Returns the delay until the next StartUtc-aligned tick. If StartUtc has
    /// already passed today, returns the delay until tomorrow's StartUtc. Used
    /// only on first boot — subsequent passes use <see cref="Period"/>.
    ///
    /// <para>If <see cref="Period"/> is small enough (e.g., dev override of one
    /// minute), the alignment becomes meaningless; we return a zero delay so the
    /// first pass runs right away.</para>
    /// </summary>
    public TimeSpan FirstDelay(DateTimeOffset nowUtc)
    {
        if (Period < TimeSpan.FromHours(1))
        {
            return TimeSpan.Zero;
        }
        var todayStart = new DateTimeOffset(
            nowUtc.Year, nowUtc.Month, nowUtc.Day,
            StartUtc.Hour, StartUtc.Minute, StartUtc.Second, TimeSpan.Zero);
        if (todayStart > nowUtc)
        {
            return todayStart - nowUtc;
        }
        // Already past today's start — next tick is tomorrow's start.
        return todayStart.AddDays(1) - nowUtc;
    }
}
