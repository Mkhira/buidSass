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
/// Spec 023 Phase 10 T135 — nightly reclaim of tickets whose assigned agent
/// has been offboarded in identity. Supersedes the active assignment with
/// <see cref="TicketAssignmentSupersededReason.ReclaimedOffboard"/>, clears
/// the denormalized <see cref="SupportTicket.AssignedAgentId"/>, appends a
/// <c>system_event</c> message, and emits <see cref="TicketReassigned"/> with
/// a null new agent id (queue-routed).
///
/// <para>Activity is queried via <see cref="ISupportAgentActivityQuery"/> —
/// the production binding lives in spec 004 Identity. The default fallback
/// makes this worker a no-op until that binding is wired.</para>
/// </summary>
public sealed class OrphanedAssignmentReclaimWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SupportWorkerOptions> options,
    TimeProvider clock,
    ILogger<OrphanedAssignmentReclaimWorker> logger) : BackgroundService
{
    public Task ExecuteOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value.OrphanedAssignmentReclaim;
        schedule.Validate($"{SupportWorkerOptions.SectionName}:OrphanedAssignmentReclaim");

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
                    "OrphanedAssignmentReclaimWorker pass failed; will retry next period.");
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
        var activity = scope.ServiceProvider.GetRequiredService<ISupportAgentActivityQuery>();
        var dataSource = scope.ServiceProvider.GetService<NpgsqlDataSource>();

        await using var lockHandle = await SupportAdvisoryLock.TryAcquireAsync(
            db, SupportAdvisoryLock.Keys.OrphanedAssignmentReclaim, ct, dataSource);
        if (!lockHandle.Acquired)
        {
            logger.LogInformation(
                "OrphanedAssignmentReclaim advisory lock held by another instance; skipping pass.");
            return;
        }

        var assignedAgents = await db.Tickets
            .Where(t => t.AssignedAgentId != null
                     && t.State != TicketStateNames.Closed)
            .Select(t => t.AssignedAgentId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (assignedAgents.Count == 0) return;

        var inactiveAgents = new List<Guid>();
        foreach (var agentId in assignedAgents)
        {
            if (!await activity.IsAgentActiveAsync(agentId, ct))
            {
                inactiveAgents.Add(agentId);
            }
        }

        if (inactiveAgents.Count == 0) return;

        var nowUtc = clock.GetUtcNow();
        var reclaimed = 0;

        foreach (var agentId in inactiveAgents)
        {
            var orphans = await db.Tickets
                .Where(t => t.AssignedAgentId == agentId
                         && t.State != TicketStateNames.Closed)
                .ToListAsync(ct);

            foreach (var ticket in orphans)
            {
                var prior = await db.Assignments
                    .Where(a => a.TicketId == ticket.Id && a.SupersededAtUtc == null)
                    .ToListAsync(ct);
                foreach (var p in prior)
                {
                    p.SupersededAtUtc = nowUtc;
                    p.SupersededReason = TicketAssignmentSupersededReason.ReclaimedOffboard;
                }

                ticket.AssignedAgentId = null;
                ticket.UpdatedAtUtc = nowUtc;

                db.Messages.Add(new TicketMessage
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Kind = TicketMessageKindNames.SystemEvent,
                    ActorId = null,
                    ActorRole = TicketActorKindNames.System,
                    Body = "Assigned agent is no longer active; reclaimed to queue.",
                    BodyLocale = ticket.Locale,
                    LeadIntervention = false,
                    CreatedAtUtc = nowUtc,
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Clear the entire ChangeTracker — the prior-assignment
                    // supersession edits + the audit TicketMessage we added
                    // above must NOT leak into the next iteration's
                    // SaveChangesAsync, otherwise we'd corrupt the assignment
                    // history + audit trail of unrelated tickets.
                    // (CodeRabbit Loop-1 finding.)
                    db.ChangeTracker.Clear();
                    logger.LogInformation(
                        "OrphanedAssignmentReclaim concurrency miss on ticket {TicketId}; will retry next pass.",
                        ticket.Id);
                    continue;
                }

                try
                {
                    // Use TicketReassigned with PriorAgentId=offboarded, NewAgentId=Empty
                    // so spec 025 (notifications) can detect the queue-route case.
                    await publisher.Publish(new TicketReassigned(
                        TicketId: ticket.Id,
                        PriorAgentId: agentId,
                        NewAgentId: Guid.Empty,
                        JustificationNote: "agent_offboarded",
                        OccurredAtUtc: nowUtc), ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "OrphanedAssignmentReclaim event publish failed for ticket {TicketId}.",
                        ticket.Id);
                }

                reclaimed++;
            }
        }

        logger.LogInformation(
            "OrphanedAssignmentReclaim pass complete at {NowUtc}: {Count} ticket(s) reclaimed across {AgentCount} offboarded agent(s).",
            nowUtc, reclaimed, inactiveAgents.Count);
    }
}
