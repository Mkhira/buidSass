using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Workers;

/// <summary>
/// Emits at most one `cart.abandoned` audit event per cart per AbandonmentDedupeHours window
/// (FR-016 / SC-004), and only for carts with ≥1 line and a known account (FR-010). When a
/// cart resumes (LastTouchedAt > LastEmittedAt), the emission row is cleared so a future
/// idle window can trigger again (FR-010 idle-timer reset).
/// </summary>
public sealed class AbandonedCartWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CartOptions> options,
    ILogger<AbandonedCartWorker> logger) : BackgroundService
{
    // Workers use a sentinel actor id so AuditEventPublisher.Validate accepts the event.
    private static readonly Guid WorkerActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "cart.abandoned-worker.cycle-failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.AbandonmentWorkerIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
        var auditPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var opts = options.Value;

        var now = DateTimeOffset.UtcNow;
        var idleCutoff = now.AddMinutes(-opts.AbandonmentIdleMinutes);

        // Step 1: clear stale emissions where the cart resumed after the previous emission.
        // Without this, a resumed cart cannot trigger a second abandonment event in the same
        // 24h window even though it's a fresh idle episode (FR-010 idle-timer reset). This
        // runs as a single set-based DELETE so a long backlog can't blow up worker memory.
        await db.Set<CartAbandonedEmission>()
            .Where(e => db.Carts.Any(c => c.Id == e.CartId && c.LastTouchedAt > e.LastEmittedAt))
            .ExecuteDeleteAsync(ct);

        // Step 2: candidate query. Must be (a) active, (b) authenticated (no addressable
        // channel without email — FR-010), (c) idle past cutoff, (d) has ≥1 line, (e) no
        // prior emission within the dedupe window.
        var dedupeCutoff = now.AddHours(-opts.AbandonmentDedupeHours);
        var candidates = await (
                from c in db.Carts
                where c.Status == CartStatuses.Active
                      && c.AccountId != null
                      && c.LastTouchedAt < idleCutoff
                      && db.CartLines.Any(l => l.CartId == c.Id)
                let lastEmit = db.Set<CartAbandonedEmission>()
                    .Where(e => e.CartId == c.Id)
                    .Select(e => (DateTimeOffset?)e.LastEmittedAt)
                    .FirstOrDefault()
                where lastEmit == null || lastEmit < dedupeCutoff
                // Deterministic ordering prevents starvation — oldest-idle first — and gives
                // concurrent workers a stable claim order so their 23505 races stay bounded.
                orderby c.LastTouchedAt, c.Id
                select c
            ).Take(200).ToListAsync(ct);

        if (candidates.Count == 0) return;

        foreach (var cart in candidates)
        {
            // CR pass 3 — atomic claim via conditional update. Previous SELECT+UPDATE could
            // lose its way between the read and the save; ExecuteUpdateAsync with a
            // `LastEmittedAt < dedupeCutoff` predicate commits only one worker's claim per
            // dedupe window. If zero rows matched we try an insert (first-time claim); a
            // concurrent insert loses on the PK and we skip the publish.
            bool claimed;
            try
            {
                var rowsUpdated = await db.Set<CartAbandonedEmission>()
                    .Where(e => e.CartId == cart.Id && e.LastEmittedAt < dedupeCutoff)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastEmittedAt, now), ct);

                if (rowsUpdated == 1)
                {
                    claimed = true;
                }
                else
                {
                    // No existing row past dedupe — insert the first-time claim. PK uniqueness
                    // on CartId makes this atomic; a losing concurrent insert throws 23505.
                    db.Add(new CartAbandonedEmission
                    {
                        CartId = cart.Id,
                        MarketCode = cart.MarketCode,
                        LastEmittedAt = now,
                    });
                    await db.SaveChangesAsync(ct);
                    claimed = true;
                }
            }
            catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
            {
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "cart.abandonment.claim_lost cartId={CartId} — another worker owns this dedupe window.",
                    cart.Id);
                continue;
            }

            if (!claimed) continue;

            try
            {
                await auditPublisher.PublishAsync(new AuditEvent(
                    WorkerActorId,
                    "system",
                    "cart.abandoned",
                    nameof(Entities.Cart),
                    cart.Id,
                    null,
                    new { cart.Id, cart.AccountId, cart.MarketCode, cart.LastTouchedAt },
                    "cart.abandonment.worker"), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The claim is committed but the publish failed — without compensation this cart
                // would be deduped until the window expires, silently dropping the event. Roll
                // the claim back by pushing LastEmittedAt below any future dedupeCutoff so the
                // next tick re-claims + re-emits. Any further failure is logged so operators can
                // reconcile manually (spec 019 will replace this with a transactional outbox).
                logger.LogError(ex,
                    "cart.abandonment.publish_failed cartId={CartId} — rolling back claim so the next tick retries.",
                    cart.Id);
                try
                {
                    await db.Set<CartAbandonedEmission>()
                        .Where(e => e.CartId == cart.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastEmittedAt, DateTimeOffset.MinValue), ct);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogError(rollbackEx,
                        "cart.abandonment.claim_rollback_failed cartId={CartId} — event permanently dropped for this dedupe window.",
                        cart.Id);
                }
            }
        }
    }
}
