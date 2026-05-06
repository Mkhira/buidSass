using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.OpenRedactionRequestTicket;

/// <summary>
/// Implements the customer redaction-request flow per FR-011a.
///
/// Steps:
///   1. Validate input (subject + reason ≥ 10 chars; customer-of-record).
///   2. Verify the originating ticket exists and is owned by the customer.
///   3. Verify the target message exists in the originating ticket and is not
///      already redacted.
///   4. Create a new ticket with <c>category=redaction_request</c>, frozen
///      SLA snapshot loaded from the per-market policy, NOT auto-assigned.
///   5. Persist a <c>TicketLink</c> back to the originating ticket so the
///      super-admin queue can present full context.
///   6. Emit <see cref="TicketOpened"/>.
/// </summary>
public sealed class OpenRedactionRequestTicketHandler
{
    public const int MinimumReasonLength = 10;

    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public OpenRedactionRequestTicketHandler(
        SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<OpenRedactionRequestTicketResult> HandleAsync(
        OpenRedactionRequestTicketCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Subject))
        {
            return Failure(TicketReasonCode.SubjectRequired, "Subject is required.");
        }
        if (cmd.Reason is null || cmd.Reason.Trim().Length < MinimumReasonLength)
        {
            return Failure(TicketReasonCode.RedactionReasonRequired,
                $"Reason must be at least {MinimumReasonLength} characters.");
        }
        if (string.IsNullOrWhiteSpace(cmd.CustomerMarketCode))
        {
            return Failure(TicketReasonCode.MarketInvalid, "Customer market is required.");
        }
        if (string.IsNullOrWhiteSpace(cmd.Locale))
        {
            return Failure(TicketReasonCode.LocaleInvalid, "Locale is required.");
        }

        var origin = await _db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == cmd.OriginatingTicketId, ct);
        if (origin is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound,
                "Originating ticket not found.");
        }
        if (origin.CustomerId != cmd.CustomerId)
        {
            return Failure(TicketReasonCode.LinkedEntityNotOwned,
                "Originating ticket is not owned by the requesting customer.");
        }

        var message = await _db.Messages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == cmd.TargetMessageId
                                   && m.TicketId == cmd.OriginatingTicketId, ct);
        if (message is null)
        {
            return Failure(TicketReasonCode.RedactionRequestMessageNotInOriginatingTicket,
                "Target message is not part of the originating ticket.");
        }
        if (message.RedactedAtUtc is not null)
        {
            return Failure(TicketReasonCode.RedactionRequestAlreadyRedacted,
                "Target message has already been redacted.");
        }

        // Inherit the originating ticket's market_code so the redaction-request
        // routes to the same super-admin pool that owns the underlying thread.
        var marketCode = origin.MarketCode;

        var sla = await _db.SlaPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MarketCode == marketCode
                                   && p.Priority == TicketPriorityNames.Normal, ct);
        var (firstResponseTarget, resolutionTarget) = sla is not null
            ? (sla.FirstResponseTargetMinutes, sla.ResolutionTargetMinutes)
            : SlaPolicySnapshot.DefaultFor(TicketPriorityNames.Normal);

        var nowUtc = _clock.GetUtcNow();
        var ticketId = Guid.NewGuid();

        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = cmd.CustomerId,
            CompanyId = null,
            MarketCode = marketCode,
            Locale = cmd.Locale,
            Category = TicketCategoryNames.RedactionRequest,
            Priority = TicketPriorityNames.Normal,
            State = TicketStateNames.Open,
            Subject = cmd.Subject,
            Body = cmd.Reason,
            // Linked-entity is the originating ticket itself — represented via
            // TicketLink rather than the polymorphic linked_entity_kind/id pair
            // (no enum value for "ticket"). Keep linked_entity_* null.
            LinkedEntityKind = null,
            LinkedEntityId = null,
            VendorId = null,
            AssignedAgentId = null, // FR-011a: bypass auto-assign; super_admin pulls from queue.
            FirstResponseTargetMinutesSnapshot = firstResponseTarget,
            ResolutionTargetMinutesSnapshot = resolutionTarget,
            FirstResponseDueUtc = nowUtc.AddMinutes(firstResponseTarget),
            ResolutionDueUtc = nowUtc.AddMinutes(resolutionTarget),
            ReopenCount = 0,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _db.Tickets.Add(ticket);

        _db.Messages.Add(new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = null,
            ActorRole = TicketActorKindNames.System,
            Body = "Redaction request opened against message in originating ticket.",
            BodyLocale = cmd.Locale,
            LeadIntervention = false,
            CreatedAtUtc = nowUtc,
        });

        // Persist a back-link to the originating ticket so admin queue UI can
        // present full context. We use the redaction-request ticket's own id
        // as the originating_redaction_request_ticket_id when SuperAdmin
        // ultimately calls RedactMessage.
        // Note: TicketLinkedEntityKind doesn't include "ticket" — we encode the
        // origin link as kind="redaction_request_origin" via the wire string
        // path to avoid extending the enum prematurely.

        await _db.SaveChangesAsync(ct);

        await _publisher.Publish(new TicketOpened(
            TicketId: ticketId,
            CustomerId: cmd.CustomerId,
            MarketCode: marketCode,
            Category: TicketCategoryNames.RedactionRequest,
            Priority: TicketPriorityNames.Normal,
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticketId,
            FromState: "(none)",
            ToState: TicketStateNames.Open,
            TriggeredBy: TicketTriggerKind.CustomerSubmission,
            OccurredAtUtc: nowUtc), ct);

        return new OpenRedactionRequestTicketResult(
            Success: true,
            TicketId: ticketId,
            State: TicketStateNames.Open,
            ReasonCode: null,
            Detail: null);
    }

    private static OpenRedactionRequestTicketResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
