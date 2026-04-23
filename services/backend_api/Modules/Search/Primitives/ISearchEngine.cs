namespace BackendApi.Modules.Search.Primitives;

public interface ISearchEngine
{
    Task EnsureIndexAsync(SearchIndexConfig index, CancellationToken cancellationToken);
    Task ApplySettingsAsync(
        string indexName,
        IReadOnlyCollection<string> searchableAttributes,
        IReadOnlyCollection<string> filterableAttributes,
        IReadOnlyCollection<string> sortableAttributes,
        IReadOnlyCollection<string> stopwords,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> synonyms,
        CancellationToken cancellationToken);
    Task UpsertAsync(string indexName, IReadOnlyCollection<ProductSearchProjection> documents, CancellationToken cancellationToken);
    Task DeleteAsync(string indexName, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken);
    Task ClearIndexAsync(string indexName, CancellationToken cancellationToken);
    Task<SearchEngineSearchResponse> SearchAsync(string indexName, SearchEngineSearchRequest request, CancellationToken cancellationToken);
    Task<SearchEngineAutocompleteResponse> AutocompleteAsync(string indexName, SearchEngineAutocompleteRequest request, CancellationToken cancellationToken);
    Task<ProductSearchProjection?> LookupExactAsync(string indexName, string code, CancellationToken cancellationToken);
    Task<SearchIndexStats> GetIndexStatsAsync(string indexName, CancellationToken cancellationToken);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}

public sealed record SearchEngineSearchRequest(
    string Query,
    int Page,
    int PageSize,
    IReadOnlyList<string> Filters,
    IReadOnlyList<string> Sort,
    IReadOnlyList<string> Facets);

public sealed record SearchEngineAutocompleteRequest(
    string Query,
    int Limit);

public sealed record SearchEngineSearchResponse(
    IReadOnlyList<ProductSearchProjection> Hits,
    int TotalEstimate,
    int EngineLatencyMs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Facets);

public sealed record SearchEngineAutocompleteResponse(
    IReadOnlyList<ProductSearchProjection> Hits,
    int EngineLatencyMs);

public sealed record SearchIndexStats(
    string IndexName,
    long DocumentCount,
    int EnginePingMs);
