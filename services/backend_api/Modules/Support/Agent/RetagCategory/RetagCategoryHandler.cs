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

        // CodeRabbit Loop-1: persist the canonical wire form rather than the
        // raw request string. This keeps idempotency robust even if `FromWire`
        // ever accepts aliases/casing variants.
        var canonicalNewCategory = TicketCategoryNames.ToWire(parsedCategory);

        // CodeRabbit Loop-1: scope the ticket mutation by market_code per
        // ADR-010. Super-admin retains the cross-market write path for
        // operational repair; agents/leads cannot touch foreign-market rows.
        var ticket = cmd.IsSuperAdmin
            ? await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            : await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId
                && t.MarketCode == cmd.MarketCode, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal,
                "Cannot retag a closed ticket.");
        }

        if (string.Equals(ticket.Category, canonicalNewCategory, StringComparison.Ordinal))
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
                $"Category '{canonicalNewCategory}' is not consistent with linked-entity-kind "
                + $"'{ticket.LinkedEntityKind ?? "(none)"}' per FR-007.");
        }

        var nowUtc = _clock.GetUtcNow();
        var priorCategory = ticket.Category;
        ticket.Category = canonicalNewCategory;
        ticket.UpdatedAtUtc = nowUtc;

        var auditBody = string.IsNullOrWhiteSpace(cmd.Justification)
            ? $"Category retagged: {priorCategory} → {canonicalNewCategory}"
            : $"Category retagged: {priorCategory} → {canonicalNewCategory}; justification: {cmd.Justification.Trim()}";

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
            NewCategory: canonicalNewCategory,
            ReasonCode: null,
            Detail: null);
    }

    private static RetagCategoryResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
