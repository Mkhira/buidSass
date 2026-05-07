using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Lead.OverrideSlaTargets;

/// <summary>
/// SLA-target override handler. Per FR-026:
/// <list type="bullet">
///   <item>Justification note ≥ 10 chars; resolution must &gt; first_response.</item>
///   <item>Due timestamps recomputed from <c>now() + new_target_minutes</c>.</item>
///   <item>If the new deadline moves beyond <c>now()</c>, the corresponding
///         <c>breach_acknowledged_at_*</c> stamp is cleared so a fresh breach
///         can be detected.</item>
/// </list>
/// </summary>
public sealed class OverrideSlaTargetsHandler
{
    public const int MinimumJustificationLength = 10;

    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public OverrideSlaTargetsHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<OverrideSlaTargetsResult> HandleAsync(
        OverrideSlaTargetsCommand cmd, CancellationToken ct)
    {
        if (cmd.JustificationNote is null
            || cmd.JustificationNote.Trim().Length < MinimumJustificationLength)
        {
            return Failure(TicketReasonCode.SlaOverrideJustificationRequired,
                $"Justification note must be at least {MinimumJustificationLength} characters.");
        }

        if (cmd.NewFirstResponseTargetMinutes < 1 || cmd.NewResolutionTargetMinutes < 1)
        {
            return Failure(TicketReasonCode.SlaOverrideJustificationRequired,
                "Both target minutes must be at least 1.");
        }

        if (cmd.NewResolutionTargetMinutes <= cmd.NewFirstResponseTargetMinutes)
        {
            return Failure(TicketReasonCode.SlaOverrideResolutionMustExceedFirstResponse,
                "resolution_target_minutes must exceed first_response_target_minutes.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }

        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal,
                "Cannot override SLA targets on a closed ticket.");
        }

        var nowUtc = _clock.GetUtcNow();
        var priorFirstResponseTarget = ticket.FirstResponseTargetMinutesSnapshot;
        var priorResolutionTarget = ticket.ResolutionTargetMinutesSnapshot;

        // CodeRabbit Loop-1: capture prior due timestamps BEFORE overwriting.
        // We must compare new-due against prior-due (not against now()) so a
        // tightening override doesn't spuriously clear acknowledgments — the
        // new due is always > now() by construction, which made the prior
        // check vacuously true for every override.
        var priorFirstResponseDueUtc = ticket.FirstResponseDueUtc;
        var priorResolutionDueUtc = ticket.ResolutionDueUtc;

        ticket.FirstResponseTargetMinutesSnapshot = cmd.NewFirstResponseTargetMinutes;
        ticket.ResolutionTargetMinutesSnapshot = cmd.NewResolutionTargetMinutes;
        ticket.FirstResponseDueUtc = nowUtc.AddMinutes(cmd.NewFirstResponseTargetMinutes);
        ticket.ResolutionDueUtc = nowUtc.AddMinutes(cmd.NewResolutionTargetMinutes);

        // Per FR-026: clear the acknowledgment ONLY when the override actually
        // pushes the deadline beyond where it was. A shorter SLA must keep
        // the existing breach acknowledgment intact — otherwise we'd lose the
        // record that the breach already happened.
        if (ticket.FirstResponseDueUtc > priorFirstResponseDueUtc)
        {
            ticket.BreachAcknowledgedAtFirstResponse = null;
        }
        if (ticket.ResolutionDueUtc > priorResolutionDueUtc)
        {
            ticket.BreachAcknowledgedAtResolution = null;
        }

        ticket.UpdatedAtUtc = nowUtc;

        // I4 fix: append a system_event message to the thread mirroring the
        // pattern used by Reopen / ForceClose / Reassign. Without this the
        // override is invisible in the per-ticket thread view; the domain
        // event alone reaches downstream notifications/audit but leaves
        // operators staring at an unchanged thread.
        //
        // CodeRabbit Loop-1: keep the customer-visible thread entry generic.
        // Customer reads include `system_event` rows, so leaking the lead's
        // internal justification text here would expose internal operational
        // notes. Detailed justification still travels through the
        // TicketSlaOverridden domain event below (consumed by audit + 025
        // internal channels), which is the correct destination.
        _db.Messages.Add(new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = cmd.LeadActorId,
            ActorRole = TicketActorKindNames.Lead,
            Body = $"SLA targets overridden — first_response={cmd.NewFirstResponseTargetMinutes}m, "
                + $"resolution={cmd.NewResolutionTargetMinutes}m.",
            BodyLocale = ticket.Locale,
            LeadIntervention = false,
            CreatedAtUtc = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket was modified concurrently; retry the request.");
        }

        // CodeRabbit Loop-1: emit a dedicated TicketSlaOverridden notification
        // rather than a no-op TicketStateChanged with same FromState/ToState.
        // This surfaces actor + justification + old/new targets to spec 025
        // (notifications) + the audit-log consumer without misleading
        // state-machine subscribers into thinking a real transition happened.
        await _publisher.Publish(new TicketSlaOverridden(
            TicketId: ticket.Id,
            LeadActorId: cmd.LeadActorId,
            PriorFirstResponseTargetMinutes: priorFirstResponseTarget,
            PriorResolutionTargetMinutes: priorResolutionTarget,
            NewFirstResponseTargetMinutes: cmd.NewFirstResponseTargetMinutes,
            NewResolutionTargetMinutes: cmd.NewResolutionTargetMinutes,
            NewFirstResponseDueUtc: ticket.FirstResponseDueUtc,
            NewResolutionDueUtc: ticket.ResolutionDueUtc,
            JustificationNote: cmd.JustificationNote.Trim(),
            OccurredAtUtc: nowUtc), ct);

        return new OverrideSlaTargetsResult(
            Success: true,
            PriorFirstResponseTargetMinutes: priorFirstResponseTarget,
            PriorResolutionTargetMinutes: priorResolutionTarget,
            NewFirstResponseTargetMinutes: cmd.NewFirstResponseTargetMinutes,
            NewResolutionTargetMinutes: cmd.NewResolutionTargetMinutes,
            NewFirstResponseDueUtc: ticket.FirstResponseDueUtc,
            NewResolutionDueUtc: ticket.ResolutionDueUtc,
            ReasonCode: null,
            Detail: null);
    }

    private static OverrideSlaTargetsResult Failure(string reasonCode, string detail) =>
        new(false, null, null, null, null, null, null, reasonCode, detail);
}
