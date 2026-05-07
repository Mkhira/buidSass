using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.ReplyAsAgent;

public sealed record ReplyAsAgentCommand(
    Guid TicketId,
    Guid ActorId,
    bool ActorIsLeadOrSuperAdmin,
    string ActorRole,
    string Body,
    bool RequestInfoTransition);

public sealed record ReplyAsAgentResult(
    bool Success,
    Guid? MessageId,
    string? NewState,
    string? ReasonCode,
    string? Detail);

/// <summary>
/// FR-014a — non-assigned agents can post internal notes only; customer-visible
/// replies require assignment OR a lead/super_admin intervention.
/// </summary>
public sealed class ReplyAsAgentHandler
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ReplyAsAgentHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ReplyAsAgentResult> HandleAsync(ReplyAsAgentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Body))
        {
            return Failure(TicketReasonCode.MessageBodyRequired, "Body is required.");
        }
        if (cmd.Body.Length > 8000)
        {
            return Failure(TicketReasonCode.MessageBodyTooLong, "Body exceeds 8000 characters.");
        }

        // CodeRabbit Loop-1 (outside-diff): the prior preflight reject (using
        // the endpoint-resolved cmd.ActorIsAssigned) was a stale TOCTOU read
        // that could *falsely deny* a now-validly-assigned actor when a lead
        // had reassigned the ticket between endpoint and handler. The
        // post-load revalidation below uses the freshly-loaded ticket row and
        // is the single source of truth for FR-014a — the preflight is
        // redundant once that exists, so we remove it here to close the
        // false-negative TOCTOU window.
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal, "Ticket is closed.");
        }

        // C1 (TOCTOU close): re-validate FR-014a against the freshly-loaded
        // ticket row. This is now the single source of truth for assignment
        // (the endpoint pre-load + the `ActorIsAssigned` command field were
        // removed in CodeRabbit Loop-2). We allow the action when the actor
        // is currently the assigned agent OR explicitly lead/super_admin.
        var actorCurrentlyAssigned = ticket.AssignedAgentId == cmd.ActorId;
        if (!actorCurrentlyAssigned && !cmd.ActorIsLeadOrSuperAdmin)
        {
            return Failure(TicketReasonCode.ActionRequiresAssignment,
                "Customer-visible replies require ticket assignment or lead intervention.");
        }

        var fromState = TicketStateNames.FromWire(ticket.State);
        var nowUtc = _clock.GetUtcNow();
        var messageId = Guid.NewGuid();
        var leadIntervention = cmd.ActorIsLeadOrSuperAdmin && !actorCurrentlyAssigned;

        _db.Messages.Add(new TicketMessage
        {
            Id = messageId,
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.AgentReply,
            ActorId = cmd.ActorId,
            ActorRole = cmd.ActorRole,
            Body = cmd.Body,
            BodyLocale = ticket.Locale,
            LeadIntervention = leadIntervention,
            CreatedAtUtc = nowUtc,
        });

        var newState = ticket.State;
        var transitioned = false;
        if (cmd.RequestInfoTransition && fromState == TicketState.InProgress)
        {
            if (TicketStateMachine.TryTransition(
                    fromState, TicketState.WaitingCustomer,
                    TicketTriggerKind.AgentReply,
                    cmd.ActorIsLeadOrSuperAdmin ? TicketActorKind.Lead : TicketActorKind.Agent,
                    out _))
            {
                ticket.State = TicketStateNames.ToWire(TicketState.WaitingCustomer);
                ticket.UpdatedAtUtc = nowUtc;
                newState = ticket.State;
                transitioned = true;
            }
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict, "Ticket modified concurrently.");
        }

        await _publisher.Publish(new TicketAgentReplySent(
            TicketId: ticket.Id,
            MessageId: messageId,
            LeadIntervention: leadIntervention,
            OccurredAtUtc: nowUtc), ct);

        if (transitioned)
        {
            await _publisher.Publish(new TicketStateChanged(
                TicketId: ticket.Id,
                FromState: TicketStateNames.ToWire(fromState),
                ToState: newState,
                TriggeredBy: TicketTriggerKind.AgentReply,
                OccurredAtUtc: nowUtc), ct);
        }

        return new ReplyAsAgentResult(true, messageId, newState, null, null);
    }

    private static ReplyAsAgentResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
