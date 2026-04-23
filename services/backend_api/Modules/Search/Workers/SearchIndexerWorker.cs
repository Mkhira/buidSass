using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using BackendApi.Modules.Observability;
using BackendApi.Modules.Search.Entities;
using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Search.Workers;

public sealed class SearchIndexerWorker(
    IServiceScopeFactory scopeFactory,
    SearchMetrics searchMetrics,
    ILogger<SearchIndexerWorker> logger) : BackgroundService, ICatalogEventSubscriber
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly SearchMetrics _searchMetrics = searchMetrics;
    private readonly ILogger<SearchIndexerWorker> _logger = logger;

    public async Task PublishAsync(CatalogEventEnvelope envelope, CancellationToken cancellationToken)
    {
        // Process in-band so catalog outbox dispatch only succeeds after the search side-effect
        // is actually applied. This avoids queue timing races in shared fixture hosts.
        await ProcessEnvelopeAsync(envelope, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessEnvelopeAsync(CatalogEventEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var searchDb = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
        var searchEngine = scope.ServiceProvider.GetRequiredService<ISearchEngine>();
        var normalizer = scope.ServiceProvider.GetRequiredService<ArabicNormalizer>();

        var eventType = envelope.EventType.Trim().ToLowerInvariant();
        switch (eventType)
        {
            case "catalog.product.archived":
                await DeleteFromAllIndexesAsync(searchEngine, envelope.AggregateId, cancellationToken);
                foreach (var index in IndexNames.All)
                {
                    await AdvanceCursorAsync(searchDb, index.Name, envelope.OutboxId, envelope.CommittedAt, cancellationToken);
                }
                return;

            case "catalog.product.published":
            case "catalog.product.field_updated":
            case "catalog.product.restriction_changed":
                await UpsertProductAcrossIndexesAsync(catalogDb, searchEngine, normalizer, envelope.AggregateId, cancellationToken);
                foreach (var index in IndexNames.All)
                {
                    await AdvanceCursorAsync(searchDb, index.Name, envelope.OutboxId, envelope.CommittedAt, cancellationToken);
                }
                return;

            default:
                return;
        }
    }

    private async Task UpsertProductAcrossIndexesAsync(
        CatalogDbContext catalogDb,
        ISearchEngine searchEngine,
        ArabicNormalizer normalizer,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var product = await catalogDb.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null || !string.Equals(product.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            await DeleteFromAllIndexesAsync(searchEngine, productId, cancellationToken);
            return;
        }

        var brand = await catalogDb.Brands
            .AsNoTracking()
            .SingleAsync(b => b.Id == product.BrandId, cancellationToken);

        var categories = await (
            from pc in catalogDb.ProductCategories.AsNoTracking()
            join c in catalogDb.Categories.AsNoTracking() on pc.CategoryId equals c.Id
            where pc.ProductId == product.Id
            select new { c.Id, c.NameAr, c.NameEn })
            .ToListAsync(cancellationToken);

        var primaryMedia = await catalogDb.ProductMedia
            .AsNoTracking()
            .Where(m => m.ProductId == product.Id && m.IsPrimary)
            .OrderBy(m => m.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var snapshot = new CatalogProductSnapshot(
            product.Id,
            product.Sku,
            product.Barcode,
            product.NameAr,
            product.NameEn,
            product.ShortDescriptionAr,
            product.ShortDescriptionEn,
            product.BrandId,
            brand.NameAr,
            brand.NameEn,
            categories.Select(c => c.Id).ToArray(),
            categories.Select(c => c.NameAr).ToArray(),
            categories.Select(c => c.NameEn).ToArray(),
            product.AttributesJson,
            product.PriceHintMinorUnits,
            product.Restricted,
            product.RestrictionReasonCode,
            product.MarketCodes,
            product.VendorId,
            product.PublishedAt,
            primaryMedia?.StorageKey is null ? null : $"/v1/customer/catalog/media/{primaryMedia.StorageKey}",
            primaryMedia?.StorageKey is null ? null : $"/v1/customer/catalog/media/{primaryMedia.StorageKey}");

        // Delete first to ensure old market assignments are cleaned up.
        await DeleteFromAllIndexesAsync(searchEngine, product.Id, cancellationToken);

        foreach (var market in product.MarketCodes.Select(m => m.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IndexNames.TryResolve(market, "ar", out var arIndex)
                || !IndexNames.TryResolve(market, "en", out var enIndex))
            {
                continue;
            }

            var arProjection = ProductSearchProjectionMapper.FromCatalogProduct(snapshot, "ar", market, normalizer);
            var enProjection = ProductSearchProjectionMapper.FromCatalogProduct(snapshot, "en", market, normalizer);

            await searchEngine.UpsertAsync(arIndex.Name, [arProjection], cancellationToken);
            await searchEngine.UpsertAsync(enIndex.Name, [enProjection], cancellationToken);
        }
    }

    private static async Task DeleteFromAllIndexesAsync(ISearchEngine searchEngine, Guid productId, CancellationToken cancellationToken)
    {
        foreach (var index in IndexNames.All)
        {
            await searchEngine.DeleteAsync(index.Name, [productId], cancellationToken);
        }
    }

    private async Task AdvanceCursorAsync(
        SearchDbContext dbContext,
        string indexName,
        long outboxId,
        DateTimeOffset committedAt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lagSeconds = Math.Max(0, (int)(now - committedAt).TotalSeconds);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO search.search_indexer_cursor
                ("IndexName", "OutboxLastIdApplied", "LastSuccessAt", "LagSecondsLastObserved", "UpdatedAt")
            VALUES
                ({indexName}, 0, {now}, 0, {now})
            ON CONFLICT ("IndexName") DO NOTHING;
            """, cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            WITH locked_cursor AS (
                SELECT "IndexName"
                FROM search.search_indexer_cursor
                WHERE "IndexName" = {indexName}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE search.search_indexer_cursor AS c
            SET "OutboxLastIdApplied" = GREATEST(c."OutboxLastIdApplied", {outboxId}),
                "LastSuccessAt" = {now},
                "LagSecondsLastObserved" = {lagSeconds},
                "UpdatedAt" = {now}
            FROM locked_cursor
            WHERE c."IndexName" = locked_cursor."IndexName";
            """, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        _searchMetrics.ObserveIndexerLag(indexName, lagSeconds);
    }
}
