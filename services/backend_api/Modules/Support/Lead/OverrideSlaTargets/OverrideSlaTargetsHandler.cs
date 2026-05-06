using BackendApi.Modules.Shared;
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
        var priorFirstResponse = ticket.FirstResponseTargetMinutesSnapshot;
        var priorResolution = ticket.ResolutionTargetMinutesSnapshot;

        ticket.FirstResponseTargetMinutesSnapshot = cmd.NewFirstResponseTargetMinutes;
        ticket.ResolutionTargetMinutesSnapshot = cmd.NewResolutionTargetMinutes;
        ticket.FirstResponseDueUtc = nowUtc.AddMinutes(cmd.NewFirstResponseTargetMinutes);
        ticket.ResolutionDueUtc = nowUtc.AddMinutes(cmd.NewResolutionTargetMinutes);

        // Per FR-026: clear acknowledgment if the override moves the deadline
        // forward. Acknowledgments at the older deadline no longer reflect the
        // operational reality, so allow the worker to re-detect.
        if (ticket.FirstResponseDueUtc > nowUtc)
        {
            ticket.BreachAcknowledgedAtFirstResponse = null;
        }
        if (ticket.ResolutionDueUtc > nowUtc)
        {
            ticket.BreachAcknowledgedAtResolution = null;
        }

        ticket.UpdatedAtUtc = nowUtc;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Ticket was modified concurrently; retry the request.");
        }

        // No dedicated event for SLA override per data-model §6 — the
        // state-changed catch-all is not appropriate (state did not change).
        // Audit row is captured via spec 003 audit log (FR-031, deferred wiring).
        // Publish a state-changed-style notification so downstream notifications
        // (spec 025) may surface the change to the assigned agent.
        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticket.Id,
            FromState: ticket.State,
            ToState: ticket.State,
            TriggeredBy: "lead_sla_override",
            OccurredAtUtc: nowUtc), ct);

        return new OverrideSlaTargetsResult(
            Success: true,
            PriorFirstResponseTargetMinutes: priorFirstResponse,
            PriorResolutionTargetMinutes: priorResolution,
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
