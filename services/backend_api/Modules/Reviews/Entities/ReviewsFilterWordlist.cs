namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Per-market profanity / abuse term per data-model §2.6. <see cref="Term"/> is
/// stored Arabic-normalized + lowercased at write time. The in-process
/// <c>ProfanityFilter</c> caches the active set per market and refreshes every
/// 60 s + on wordlist-mutation events (R13).
/// </summary>
public sealed class ReviewsFilterWordlist
{
    public string MarketCode { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;

    /// <summary>Reserved for V1.5 tiered moderation; V1 treats every match as "trip the filter".</summary>
    public string? Severity { get; set; }

    public Guid CreatedByActorId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
