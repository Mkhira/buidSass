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

        // Candidate tickets: state=resolved with a non-null resolved_at_utc
        // older than the per-market window. We compute the window in-process
        // because schemas are small and Postgres doesn't have direct access
        // to the per-market knob table from a single SQL filter.
        var candidates = await db.Tickets
            .Where(t => t.State == TicketStateNames.Resolved
                     && t.ResolvedAtUtc != null)
            .Select(t => new AutoCloseCandidate(
                t.Id,
                t.MarketCode,
                t.ResolvedAtUtc!.Value))
            .ToListAsync(ct);

        var closed = 0;
        foreach (var candidate in candidates)
        {
            var schema = schemas.TryGetValue(candidate.MarketCode, out var s) ? s : null;
            var policy = schema is null
                ? SupportMarketPolicy.DefaultFor(candidate.MarketCode)
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

            var threshold = candidate.ResolvedAtUtc.AddDays(policy.AutoCloseAfterResolvedDays);
            if (nowUtc < threshold) continue;

            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == candidate.TicketId, ct);
            if (ticket is null) continue;

            // Re-check: state may have flipped (reopen) between candidate scan
            // and now. Refusing the transition is the safe default.
            if (ticket.State != TicketStateNames.Resolved) continue;
            if (ticket.ResolvedAtUtc is null) continue;
            if (nowUtc < ticket.ResolvedAtUtc.Value.AddDays(policy.AutoCloseAfterResolvedDays)) continue;

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
                db.Entry(ticket).State = EntityState.Detached;
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

    private sealed record AutoCloseCandidate(
        Guid TicketId,
        string MarketCode,
        DateTimeOffset ResolvedAtUtc);
}
