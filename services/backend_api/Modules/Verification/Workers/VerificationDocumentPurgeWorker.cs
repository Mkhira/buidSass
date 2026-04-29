using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Storage;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Verification.Workers;

/// <summary>
/// Spec 020 task T095. Daily worker that purges document bodies once the
/// per-market retention window has elapsed.
///
/// <para>For each <c>verification_documents</c> row where
/// <c>purge_after &lt;= now AND purged_at IS NULL</c>:</para>
/// <list type="number">
///   <item>Call <see cref="IStorageService.DeleteAsync"/> on the
///         <c>storage_key</c> (silently no-op if storage already deleted).</item>
///   <item>Set <c>purged_at = now</c> and <c>storage_key = null</c>.</item>
///   <item>Emit <c>verification.document_purged</c> audit event so
///         downstream <c>OpenHistoricalDocument</c> can return a
///         <c>410 Gone</c> with the expected reason code.</item>
/// </list>
///
/// <para>The row itself is preserved for audit linkage — only the document
/// body is destroyed.</para>
/// </summary>
public sealed class VerificationDocumentPurgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<VerificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<VerificationDocumentPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = options.Value.DocumentPurge.Period;
        var firstDelay = options.Value.DocumentPurge.FirstDelay(clock.GetUtcNow());
        if (firstDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(firstDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "VerificationDocumentPurgeWorker pass failed; will retry next tick.");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Single pass; public for test access. Returns the count of
    /// documents purged.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        await using var lockHandle = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.DocumentPurgeWorker, ct);
        if (!lockHandle.Acquired)
        {
            logger.LogDebug("VerificationDocumentPurgeWorker — lock held by peer instance; no-op pass.");
            return 0;
        }

        var auditPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var nowUtc = clock.GetUtcNow();

        var dueIds = await db.Documents
            .AsNoTracking()
            .Where(d => d.PurgeAfter != null && d.PurgeAfter <= nowUtc && d.PurgedAt == null)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var purgedCount = 0;
        foreach (var documentId in dueIds)
        {
            try
            {
                if (await PurgeOneAsync(scope.ServiceProvider, storage, auditPublisher, documentId, nowUtc, ct))
                {
                    purgedCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to purge document {DocumentId}; will retry next tick.", documentId);
            }
        }

        if (purgedCount > 0)
        {
            logger.LogInformation("VerificationDocumentPurgeWorker purged {Count} document body(ies).", purgedCount);
        }
        return purgedCount;
    }

    private async Task<bool> PurgeOneAsync(
        IServiceProvider sp,
        IStorageService storage,
        IAuditEventPublisher auditPublisher,
        Guid documentId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        await using var rowScope = sp.CreateAsyncScope();
        var db = rowScope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        var doc = await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null || doc.PurgedAt != null
            || doc.PurgeAfter is null || doc.PurgeAfter > nowUtc)
        {
            // Idempotent guard — another instance / tick already purged it.
            return false;
        }

        var storageKeyAtPurge = doc.StorageKey;
        if (!string.IsNullOrWhiteSpace(storageKeyAtPurge))
        {
            try
            {
                await storage.DeleteAsync(storageKeyAtPurge, ct);
            }
            catch (Exception ex)
            {
                // If storage delete fails we still mark the row purged — the
                // body lifetime is governed by the retention window, not the
                // storage call's success. Log loudly so ops can investigate.
                logger.LogWarning(ex,
                    "Storage delete failed for document {DocumentId} (storage_key={StorageKey}); marking row purged anyway.",
                    documentId, storageKeyAtPurge);
            }
        }

        doc.PurgedAt = nowUtc;
        doc.StorageKey = null;
        await db.SaveChangesAsync(ct);

        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: VerificationSystemActor.Id,
                ActorRole: "system",
                Action: "verification.document_purged",
                EntityType: "verification.document",
                EntityId: doc.Id,
                BeforeState: new { storage_key = storageKeyAtPurge },
                AfterState: new { purged_at = nowUtc, storage_key = (string?)null },
                Reason: "retention_window_elapsed"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document {DocumentId} purged but audit publish failed.", documentId);
        }

        return true;
    }
}
