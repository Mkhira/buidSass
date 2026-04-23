using BackendApi.Modules.Search.Entities;
using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Synonyms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Search.Primitives;

public sealed class SearchBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    ISearchEngine searchEngine,
    SynonymsSeeder synonymsSeeder,
    ILogger<SearchBootstrapHostedService> logger) : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ISearchEngine _searchEngine = searchEngine;
    private readonly SynonymsSeeder _synonymsSeeder = synonymsSeeder;
    private readonly ILogger<SearchBootstrapHostedService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var index in IndexNames.All)
            {
                await _searchEngine.EnsureIndexAsync(index, cancellationToken);
            }

            await _synonymsSeeder.SeedAsync(_searchEngine, IndexNames.All, cancellationToken);
            await EnsureCursorRowsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "search.bootstrap.failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureCursorRowsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        foreach (var index in IndexNames.All)
        {
            var exists = await dbContext.SearchIndexerCursors.AnyAsync(x => x.IndexName == index.Name, cancellationToken);
            if (exists)
            {
                continue;
            }

            dbContext.SearchIndexerCursors.Add(new SearchIndexerCursor
            {
                IndexName = index.Name,
                OutboxLastIdApplied = 0,
                LastSuccessAt = DateTimeOffset.UtcNow,
                LagSecondsLastObserved = 0,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
