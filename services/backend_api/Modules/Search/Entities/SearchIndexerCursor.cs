namespace BackendApi.Modules.Search.Entities;

public sealed class SearchIndexerCursor
{
    public string IndexName { get; set; } = string.Empty;
    public long OutboxLastIdApplied { get; set; }
    public DateTimeOffset LastSuccessAt { get; set; } = DateTimeOffset.UtcNow;
    public int LagSecondsLastObserved { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
