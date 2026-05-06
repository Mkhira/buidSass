using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.ReopenTicket;

public sealed record ReopenTicketCommand(Guid TicketId, Guid CustomerId, string? Body);

public sealed record ReopenTicketResult(
    bool Success,
    string? NewState,
    int? ReopenCount,
    DateTimeOffset? FirstResponseDueUtc,
    DateTimeOffset? ResolutionDueUtc,
    string? ReasonCode,
    string? Detail);

/// <summary>
/// FR-029 + research §R-09 — customer-initiated reopen within per-market window
/// + cap. Recomputes both SLA deadlines, clears breach acknowledgments,
/// increments reopen_count.
/// </summary>
public sealed class ReopenTicketHandler
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ReopenTicketHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ReopenTicketResult> HandleAsync(ReopenTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        if (ticket.CustomerId != cmd.CustomerId)
        {
            return Failure(TicketReasonCode.CustomerForbidden, "Ticket not owned by this customer.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal, "Ticket is closed.");
        }
        if (ticket.State != TicketStateNames.Resolved)
        {
            return Failure(TicketReasonCode.InvalidTransition,
                "Reopen is only valid from state 'resolved'.");
        }

        var schema = await _db.MarketSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MarketCode == ticket.MarketCode, ct);

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

        if (!policy.ReopenEnabled)
        {
            return Failure(TicketReasonCode.ReopenDisabledForMarket,
                "Reopen is disabled for this market.");
        }
        if (ticket.ReopenCount >= policy.MaxReopenCount)
        {
            return Failure(TicketReasonCode.ReopenCountExceeded,
                "Reopen count exceeded for this ticket.");
        }
        if (ticket.ResolvedAtUtc is null)
        {
            return Failure(TicketReasonCode.ReopenWindowClosed,
                "Resolved timestamp missing; cannot validate reopen window.");
        }

        var nowUtc = _clock.GetUtcNow();
        var windowEnd = ticket.ResolvedAtUtc.Value.AddDays(policy.ReopenWindowDays);
        if (nowUtc > windowEnd)
        {
            return Failure(TicketReasonCode.ReopenWindowClosed,
                "Reopen window has elapsed.");
        }

        var fromState = TicketState.Resolved;
        var toState = TicketState.InProgress;
        if (!TicketStateMachine.TryTransition(
                fromState, toState,
                TicketTriggerKind.CustomerReopen,
                TicketActorKind.Customer,
                out var reason))
        {
            return Failure(reason ?? TicketReasonCode.InvalidTransition, "Reopen transition rejected.");
        }

        // Recompute SLA deadlines from snapshot (FR-022 frozen targets).
        ticket.State = TicketStateNames.ToWire(toState);
        ticket.ReopenCount += 1;
        ticket.ReopenedAtUtc = nowUtc;
        ticket.ResolvedAtUtc = null;
        ticket.FirstResponseDueUtc = nowUtc.AddMinutes(ticket.FirstResponseTargetMinutesSnapshot);
        ticket.ResolutionDueUtc = nowUtc.AddMinutes(ticket.ResolutionTargetMinutesSnapshot);
        ticket.BreachAcknowledgedAtFirstResponse = null;
        ticket.BreachAcknowledgedAtResolution = null;
        ticket.UpdatedAtUtc = nowUtc;

        if (!string.IsNullOrWhiteSpace(cmd.Body))
        {
            _db.Messages.Add(new Entities.TicketMessage
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Kind = TicketMessageKindNames.CustomerReply,
                ActorId = cmd.CustomerId,
                ActorRole = TicketActorKindNames.Customer,
                Body = cmd.Body,
                BodyLocale = ticket.Locale,
                LeadIntervention = false,
                CreatedAtUtc = nowUtc,
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict, "Ticket modified concurrently.");
        }

        await _publisher.Publish(new TicketReopened(
            TicketId: ticket.Id,
            ReopenCount: ticket.ReopenCount,
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticket.Id,
            FromState: TicketStateNames.ToWire(fromState),
            ToState: ticket.State,
            TriggeredBy: TicketTriggerKind.CustomerReopen,
            OccurredAtUtc: nowUtc), ct);

        return new ReopenTicketResult(
            Success: true,
            NewState: ticket.State,
            ReopenCount: ticket.ReopenCount,
            FirstResponseDueUtc: ticket.FirstResponseDueUtc,
            ResolutionDueUtc: ticket.ResolutionDueUtc,
            ReasonCode: null,
            Detail: null);
    }

    private static ReopenTicketResult Failure(string code, string detail) =>
        new(false, null, null, null, null, code, detail);
}
