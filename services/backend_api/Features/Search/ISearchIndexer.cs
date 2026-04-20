namespace BackendApi.Features.Search;

/// <summary>
/// Stub contract for the search-indexer abstraction. The concrete Meilisearch adapter lands
/// with spec 006. Shape matches specs/phase-1B/006-search/contracts/events.md.
/// </summary>
public interface ISearchIndexer
{
    Task IndexVariantAsync(Guid variantId, CancellationToken ct);

    Task ReindexAllAsync(CancellationToken ct);
}
