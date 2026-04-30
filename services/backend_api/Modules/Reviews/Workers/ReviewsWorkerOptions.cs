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
}
