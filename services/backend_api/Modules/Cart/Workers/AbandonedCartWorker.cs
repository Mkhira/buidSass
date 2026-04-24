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
        // 24h window even though it's a fresh idle episode (FR-010 idle-timer reset).
        var resumedEmissions = await (
                from e in db.Set<CartAbandonedEmission>()
                join c in db.Carts on e.CartId equals c.Id
                where c.LastTouchedAt > e.LastEmittedAt
                select e
            ).ToListAsync(ct);
        if (resumedEmissions.Count > 0)
        {
            db.Set<CartAbandonedEmission>().RemoveRange(resumedEmissions);
            await db.SaveChangesAsync(ct);
        }

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
            // CR #11: persist the dedupe marker BEFORE publishing the event. If the publish
            // fails the marker stays so we don't re-emit; if two workers race on the same cart
            // the second SaveChanges hits the PK uniqueness on CartAbandonedEmission.CartId (or
            // ends up as a conditional update of LastEmittedAt past dedupe cutoff) — either way
            // only one audit row fires per dedupe window.
            var emission = await db.Set<CartAbandonedEmission>()
                .SingleOrDefaultAsync(e => e.CartId == cart.Id, ct);
            if (emission is null)
            {
                db.Add(new CartAbandonedEmission { CartId = cart.Id, MarketCode = cart.MarketCode, LastEmittedAt = now });
            }
            else
            {
                emission.LastEmittedAt = now;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
            {
                // Another worker claimed this cart first — skip publishing and move on.
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "cart.abandonment.claim_lost cartId={CartId} — another worker owns this dedupe window.",
                    cart.Id);
                continue;
            }

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
    }
}
