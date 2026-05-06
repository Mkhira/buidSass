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
    /// <summary>
    /// Hard upper bound on <see cref="Period"/>. Anchoring against today's
    /// <see cref="StartUtc"/> drifts when the period is longer than a day, and
    /// the workers in this module are designed for sub-daily / daily cadences.
    /// </summary>
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(1);

    /// <summary>How long between passes. Production defaults to 1 day; values
    /// greater than <see cref="MaxPeriod"/> are rejected by <see cref="FirstDelay"/>.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Wall-clock UTC time of day for the first pass.</summary>
    public TimeOnly StartUtc { get; set; } = new TimeOnly(3, 0);

    /// <summary>
    /// Returns the delay until the next <see cref="StartUtc"/>-anchored tick. The
    /// next tick is the smallest multiple of <see cref="Period"/> after
    /// <see cref="StartUtc"/> that is strictly greater than <paramref name="nowUtc"/>,
    /// so sub-daily periods (e.g. 6h with StartUtc=03:15) align to 09:15/15:15/21:15
    /// rather than waiting until tomorrow's StartUtc. If the configured
    /// <see cref="Period"/> is sub-hourly (dev override) the alignment is skipped
    /// and the worker runs on its first tick.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Period"/> exceeds <see cref="MaxPeriod"/> — the
    /// today-anchored math would silently drift, so we fail fast at config time.
    /// </exception>
    public TimeSpan FirstDelay(DateTimeOffset nowUtc)
    {
        if (Period <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"WorkerSchedule.Period must be positive (got {Period}).");
        }
        if (Period > MaxPeriod)
        {
            throw new InvalidOperationException(
                $"WorkerSchedule.Period must be <= 24h to keep the StartUtc anchor stable (got {Period}). " +
                "If a multi-day cadence is needed, switch to a fixed-epoch anchor first.");
        }
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
        var elapsed = nowUtc - todayStart;
        var remainder = TimeSpan.FromTicks(elapsed.Ticks % Period.Ticks);
        return remainder == TimeSpan.Zero ? TimeSpan.Zero : Period - remainder;
    }
}
