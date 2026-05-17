using BackendApi.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T045 (worker part) — daily archiver. Dead-letter rows resolved more than
/// 30 days ago (clarify-locked retention) are moved into the archive table.
/// The archive preserves the notification body for forensic replay but is
/// outside the 30-day operator queue. Older-than-365d archive rows are then
/// purged in a second pass to keep the archive bounded.
/// </summary>
public sealed class DeadLetterArchiver : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DeadLetterArchiver> _logger;
    private readonly TimeProvider _clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan ArchiveAfter = TimeSpan.FromDays(30);
    private static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(365);

    public DeadLetterArchiver(
        IServiceScopeFactory scopes,
        ILogger<DeadLetterArchiver> logger,
        TimeProvider clock)
    {
        _scopes = scopes;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DeadLetterArchiver iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = _clock.GetUtcNow();

        // Move resolved rows older than 30 days. The DeadLetterArchive table
        // schema mirrors DeadLetterEntry; the migration created it; the EF
        // entity DeadLetterArchive backs it.
        var toArchive = await db.DeadLetterEntries
            .Where(d => d.ResolvedAt != null && d.ResolvedAt < now - ArchiveAfter)
            .Take(500)
            .ToListAsync(ct);

        foreach (var row in toArchive)
        {
            db.DeadLetterArchive.Add(new Domain.DeadLetterArchive
            {
                NotificationId = row.NotificationId,
                LastErrorMessageRedacted = row.LastErrorMessageRedacted,
                EnteredAt = row.EnteredAt,
                ResolvedAt = row.ResolvedAt,
                Resolution = row.Resolution,
                ResolvedBy = row.ResolvedBy,
                ArchivedAt = now,
            });
            db.DeadLetterEntries.Remove(row);
        }

        // Purge archived rows older than 365 days.
        var toPurge = await db.DeadLetterArchive
            .Where(a => a.ArchivedAt < now - PurgeAfter)
            .Take(500)
            .ToListAsync(ct);
        db.DeadLetterArchive.RemoveRange(toPurge);

        if (toArchive.Count > 0 || toPurge.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
