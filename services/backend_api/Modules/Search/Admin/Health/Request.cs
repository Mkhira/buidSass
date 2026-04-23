namespace BackendApi.Modules.Search.Admin.Health;

public sealed record HealthRequest();

public sealed record SearchHealthResponse(
    IReadOnlyList<SearchIndexHealthItem> Indexes,
    string EngineStatus,
    int EnginePingMs,
    bool BootstrapSucceeded);

public sealed record SearchIndexHealthItem(
    string Name,
    long DocCount,
    DateTimeOffset? LastSuccessAt,
    int LagSeconds,
    string Status);
