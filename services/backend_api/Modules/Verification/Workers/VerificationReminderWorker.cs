using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BackendApi.Modules.Verification.Workers;

/// <summary>
/// Spec 020 task T094 / research §R5 / FR-019. Daily worker that emits renewal
/// reminders for approved verifications whose expiry falls within an unfired
/// reminder window from the snapshotted schema.
///
/// <para>De-duplication invariant: <c>UNIQUE (verification_id, window_days)</c>
/// on <c>verification_reminders</c> guarantees each window fires at most once
/// per verification — even under horizontal-scale or back-window outages, the
/// worker's INSERT either succeeds or violates the unique index (in which case
/// the publish is skipped).</para>
///
/// <para>Back-window skip (R5): when the worker resumes from an outage and
/// finds two windows expired on the same verification, only the closest
/// unfired window fires. The other windows are recorded with
/// <c>skipped=true</c> + an audit note, so operators can explain "why didn't
/// customer X get the 14-day reminder?" — the answer is captured.</para>
/// </summary>
public sealed class VerificationReminderWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<VerificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<VerificationReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = options.Value.Reminder.Period;
        var firstDelay = options.Value.Reminder.FirstDelay(clock.GetUtcNow());
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
                logger.LogWarning(ex, "VerificationReminderWorker pass failed; will retry next tick.");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Single pass; public for test access. Returns the count of
    /// reminders fired (excluding skipped rows).</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        await using var lockHandle = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.ReminderWorker, ct);
        if (!lockHandle.Acquired)
        {
            logger.LogDebug("VerificationReminderWorker — lock held by peer instance; no-op pass.");
            return 0;
        }

        var auditPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var domainPublisher = scope.ServiceProvider.GetRequiredService<IVerificationDomainEventPublisher>();

        var nowUtc = clock.GetUtcNow();

        // Pull every approved row whose expiry is in the future (we don't fire
        // reminders for already-expired rows — the ExpiryWorker handles those).
        // The set is bounded by IX_verifications_expires_at (filter
        // "State = 'approved'").
        var candidates = await db.Verifications
            .AsNoTracking()
            .Where(v => v.State == VerificationState.Approved
                     && v.ExpiresAt != null
                     && v.ExpiresAt > nowUtc)
            .Select(v => new { v.Id, v.CustomerId, v.MarketCode, v.SchemaVersion, v.ExpiresAt })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        // Snapshot schemas (one read per (market, version) tuple) so each
        // candidate evaluates against the snapshotted reminder windows.
        var schemaKeys = candidates.Select(c => new { c.MarketCode, c.SchemaVersion }).Distinct().ToList();
        var schemas = new Dictionary<(string MarketCode, int Version), VerificationMarketSchema>();
        foreach (var key in schemaKeys)
        {
            var schema = await db.MarketSchemas
                .AsNoTracking()
                .Where(s => s.MarketCode == key.MarketCode && s.Version == key.SchemaVersion)
                .SingleOrDefaultAsync(ct);
            if (schema is not null)
            {
                schemas[(key.MarketCode, key.SchemaVersion)] = schema;
            }
        }

        var firedCount = 0;
        foreach (var c in candidates)
        {
            if (!schemas.TryGetValue((c.MarketCode, c.SchemaVersion), out var schema))
            {
                continue;
            }
            var windows = ParseReminderWindows(schema.ReminderWindowsDaysJson);
            if (windows.Count == 0)
            {
                continue;
            }

            // Already-fired windows for this verification (idempotency lookup).
            var fired = await db.Reminders
                .AsNoTracking()
                .Where(r => r.VerificationId == c.Id)
                .Select(r => r.WindowDays)
                .ToListAsync(ct);
            var firedSet = fired.ToHashSet();

            // A reminder window W "has passed its reminder time" when
            // (expires_at - now) <= W days — i.e., we're inside the window.
            // R5: of all windows whose reminder time has passed, fire only the
            // closest unfired (smallest W is closest to expiry); the rest are
            // recorded as skipped with an audit note for ops triage.
            var daysUntilExpiry = (c.ExpiresAt!.Value - nowUtc).TotalDays;
            var unfiredEligible = windows
                .Where(w => w >= daysUntilExpiry && !firedSet.Contains(w))
                .OrderBy(w => w)
                .ToList();

            if (unfiredEligible.Count == 0)
            {
                continue;
            }

            // Closest = smallest window_days that's still ≥ 0.
            var toFire = unfiredEligible[0];
            var toSkip = unfiredEligible.Skip(1).ToList();

            try
            {
                if (await TryFireAsync(scope.ServiceProvider, c.Id, c.CustomerId, c.MarketCode, c.ExpiresAt!.Value, toFire, nowUtc, auditPublisher, domainPublisher, ct))
                {
                    firedCount++;
                }
                foreach (var skipDays in toSkip)
                {
                    await TrySkipAsync(scope.ServiceProvider, c.Id, c.MarketCode, skipDays, toFire, nowUtc, auditPublisher, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reminder pass for verification {VerificationId} failed.", c.Id);
            }
        }

        if (firedCount > 0)
        {
            logger.LogInformation("VerificationReminderWorker fired {Count} reminder(s).", firedCount);
        }
        return firedCount;
    }

    private async Task<bool> TryFireAsync(
        IServiceProvider sp,
        Guid verificationId,
        Guid customerId,
        string marketCode,
        DateTimeOffset expiresAt,
        int windowDays,
        DateTimeOffset nowUtc,
        IAuditEventPublisher auditPublisher,
        IVerificationDomainEventPublisher domainPublisher,
        CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        db.Reminders.Add(new VerificationReminder
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = marketCode,
            WindowDays = windowDays,
            EmittedAt = nowUtc,
            Skipped = false,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsReminderUniqueViolation(ex))
        {
            // Another worker / earlier tick already fired this window — no-op.
            return false;
        }

        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: VerificationSystemActor.Id,
                ActorRole: "system",
                Action: "verification.reminder_emitted",
                EntityType: "verification",
                EntityId: verificationId,
                BeforeState: null,
                AfterState: new { window_days = windowDays, fired_at = nowUtc },
                Reason: "verification_reminder_due"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reminder fired but audit publish failed for verification {VerificationId}.", verificationId);
        }

        try
        {
            await domainPublisher.PublishAsync(new VerificationDomainEvents.VerificationReminderDue(
                VerificationId: verificationId,
                CustomerId: customerId,
                MarketCode: marketCode,
                WindowDays: windowDays,
                ExpiresAt: expiresAt,
                LocaleHint: "en"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reminder fired but domain publish failed for verification {VerificationId}.", verificationId);
        }

        return true;
    }

    private async Task TrySkipAsync(
        IServiceProvider sp,
        Guid verificationId,
        string marketCode,
        int skippedWindowDays,
        int firedWindowDays,
        DateTimeOffset nowUtc,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        db.Reminders.Add(new VerificationReminder
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = marketCode,
            WindowDays = skippedWindowDays,
            EmittedAt = nowUtc,
            Skipped = true,
            SkipReason = $"back_window_skip_in_favor_of_{firedWindowDays}_day_window",
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsReminderUniqueViolation(ex))
        {
            return;
        }

        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: VerificationSystemActor.Id,
                ActorRole: "system",
                Action: "verification.reminder_emitted",
                EntityType: "verification",
                EntityId: verificationId,
                BeforeState: null,
                AfterState: new
                {
                    window_days = skippedWindowDays,
                    skipped = true,
                    skip_reason = $"back_window_skip_in_favor_of_{firedWindowDays}_day_window",
                },
                Reason: "verification_reminder_skipped"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reminder skip recorded but audit publish failed for verification {VerificationId}.", verificationId);
        }
    }

    private static IReadOnlyList<int> ParseReminderWindows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<int>();
        try
        {
            var arr = JsonSerializer.Deserialize<int[]>(json);
            return arr is null ? Array.Empty<int>() : arr.Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();
        }
    }

    private static bool IsReminderUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
