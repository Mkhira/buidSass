using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Search.Admin.Health;

public static class HealthHandler
{
    public static async Task<SearchHealthResponse> HandleAsync(
        ISearchEngine searchEngine,
        SearchDbContext searchDbContext,
        CancellationToken cancellationToken)
    {
        var indexes = new List<SearchIndexHealthItem>(IndexNames.All.Count);
        var pingSamples = new List<int>(IndexNames.All.Count);

        foreach (var index in IndexNames.All)
        {
            var stats = await searchEngine.GetIndexStatsAsync(index.Name, cancellationToken);
            var cursor = await searchDbContext.SearchIndexerCursors
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IndexName == index.Name, cancellationToken);

            var lagSeconds = cursor?.LagSecondsLastObserved ?? 0;
            var status = lagSeconds <= 60 ? "healthy" : "lagging";

            indexes.Add(new SearchIndexHealthItem(
                index.Name,
                stats.DocumentCount,
                cursor?.LastSuccessAt,
                lagSeconds,
                status));

            pingSamples.Add(Math.Max(0, stats.EnginePingMs));
        }

        var isHealthy = await searchEngine.IsHealthyAsync(cancellationToken);
        var averagePing = pingSamples.Count == 0 ? 0 : (int)Math.Round(pingSamples.Average());

        return new SearchHealthResponse(
            indexes,
            isHealthy ? "available" : "unavailable",
            averagePing,
            SearchBootstrapHostedService.LastBootstrapSucceeded);
    }
}
