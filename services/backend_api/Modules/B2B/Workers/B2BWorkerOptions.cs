namespace BackendApi.Modules.B2B.Workers;

/// <summary>
/// Configuration root bound to <c>B2B:Workers</c> (T137 / quickstart §8). Each
/// worker has its own <c>Period</c> + <c>StartUtc</c> so daily ordering can be
/// tweaked via configuration. Production / Staging defaults live here; dev
/// overrides via <c>appsettings.Development.json</c>.
/// </summary>
public sealed class B2BWorkerOptions
{
    public const string SectionName = "B2B:Workers";

    public WorkerSchedule Expiry { get; set; } = new()
    {
        Period = TimeSpan.FromDays(1),
        StartUtc = new TimeOnly(3, 15),
    };

    public WorkerSchedule Invitation { get; set; } = new()
    {
        Period = TimeSpan.FromDays(1),
        StartUtc = new TimeOnly(3, 45),
    };
}

public sealed class WorkerSchedule
{
    /// <summary>How long between passes. Production defaults to 1 day.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Wall-clock UTC time of day for the first pass.</summary>
    public TimeOnly StartUtc { get; set; } = new TimeOnly(3, 0);

    /// <summary>
    /// Returns the delay until the next StartUtc-aligned tick. If the configured
    /// <see cref="Period"/> is sub-hourly (dev override) the alignment is skipped
    /// and the worker runs on its first tick.
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
        return todayStart.AddDays(1) - nowUtc;
    }
}
