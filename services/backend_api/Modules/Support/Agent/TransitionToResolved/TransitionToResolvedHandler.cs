using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.TransitionToResolved;

public sealed record TransitionToResolvedCommand(
    Guid TicketId,
    Guid ActorId,
    bool ActorIsAssigned,
    bool ActorIsLeadOrSuperAdmin);

public sealed record TransitionToResolvedResult(
    bool Success,
    string? ReasonCode,
    string? Detail);

/// <summary>
/// Transition <c>in_progress | waiting_customer → resolved</c>. Assigned-only
/// (or lead/super-admin override). FR-003 requires a customer-visible agent
/// reply prior to resolving — enforced by checking the message thread for
/// at least one <c>agent_reply</c>.
/// </summary>
public sealed class TransitionToResolvedHandler
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public TransitionToResolvedHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<TransitionToResolvedResult> HandleAsync(
        TransitionToResolvedCommand cmd, CancellationToken ct)
    {
        if (!cmd.ActorIsAssigned && !cmd.ActorIsLeadOrSuperAdmin)
        {
            return Failure(TicketReasonCode.ActionRequiresAssignment,
                "Resolve requires ticket assignment or lead intervention.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        if (ticket.State == TicketStateNames.Closed) return Failure(TicketReasonCode.ClosedTerminal, "Ticket closed.");

        var fromState = TicketStateNames.FromWire(ticket.State);
        if (!TicketStateMachine.TryTransition(
                fromState, TicketState.Resolved,
                TicketTriggerKind.AgentResolve,
                cmd.ActorIsLeadOrSuperAdmin ? TicketActorKind.Lead : TicketActorKind.Agent,
                out var reason))
        {
            return Failure(reason ?? TicketReasonCode.InvalidTransition,
                $"Resolve not allowed from state '{ticket.State}'.");
        }

        // FR-003 — at least one agent_reply must precede resolution.
        var hasAgentReply = await _db.Messages.AsNoTracking()
            .AnyAsync(m => m.TicketId == ticket.Id
                && m.Kind == TicketMessageKindNames.AgentReply, ct);
        if (!hasAgentReply)
        {
            return Failure(TicketReasonCode.ResolvedRequiresAgentReply,
                "Cannot resolve without a prior customer-visible agent reply.");
        }

        var nowUtc = _clock.GetUtcNow();
        ticket.State = TicketStateNames.ToWire(TicketState.Resolved);
        ticket.ResolvedAtUtc = nowUtc;
        ticket.UpdatedAtUtc = nowUtc;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict, "Ticket modified concurrently.");
        }

        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticket.Id,
            FromState: TicketStateNames.ToWire(fromState),
            ToState: ticket.State,
            TriggeredBy: TicketTriggerKind.AgentResolve,
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketResolved(
            TicketId: ticket.Id,
            ResolvedByAgentId: ticket.AssignedAgentId,
            ResolvedAtUtc: nowUtc), ct);

        return new TransitionToResolvedResult(true, null, null);
    }

    private static TransitionToResolvedResult Failure(string code, string detail) =>
        new(false, code, detail);
}
