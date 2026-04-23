using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Search.Entities;
using BackendApi.Modules.Search.Persistence;
using BackendApi.Modules.Search.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BackendApi.Modules.Search.Admin.Reindex;

public sealed class SearchReindexService(
    IServiceScopeFactory scopeFactory,
    ILogger<SearchReindexService> logger)
{
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<SearchReindexService> _logger = logger;

    public async Task<ReindexStartResult> StartAsync(
        string? requestedIndex,
        Guid startedByAccountId,
        CancellationToken cancellationToken)
    {
        var rawIndex = requestedIndex?.Trim();
        if (string.IsNullOrWhiteSpace(rawIndex) || !IndexNames.TryParseIndex(rawIndex, out var index))
        {
            return ReindexStartResult.InvalidIndex();
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();

        var now = DateTimeOffset.UtcNow;
        var newJob = new ReindexJob
        {
            Id = Guid.NewGuid(),
            IndexName = index.Name,
            Status = "pending",
            StartedByAccountId = startedByAccountId,
            StartedAt = now,
            DocsWritten = 0,
        };

        db.ReindexJobs.Add(newJob);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var active = await db.ReindexJobs
                .AsNoTracking()
                .Where(x => x.IndexName == index.Name && (x.Status == "pending" || x.Status == "running"))
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (active is null)
            {
                return ReindexStartResult.Conflict(Guid.Empty);
            }

            return ReindexStartResult.Conflict(active.Id);
        }

        _ = Task.Run(() => RunJobAsync(newJob.Id, index), CancellationToken.None);
        return ReindexStartResult.Started(newJob.Id);
    }

    public async Task<ReindexJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
        return await db.ReindexJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
    }

    private async Task RunJobAsync(Guid jobId, SearchIndexConfig index)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var searchDb = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
            var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var searchEngine = scope.ServiceProvider.GetRequiredService<ISearchEngine>();
            var normalizer = scope.ServiceProvider.GetRequiredService<ArabicNormalizer>();

            var job = await searchDb.ReindexJobs.SingleAsync(x => x.Id == jobId);
            job.Status = "running";
            await searchDb.SaveChangesAsync();

            await searchEngine.EnsureIndexAsync(index, CancellationToken.None);
            await searchEngine.ClearIndexAsync(index.Name, CancellationToken.None);

            var baseQuery = catalogDb.Products
                .AsNoTracking()
                .Where(p => p.Status == "published" && p.MarketCodes.Contains(index.MarketCode));

            var docsExpected = await baseQuery.CountAsync();
            job.DocsExpected = docsExpected;
            job.DocsWritten = 0;
            await searchDb.SaveChangesAsync();

            for (var skip = 0; ; skip += BatchSize)
            {
                var products = await baseQuery
                    .OrderBy(x => x.Id)
                    .Skip(skip)
                    .Take(BatchSize)
                    .ToListAsync();

                if (products.Count == 0)
                {
                    break;
                }

                var brandIds = products.Select(x => x.BrandId).Distinct().ToArray();
                var brands = await catalogDb.Brands
                    .AsNoTracking()
                    .Where(b => brandIds.Contains(b.Id))
                    .ToDictionaryAsync(b => b.Id);

                var productIds = products.Select(x => x.Id).ToArray();
                var categories = await (
                        from pc in catalogDb.ProductCategories.AsNoTracking()
                        join c in catalogDb.Categories.AsNoTracking() on pc.CategoryId equals c.Id
                        where productIds.Contains(pc.ProductId)
                        select new { pc.ProductId, CategoryId = c.Id, c.NameAr, c.NameEn })
                    .ToListAsync();

                var media = await catalogDb.ProductMedia
                    .AsNoTracking()
                    .Where(m => productIds.Contains(m.ProductId) && m.IsPrimary)
                    .ToListAsync();

                var projections = new List<ProductSearchProjection>(products.Count);
                foreach (var product in products)
                {
                    if (!brands.TryGetValue(product.BrandId, out var brand))
                    {
                        continue;
                    }

                    var productCategories = categories.Where(c => c.ProductId == product.Id).ToArray();
                    var primaryMedia = media.FirstOrDefault(m => m.ProductId == product.Id);

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
                        productCategories.Select(c => c.CategoryId).ToArray(),
                        productCategories.Select(c => c.NameAr).ToArray(),
                        productCategories.Select(c => c.NameEn).ToArray(),
                        product.AttributesJson,
                        product.PriceHintMinorUnits,
                        product.Restricted,
                        product.RestrictionReasonCode,
                        product.MarketCodes,
                        product.VendorId,
                        product.PublishedAt,
                        primaryMedia?.StorageKey is null ? null : $"/v1/customer/catalog/media/{primaryMedia.StorageKey}",
                        primaryMedia?.StorageKey is null ? null : $"/v1/customer/catalog/media/{primaryMedia.StorageKey}");

                    projections.Add(ProductSearchProjectionMapper.FromCatalogProduct(snapshot, index.Locale, index.MarketCode, normalizer));
                }

                await searchEngine.UpsertAsync(index.Name, projections, CancellationToken.None);
                job.DocsWritten += projections.Count;
                await searchDb.SaveChangesAsync();
            }

            job.Status = "completed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await searchDb.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "search.reindex.job-failed jobId={JobId}", jobId);
            await MarkFailedAsync(jobId, ex);
        }
    }

    private async Task MarkFailedAsync(Guid jobId, Exception ex)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
            var job = await db.ReindexJobs.SingleOrDefaultAsync(x => x.Id == jobId);
            if (job is null)
            {
                return;
            }

            job.Status = "failed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Error = ex.Message;
            await db.SaveChangesAsync();
        }
        catch (Exception updateEx)
        {
            _logger.LogWarning(updateEx, "search.reindex.job-failed-marking-error jobId={JobId}", jobId);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed record ReindexStartResult(
    bool IsSuccess,
    bool IsConflict,
    bool IsInvalidIndex,
    Guid? JobId,
    Guid? ActiveJobId)
{
    public static ReindexStartResult Started(Guid jobId) => new(true, false, false, jobId, null);
    public static ReindexStartResult Conflict(Guid activeJobId) => new(false, true, false, null, activeJobId);
    public static ReindexStartResult InvalidIndex() => new(false, false, true, null, null);
}
