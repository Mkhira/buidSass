using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Synonyms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Search.Primitives;

public sealed class SearchBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    SynonymsSeeder synonymsSeeder,
    ILogger<SearchBootstrapHostedService> logger) : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly SynonymsSeeder _synonymsSeeder = synonymsSeeder;
    private readonly ILogger<SearchBootstrapHostedService> _logger = logger;

    public static volatile bool LastBootstrapSucceeded;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var searchEngine = scope.ServiceProvider.GetRequiredService<ISearchEngine>();

            foreach (var index in IndexNames.All)
            {
                await searchEngine.EnsureIndexAsync(index, cancellationToken);
            }

            await _synonymsSeeder.SeedAsync(searchEngine, IndexNames.All, cancellationToken);
            await EnsureCursorRowsAsync(scope.ServiceProvider, cancellationToken);
            LastBootstrapSucceeded = true;
        }
        catch (Exception ex)
        {
            LastBootstrapSucceeded = false;
            _logger.LogError(ex, "search.bootstrap.failed — indexes, synonyms, or cursor rows did not initialize; searches may return 503 until resolved");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureCursorRowsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<SearchDbContext>();

        foreach (var index in IndexNames.All)
        {
            var exists = await dbContext.SearchIndexerCursors.AnyAsync(x => x.IndexName == index.Name, cancellationToken);
            if (exists)
            {
                continue;
            }

            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO search.search_indexer_cursor
                    ("IndexName", "OutboxLastIdApplied", "LastSuccessAt", "LagSecondsLastObserved", "UpdatedAt")
                VALUES
                    ({index.Name}, 0, {DateTimeOffset.UtcNow}, 0, {DateTimeOffset.UtcNow})
                ON CONFLICT ("IndexName") DO NOTHING;
                """, cancellationToken);
        }
    }
}
