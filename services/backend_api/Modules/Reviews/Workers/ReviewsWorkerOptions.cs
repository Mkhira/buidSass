namespace BackendApi.Modules.Reviews.Workers;

/// <summary>Options for the Reviews module hosted workers (Phase L).</summary>
public sealed class ReviewsWorkerOptions
{
    public const string SectionName = "Reviews:Workers";

    public ReviewsWorkerSchedule RatingAggregateRebuild { get; set; } = new()
    {
        Period = TimeSpan.FromHours(24),
        InitialDelay = TimeSpan.FromMinutes(2),
    };

    public ReviewsWorkerSchedule ReviewIntegrityScan { get; set; } = new()
    {
        Period = TimeSpan.FromHours(24),
        InitialDelay = TimeSpan.FromMinutes(5),
    };
}

public sealed class ReviewsWorkerSchedule
{
    public TimeSpan Period { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Throws when the schedule is misconfigured. Period must be strictly
    /// positive (zero would cause a hot loop), InitialDelay must be non-negative.
    /// Called from the worker registration site so misconfig fails fast at
    /// startup rather than being deferred to a runtime crash loop.
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
