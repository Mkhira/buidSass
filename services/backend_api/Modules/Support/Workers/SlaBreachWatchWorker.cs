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
/// Spec 023 US4 — SLA breach detection worker (FR-022, SC-005, SC-006).
///
/// <para>Runs every 60s. For every non-closed ticket whose first-response or
/// resolution deadline has elapsed and has not yet been acknowledged on the
/// ticket row, inserts an append-only <see cref="TicketSlaBreachEvent"/>,
/// stamps the matching <c>breach_acknowledged_at_*</c> column, and publishes
/// the corresponding domain event.</para>
///
/// <para>Idempotency (SC-006): the worker filters by the unstamped
/// acknowledgment timestamp, so a re-tick after a successful pass produces
/// no new rows. After reopen the originating handler clears the
/// acknowledgment + sets <c>superseded_by_event_id</c> on the prior breach
/// row, which lets a fresh breach record under the same
/// <c>(ticket_id, breach_kind)</c> partition (PK includes
/// <c>detected_at_utc</c>).</para>
///
/// <para>Concurrency: a session-scoped <c>pg_try_advisory_lock</c> guards
/// against double-execution by replicas. Lock acquisition failure is logged
/// and the pass exits cleanly.</para>
/// </summary>
public sealed class SlaBreachWatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SupportWorkerOptions> options,
    TimeProvider clock,
    ILogger<SlaBreachWatchWorker> logger) : BackgroundService
{
    /// <summary>Synchronous entry point for tests (bypasses scheduling).</summary>
    public Task ExecuteOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value.SlaBreachWatch;
        schedule.Validate($"{SupportWorkerOptions.SectionName}:SlaBreachWatch");

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
                    "SlaBreachWatchWorker pass failed; will retry next period.");
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
            db, SupportAdvisoryLock.Keys.SlaBreachWatch, ct, dataSource);
        if (!lockHandle.Acquired)
        {
            logger.LogInformation(
                "SlaBreachWatch advisory lock held by another instance; skipping pass.");
            return;
        }

        var nowUtc = clock.GetUtcNow();

        // Scan path: idx_tickets_breach_scan covers (state, first_response_due_utc,
        // resolution_due_utc) filtered to state NOT IN ('closed') per data-model §4.
        // We pull both candidates in one query and split client-side; the cardinality
        // is small (peak ~5 breaches / hour per spec §11) so this is fine.
        var firstResponseCandidates = await db.Tickets
            .Where(t => t.State != TicketStateNames.Closed
                     && t.BreachAcknowledgedAtFirstResponse == null
                     && t.FirstResponseDueUtc <= nowUtc)
            .Select(t => new BreachCandidate(
                t.Id,
                t.AssignedAgentId,
                t.FirstResponseDueUtc,
                t.ReopenedAtUtc))
            .ToListAsync(ct);

        var resolutionCandidates = await db.Tickets
            .Where(t => t.State != TicketStateNames.Closed
                     && t.BreachAcknowledgedAtResolution == null
                     && t.ResolutionDueUtc <= nowUtc)
            .Select(t => new BreachCandidate(
                t.Id,
                t.AssignedAgentId,
                t.ResolutionDueUtc,
                t.ReopenedAtUtc))
            .ToListAsync(ct);

        if (firstResponseCandidates.Count == 0 && resolutionCandidates.Count == 0)
        {
            return;
        }

        var firstResponseEmitted = await ProcessBatchAsync(
            db, publisher, firstResponseCandidates, TicketSlaBreachKind.FirstResponse, nowUtc, ct);

        var resolutionEmitted = await ProcessBatchAsync(
            db, publisher, resolutionCandidates, TicketSlaBreachKind.Resolution, nowUtc, ct);

        logger.LogInformation(
            "SlaBreachWatch pass complete at {NowUtc}: {FirstResponseCount} first-response breach(es), {ResolutionCount} resolution breach(es) emitted.",
            nowUtc, firstResponseEmitted, resolutionEmitted);
    }

    private async Task<int> ProcessBatchAsync(
        SupportDbContext db,
        IPublisher publisher,
        IReadOnlyList<BreachCandidate> candidates,
        string breachKind,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (candidates.Count == 0) return 0;

        var emitted = 0;
        foreach (var candidate in candidates)
        {
            // Idempotency guard at the row level — another replica or a prior
            // partial pass may have stamped the acknowledgment already.
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == candidate.TicketId, ct);
            if (ticket is null) continue;

            var alreadyAcknowledged = breachKind == TicketSlaBreachKind.FirstResponse
                ? ticket.BreachAcknowledgedAtFirstResponse is not null
                : ticket.BreachAcknowledgedAtResolution is not null;
            if (alreadyAcknowledged) continue;

            // Re-check that the deadline is still elapsed — a lead override may
            // have moved it forward in flight.
            var dueAt = breachKind == TicketSlaBreachKind.FirstResponse
                ? ticket.FirstResponseDueUtc
                : ticket.ResolutionDueUtc;
            if (dueAt > nowUtc) continue;

            var eventId = Guid.NewGuid();
            db.SlaBreachEvents.Add(new TicketSlaBreachEvent
            {
                Id = eventId,
                TicketId = ticket.Id,
                BreachKind = breachKind,
                DetectedAtUtc = nowUtc,
                TargetDueUtc = dueAt,
                EventEmitted = true,
                SupersededByEventId = null,
            });

            // Per data-model §4: on re-breach after reopen, point any prior
            // unsuperseded event row at the new event id so audit history
            // captures the supersession chain. The acknowledgment-clear path
            // in ReopenTicket guarantees this only fires when reopen happened.
            var priorEvents = await db.SlaBreachEvents
                .Where(e => e.TicketId == ticket.Id
                         && e.BreachKind == breachKind
                         && e.Id != eventId
                         && e.SupersededByEventId == null
                         && e.DetectedAtUtc < nowUtc)
                .ToListAsync(ct);
            foreach (var prior in priorEvents)
            {
                prior.SupersededByEventId = eventId;
            }

            if (breachKind == TicketSlaBreachKind.FirstResponse)
            {
                ticket.BreachAcknowledgedAtFirstResponse = nowUtc;
            }
            else
            {
                ticket.BreachAcknowledgedAtResolution = nowUtc;
            }
            ticket.UpdatedAtUtc = nowUtc;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                // Composite-PK collision — another replica raced us within the same
                // detected_at_utc tick. Treat as already-emitted and continue.
                db.Entry(ticket).State = EntityState.Detached;
                logger.LogInformation(
                    "SlaBreachWatch race lost on (ticket_id={TicketId}, breach_kind={BreachKind}); already emitted.",
                    ticket.Id, breachKind);
                continue;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Ticket row changed mid-flight (another transition / override);
                // skip — the next tick will re-evaluate.
                db.Entry(ticket).State = EntityState.Detached;
                logger.LogInformation(
                    "SlaBreachWatch optimistic-concurrency miss on ticket {TicketId}; will retry next pass.",
                    ticket.Id);
                continue;
            }

            // Emit the kind-specific event. Failures here do not roll back the
            // persisted breach row — the ack stamp is the durable signal.
            try
            {
                if (breachKind == TicketSlaBreachKind.FirstResponse)
                {
                    await publisher.Publish(new TicketSlaBreachedFirstResponse(
                        TicketId: ticket.Id,
                        TargetDueUtc: dueAt,
                        AgentId: candidate.AssignedAgentId,
                        DetectedAtUtc: nowUtc), ct);
                }
                else
                {
                    await publisher.Publish(new TicketSlaBreachedResolution(
                        TicketId: ticket.Id,
                        TargetDueUtc: dueAt,
                        AgentId: candidate.AssignedAgentId,
                        DetectedAtUtc: nowUtc), ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "SlaBreachWatch event publish failed for ticket {TicketId} ({BreachKind}); breach row already persisted.",
                    ticket.Id, breachKind);
            }

            emitted++;
        }

        return emitted;
    }

    private sealed record BreachCandidate(
        Guid TicketId,
        Guid? AssignedAgentId,
        DateTimeOffset DueAtUtc,
        DateTimeOffset? ReopenedAtUtc);
}
