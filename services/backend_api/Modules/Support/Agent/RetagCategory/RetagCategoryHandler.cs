using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.RetagCategory;

/// <summary>
/// T124 handler. Validates new-category enum membership + FR-007
/// consistency against the ticket's existing <c>linked_entity_kind</c>.
/// Persists a <c>system_event</c> message capturing the prior + new
/// category and (optional) justification so the change is visible in the
/// thread audit trail.
/// </summary>
public sealed class RetagCategoryHandler
{
    private readonly SupportDbContext _db;
    private readonly TimeProvider _clock;

    public RetagCategoryHandler(SupportDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<RetagCategoryResult> HandleAsync(
        RetagCategoryCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.NewCategory))
        {
            return Failure(TicketReasonCode.InvalidTransition, "New category is required.");
        }

        TicketCategory parsedCategory;
        try
        {
            parsedCategory = TicketCategoryNames.FromWire(cmd.NewCategory);
        }
        catch (InvalidOperationException)
        {
            return Failure(TicketReasonCode.InvalidTransition,
                $"Unknown ticket category '{cmd.NewCategory}'.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal,
                "Cannot retag a closed ticket.");
        }

        if (string.Equals(ticket.Category, cmd.NewCategory, StringComparison.Ordinal))
        {
            // Idempotent no-op.
            return new RetagCategoryResult(
                Success: true,
                PriorCategory: ticket.Category,
                NewCategory: ticket.Category,
                ReasonCode: null,
                Detail: "Already tagged with the requested category.");
        }

        // FR-007: category MUST remain consistent with the existing linked-entity kind.
        if (!TicketCategoryNames.IsConsistentWithLinkedKind(parsedCategory, ticket.LinkedEntityKind))
        {
            return Failure(TicketReasonCode.LinkedEntityKindInconsistent,
                $"Category '{cmd.NewCategory}' is not consistent with linked-entity-kind "
                + $"'{ticket.LinkedEntityKind ?? "(none)"}' per FR-007.");
        }

        var nowUtc = _clock.GetUtcNow();
        var priorCategory = ticket.Category;
        ticket.Category = cmd.NewCategory;
        ticket.UpdatedAtUtc = nowUtc;

        var auditBody = string.IsNullOrWhiteSpace(cmd.Justification)
            ? $"Category retagged: {priorCategory} → {cmd.NewCategory}"
            : $"Category retagged: {priorCategory} → {cmd.NewCategory}; justification: {cmd.Justification.Trim()}";

        _db.Messages.Add(new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = cmd.ActorId,
            ActorRole = cmd.ActorRole,
            Body = auditBody,
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
            return Failure(TicketReasonCode.VersionConflict, "Ticket modified concurrently.");
        }

        return new RetagCategoryResult(
            Success: true,
            PriorCategory: priorCategory,
            NewCategory: cmd.NewCategory,
            ReasonCode: null,
            Detail: null);
    }

    private static RetagCategoryResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
