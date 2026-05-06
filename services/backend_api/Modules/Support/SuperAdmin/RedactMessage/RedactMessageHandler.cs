using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.SuperAdmin.RedactMessage;

/// <summary>
/// FR-011a — super-admin redacts a message body. Per data-model §4 the row
/// is mutated through the controlled redaction-exception path (body→null,
/// stamps redacted_at_utc, redacted_by_role, originating_redaction_request_ticket_id).
///
/// <para>The originating customer's redaction-request ticket (if supplied)
/// is auto-resolved with a synthetic system-event message. Repeat redaction
/// of the same message returns <see cref="TicketReasonCode.RedactionRequestAlreadyRedacted"/>.</para>
///
/// <para>Per FR-011a, original body forwarding to spec 028 encrypted audit
/// storage is a stub at V1 — that integration lands when spec 028 ships.</para>
/// </summary>
public sealed class RedactMessageHandler
{
    public const int MinimumReasonLength = 10;

    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public RedactMessageHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<RedactMessageResult> HandleAsync(RedactMessageCommand cmd, CancellationToken ct)
    {
        if (cmd.ReasonNote is null
            || cmd.ReasonNote.Trim().Length < MinimumReasonLength)
        {
            return Failure(TicketReasonCode.RedactionReasonRequired,
                $"Redaction reason note must be at least {MinimumReasonLength} characters.");
        }

        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == cmd.MessageId && m.TicketId == cmd.TicketId, ct);
        if (message is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Message not found.");
        }

        if (message.RedactedAtUtc is not null)
        {
            return Failure(TicketReasonCode.RedactionRequestAlreadyRedacted,
                "Message is already redacted.");
        }

        // Per data-model §4 we only redact customer/agent reply bodies; system
        // events do not have a body to wipe.
        if (message.Kind == TicketMessageKindNames.SystemEvent)
        {
            return Failure(TicketReasonCode.RedactionMessageNotRedactable,
                "System-event messages have no body to redact.");
        }

        // FR-011a guard: the message must belong to the redaction-request's
        // originating ticket if one is supplied. This stops a customer's
        // redaction-request ticket from cascading into an unrelated thread.
        if (cmd.OriginatingRedactionRequestTicketId is not null)
        {
            var requestTicket = await _db.Tickets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == cmd.OriginatingRedactionRequestTicketId.Value, ct);
            if (requestTicket is null)
            {
                return Failure(TicketReasonCode.RedactionRequestMessageNotInOriginatingTicket,
                    "Originating redaction-request ticket not found.");
            }

            if (cmd.RequestingCustomerId is not null
                && requestTicket.CustomerId != cmd.RequestingCustomerId.Value)
            {
                return Failure(TicketReasonCode.RedactionRequestMessageNotInOriginatingTicket,
                    "Originating redaction-request ticket does not belong to the requesting customer.");
            }
        }

        var nowUtc = _clock.GetUtcNow();
        message.Body = null;
        message.RedactedAtUtc = nowUtc;
        message.RedactedByRole = TicketActorKindNames.SuperAdmin;
        message.OriginatingRedactionRequestTicketId = cmd.OriginatingRedactionRequestTicketId;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Message was modified concurrently; retry the request.");
        }

        await _publisher.Publish(new TicketMessageRedacted(
            TicketId: cmd.TicketId,
            MessageId: cmd.MessageId,
            RequestingCustomerId: cmd.RequestingCustomerId ?? Guid.Empty,
            OccurredAtUtc: nowUtc), ct);

        return new RedactMessageResult(true, nowUtc, null, null);
    }

    private static RedactMessageResult Failure(string reasonCode, string detail) =>
        new(false, null, reasonCode, detail);
}
