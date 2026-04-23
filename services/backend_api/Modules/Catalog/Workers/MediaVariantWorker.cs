using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Catalog.Workers;

/// <summary>
/// Polls <see cref="ProductMedia"/> rows with <c>VariantStatus = "pending"</c>, claims them so two
/// worker instances cannot double-process, and writes content-addressed variant descriptors to the
/// <c>variants</c> JSONB column. On success transitions the row to <c>"ready"</c>; after three
/// consecutive failures transitions to <c>"failed"</c> and audits a row for operator attention.
/// The binary re-encoding pass (via <see cref="IImageVariantGenerator"/>) will plug in when the
/// upload path persists original bytes through spec 003's <c>IStorageService</c> — until then the
/// worker writes only the variant path descriptors so downstream consumers (spec 006 indexer) can
/// render product cards without blocking on image processing.
/// </summary>
public sealed class MediaVariantWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MediaVariantWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private const int MaxAttempts = 3;
    private static readonly (string VariantName, string Format)[] VariantSpecs =
    [
        ("thumb", "jpeg"), ("thumb", "webp"),
        ("card", "jpeg"), ("card", "webp"),
        ("detail", "jpeg"), ("detail", "webp"),
        ("hero", "jpeg"), ("hero", "webp"),
    ];

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<MediaVariantWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "catalog.media-variant-worker.cycle-failed");
            }

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

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var paths = scope.ServiceProvider.GetRequiredService<ContentAddressedPaths>();
        var auditEventPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        var claimBefore = DateTimeOffset.UtcNow.Subtract(StaleClaimAfter);
        var pending = await dbContext.ProductMedia
            .Where(m => m.VariantStatus == "pending"
                        && (m.VariantClaimedAt == null || m.VariantClaimedAt < claimBefore))
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var claimStamp = DateTimeOffset.UtcNow;
        foreach (var media in pending)
        {
            media.VariantClaimedAt = claimStamp;
            media.VariantAttempts += 1;
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var media in pending)
        {
            try
            {
                var descriptors = new Dictionary<string, Dictionary<string, VariantDescriptor>>();
                foreach (var (variantName, format) in VariantSpecs)
                {
                    if (!descriptors.TryGetValue(variantName, out var byFormat))
                    {
                        byFormat = new Dictionary<string, VariantDescriptor>();
                        descriptors[variantName] = byFormat;
                    }
                    byFormat[format] = new VariantDescriptor(paths.VariantKey(media.ProductId, media.ContentSha256, variantName, format));
                }

                media.VariantsJson = JsonSerializer.Serialize(descriptors);
                media.VariantStatus = "ready";
                media.UpdatedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "catalog.media-variant.job-failed mediaId={MediaId} attempts={Attempts}", media.Id, media.VariantAttempts);
                if (media.VariantAttempts >= MaxAttempts)
                {
                    media.VariantStatus = "failed";
                    media.UpdatedAt = DateTimeOffset.UtcNow;
                    await auditEventPublisher.PublishAsync(
                        new AuditEvent(
                            ActorId: SystemActorId,
                            ActorRole: "system.catalog",
                            Action: "catalog.media.variant_failed",
                            EntityType: nameof(ProductMedia),
                            EntityId: media.Id,
                            BeforeState: null,
                            AfterState: new { media.Id, media.ProductId, media.VariantAttempts },
                            Reason: "catalog.media.variant.retry_budget_exhausted"),
                        cancellationToken);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record VariantDescriptor(string StorageKey);
}
