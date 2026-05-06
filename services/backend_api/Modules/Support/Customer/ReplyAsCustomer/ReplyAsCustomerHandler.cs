using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.ReplyAsCustomer;

/// <summary>
/// Customer-side reply handler per US1 reply loop. Transitions
/// <c>open / waiting_customer → in_progress</c>.
///
/// Reopen-via-reply (FR-013) is intentionally NOT inlined here — that's
/// handled by ReopenTicket (US6). Reply on a resolved ticket within the
/// reopen window is a separate validation surface.
/// </summary>
public sealed class ReplyAsCustomerHandler
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ReplyAsCustomerHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ReplyAsCustomerResult> HandleAsync(ReplyAsCustomerCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Body))
        {
            return Failure(TicketReasonCode.MessageBodyRequired, "Body is required.");
        }
        if (cmd.Body.Length > 8000)
        {
            return Failure(TicketReasonCode.MessageBodyTooLong, "Body exceeds 8000 characters.");
        }

        var ticket = await _db.Tickets
            .FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);

        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (ticket.CustomerId != cmd.CustomerId)
        {
            return Failure(TicketReasonCode.CustomerForbidden, "Ticket is not owned by this customer.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal, "Ticket is closed; replies are not accepted.");
        }
        if (ticket.State == TicketStateNames.Resolved)
        {
            // Reopen flow is handled separately by US6's ReopenTicket endpoint.
            return Failure(TicketReasonCode.ReopenWindowClosed,
                "Ticket is resolved; use the reopen endpoint to continue the conversation.");
        }

        var fromState = TicketStateNames.FromWire(ticket.State);
        var toState = TicketState.InProgress;

        if (!TicketStateMachine.TryTransition(
                fromState, toState,
                TicketTriggerKind.CustomerReply,
                TicketActorKind.Customer,
                out var reason))
        {
            return Failure(reason ?? TicketReasonCode.InvalidTransition,
                $"Customer reply not allowed in state '{ticket.State}'.");
        }

        var nowUtc = _clock.GetUtcNow();
        var messageId = Guid.NewGuid();

        _db.Messages.Add(new TicketMessage
        {
            Id = messageId,
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.CustomerReply,
            ActorId = cmd.CustomerId,
            ActorRole = TicketActorKindNames.Customer,
            Body = cmd.Body,
            BodyLocale = ticket.Locale,
            LeadIntervention = false,
            CreatedAtUtc = nowUtc,
        });

        var newState = TicketStateNames.ToWire(toState);
        if (ticket.State != newState)
        {
            ticket.State = newState;
            ticket.UpdatedAtUtc = nowUtc;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket was modified concurrently; retry the request.");
        }

        await _publisher.Publish(new TicketCustomerReplyReceived(
            TicketId: ticket.Id,
            MessageId: messageId,
            OccurredAtUtc: nowUtc), ct);

        if (fromState != toState)
        {
            await _publisher.Publish(new TicketStateChanged(
                TicketId: ticket.Id,
                FromState: TicketStateNames.ToWire(fromState),
                ToState: newState,
                TriggeredBy: TicketTriggerKind.CustomerReply,
                OccurredAtUtc: nowUtc), ct);
        }

        return new ReplyAsCustomerResult(true, messageId, newState, null, null);
    }

    private static ReplyAsCustomerResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
