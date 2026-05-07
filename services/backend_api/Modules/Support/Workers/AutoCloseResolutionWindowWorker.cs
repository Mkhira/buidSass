using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BackendApi.Modules.Support.Workers;

/// <summary>
/// Spec 023 Phase 10 T134 — auto-closes <c>resolved</c> tickets that have
/// passed the per-market <c>auto_close_after_resolved_days</c> grace window
/// (FR-023).
///
/// <para>Default cadence: hourly. Idempotent: a re-tick over an
/// already-closed ticket is a no-op (state-machine guard rejects). Advisory
/// lock guards against double-execution by replicas.</para>
/// </summary>
public sealed class AutoCloseResolutionWindowWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SupportWorkerOptions> options,
    TimeProvider clock,
    ILogger<AutoCloseResolutionWindowWorker> logger) : BackgroundService
{
    public Task ExecuteOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value.AutoCloseResolutionWindow;
        schedule.Validate($"{SupportWorkerOptions.SectionName}:AutoCloseResolutionWindow");

        if (schedule.InitialDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(schedule.InitialDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "AutoCloseResolutionWindowWorker pass failed; will retry next period.");
            }

            try { await Task.Delay(schedule.Period, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var dataSource = scope.ServiceProvider.GetService<NpgsqlDataSource>();

        await using var lockHandle = await SupportAdvisoryLock.TryAcquireAsync(
            db, SupportAdvisoryLock.Keys.AutoCloseResolutionWindow, ct, dataSource);
        if (!lockHandle.Acquired)
        {
            logger.LogInformation(
                "AutoCloseResolutionWindow advisory lock held by another instance; skipping pass.");
            return;
        }

        var nowUtc = clock.GetUtcNow();

        // Load per-market schemas once — there are only ~2 in V1, so this is
        // cheap and avoids per-ticket lookup.
        var schemas = await db.MarketSchemas.AsNoTracking().ToDictionaryAsync(s => s.MarketCode, ct);

        // CodeRabbit Loop-2: avoid the prior N+1 ticket-fetch pattern by
        // loading the full ticket rows up front. We still need tracked
        // entities (we mutate State + ClosedAtUtc + UpdatedAtUtc), so we
        // pull a tracked Where() rather than a projection-into-record.
        // Volume is bounded — auto-close runs hourly and only sees tickets
        // that have actually been in `resolved` for at least the per-market
        // window — so the working set is small.
        var candidates = await db.Tickets
            .Where(t => t.State == TicketStateNames.Resolved
                     && t.ResolvedAtUtc != null)
            .ToListAsync(ct);

        var closed = 0;
        foreach (var ticket in candidates)
        {
            // Re-check: state may have flipped (reopen) between scan and now.
            // The Where() above filtered on the candidate set but EF tracks
            // mutations, so a parallel reopen during this pass is still
            // possible. Refusing the transition is the safe default.
            if (ticket.State != TicketStateNames.Resolved) continue;
            if (ticket.ResolvedAtUtc is null) continue;

            var schema = schemas.TryGetValue(ticket.MarketCode, out var s) ? s : null;
            var policy = schema is null
                ? SupportMarketPolicy.DefaultFor(ticket.MarketCode)
                : new SupportMarketPolicy(
                    schema.MarketCode,
                    schema.AutoAssignmentEnabled,
                    schema.ReopenWindowDays,
                    schema.MaxReopenCount,
                    schema.AutoCloseAfterResolvedDays,
                    schema.AttachmentMaxPerTicket,
                    schema.AttachmentMaxSizeMb,
                    schema.AttachmentCumulativeMaxMb,
                    schema.AllowedMimeTypes);

            // CodeRabbit Loop-2: respect per-market policy disablement
            // (AutoCloseAfterResolvedDays <= 0). Skipping the threshold math
            // entirely when disabled also avoids creating a misleading
            // candidate.ResolvedAtUtc.AddDays(0) threshold that always fires.
            if (!policy.AutoCloseEnabled) continue;

            var threshold = ticket.ResolvedAtUtc.Value.AddDays(policy.AutoCloseAfterResolvedDays);
            if (nowUtc < threshold) continue;

            var fromState = TicketStateNames.FromWire(ticket.State);
            if (!TicketStateMachine.TryTransition(
                    fromState, TicketState.Closed,
                    TicketTriggerKind.AutoCloseResolutionWindow,
                    TicketActorKind.System, out _))
            {
                continue;
            }

            ticket.State = TicketStateNames.ToWire(TicketState.Closed);
            ticket.ClosedAtUtc = nowUtc;
            ticket.UpdatedAtUtc = nowUtc;

            db.Messages.Add(new TicketMessage
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Kind = TicketMessageKindNames.SystemEvent,
                ActorId = null,
                ActorRole = TicketActorKindNames.System,
                Body = null,
                BodyLocale = null,
                LeadIntervention = false,
                CreatedAtUtc = nowUtc,
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Clear the entire ChangeTracker, not just the ticket entry —
                // we also added a TicketMessage above that would otherwise
                // leak into the next iteration's SaveChangesAsync and corrupt
                // the audit trail of an unrelated ticket. (CodeRabbit Loop-1.)
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "AutoCloseResolutionWindow optimistic-concurrency miss on ticket {TicketId}; will retry next pass.",
                    ticket.Id);
                continue;
            }

            try
            {
                await publisher.Publish(new TicketStateChanged(
                    TicketId: ticket.Id,
                    FromState: TicketStateNames.ToWire(fromState),
                    ToState: TicketStateNames.ToWire(TicketState.Closed),
                    TriggeredBy: TicketTriggerKind.AutoCloseResolutionWindow,
                    OccurredAtUtc: nowUtc), ct);

                await publisher.Publish(new TicketClosed(
                    TicketId: ticket.Id,
                    TriggeredBy: TicketTriggerKind.AutoCloseResolutionWindow,
                    ClosedAtUtc: nowUtc), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "AutoCloseResolutionWindow event publish failed for ticket {TicketId}; row already persisted.",
                    ticket.Id);
            }

            closed++;
        }

        if (closed > 0)
        {
            logger.LogInformation(
                "AutoCloseResolutionWindow pass complete at {NowUtc}: {Count} ticket(s) auto-closed.",
                nowUtc, closed);
        }
    }

    // AutoCloseCandidate removed in Loop-2 fix (CodeRabbit) — the worker now
    // pulls full tracked SupportTicket rows in a single Where() query instead
    // of projecting into a record + re-fetching per-candidate.
}
