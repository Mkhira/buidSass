using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.ClaimTicket;

public sealed record ClaimTicketCommand(Guid TicketId, Guid AgentId, uint? IfMatchRowVersion);

public sealed record ClaimTicketResult(
    bool Success,
    Guid? AssignmentId,
    string? NewState,
    string? ReasonCode,
    string? Detail);

/// <summary>
/// Optimistic-concurrency-guarded claim per FR-017.
/// Two agents racing on the same open ticket: the loser gets
/// <see cref="TicketReasonCode.AssignmentConflict"/>.
/// </summary>
public sealed class ClaimTicketHandler
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ClaimTicketHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ClaimTicketResult> HandleAsync(ClaimTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }

        if (cmd.IfMatchRowVersion is not null && ticket.Xmin != cmd.IfMatchRowVersion.Value)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket version mismatch; reload before claiming.");
        }

        // Already claimed by another agent — fast-path conflict before DB insert.
        if (ticket.AssignedAgentId is not null && ticket.AssignedAgentId != cmd.AgentId)
        {
            return Failure(TicketReasonCode.AssignmentConflict,
                "Ticket has already been claimed by another agent.");
        }

        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal, "Ticket is closed.");
        }

        var fromState = TicketStateNames.FromWire(ticket.State);
        var toState = fromState == TicketState.Open ? TicketState.InProgress : fromState;

        if (fromState == TicketState.Open && !TicketStateMachine.TryTransition(
                fromState, toState, TicketTriggerKind.AgentClaim, TicketActorKind.Agent,
                out var reason))
        {
            return Failure(reason ?? TicketReasonCode.InvalidTransition,
                $"Claim not allowed in state '{ticket.State}'.");
        }

        var nowUtc = _clock.GetUtcNow();
        var assignmentId = Guid.NewGuid();

        _db.Assignments.Add(new TicketAssignment
        {
            Id = assignmentId,
            TicketId = ticket.Id,
            AgentId = cmd.AgentId,
            AssignmentKind = TicketAssignmentKind.SelfClaim,
            AssignedByActorId = cmd.AgentId,
            AssignedAtUtc = nowUtc,
        });

        ticket.AssignedAgentId = cmd.AgentId;
        ticket.UpdatedAtUtc = nowUtc;

        var transitioned = false;
        if (fromState == TicketState.Open)
        {
            ticket.State = TicketStateNames.ToWire(toState);
            transitioned = true;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            // Partial unique index UX_ticket_assignments_active_per_ticket — race lost.
            return Failure(TicketReasonCode.AssignmentConflict,
                "Ticket has already been claimed by another agent.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket was modified concurrently; retry the request.");
        }

        await _publisher.Publish(new TicketAssigned(
            TicketId: ticket.Id,
            AgentId: cmd.AgentId,
            AssignmentKind: TicketAssignmentKind.SelfClaim,
            OccurredAtUtc: nowUtc), ct);

        if (transitioned)
        {
            await _publisher.Publish(new TicketStateChanged(
                TicketId: ticket.Id,
                FromState: TicketStateNames.ToWire(fromState),
                ToState: TicketStateNames.ToWire(toState),
                TriggeredBy: TicketTriggerKind.AgentClaim,
                OccurredAtUtc: nowUtc), ct);
        }

        return new ClaimTicketResult(true, assignmentId, ticket.State, null, null);
    }

    private static ClaimTicketResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
