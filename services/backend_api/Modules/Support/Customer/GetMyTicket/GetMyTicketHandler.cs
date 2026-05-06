using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.GetMyTicket;

public sealed record GetMyTicketQuery(Guid TicketId, Guid CustomerId);

/// <summary>
/// Customer-facing single-ticket read. Per FR-014 internal_note rows are
/// stripped server-side; redacted messages return a tombstone payload (null
/// body + redacted_at_utc populated).
/// </summary>
public sealed class GetMyTicketHandler
{
    private readonly SupportDbContext _db;

    public GetMyTicketHandler(SupportDbContext db) => _db = db;

    public async Task<GetMyTicketResult> HandleAsync(GetMyTicketQuery q, CancellationToken ct)
    {
        var ticket = await _db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == q.TicketId, ct);

        if (ticket is null)
        {
            return GetMyTicketResult.NotFound();
        }
        if (ticket.CustomerId != q.CustomerId)
        {
            return GetMyTicketResult.Forbidden();
        }

        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.TicketId == ticket.Id
                && m.Kind != TicketMessageKindNames.InternalNote)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new TicketMessageDto(
                m.Id,
                m.Kind,
                m.ActorRole,
                m.RedactedAtUtc == null ? m.Body : null,
                m.BodyLocale,
                m.RedactedAtUtc,
                m.CreatedAtUtc))
            .ToListAsync(ct);

        return GetMyTicketResult.Ok(new TicketDetailDto(
            ticket.Id,
            ticket.State,
            ticket.Category,
            ticket.Priority,
            ticket.Subject,
            ticket.Body,
            ticket.Locale,
            ticket.MarketCode,
            ticket.LinkedEntityKind,
            ticket.LinkedEntityId,
            ticket.FirstResponseDueUtc,
            ticket.ResolutionDueUtc,
            ticket.CreatedAtUtc,
            ticket.UpdatedAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.ReopenCount,
            messages));
    }
}

public sealed record TicketMessageDto(
    Guid Id,
    string Kind,
    string ActorRole,
    string? Body,
    string? BodyLocale,
    DateTimeOffset? RedactedAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record TicketDetailDto(
    Guid Id,
    string State,
    string Category,
    string Priority,
    string Subject,
    string Body,
    string Locale,
    string MarketCode,
    string? LinkedEntityKind,
    Guid? LinkedEntityId,
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset ResolutionDueUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    int ReopenCount,
    IReadOnlyList<TicketMessageDto> Messages);

public sealed record GetMyTicketResult(bool IsOk, bool IsNotFound, bool IsForbidden, TicketDetailDto? Ticket)
{
    public static GetMyTicketResult Ok(TicketDetailDto ticket) => new(true, false, false, ticket);
    public static GetMyTicketResult NotFound() => new(false, true, false, null);
    public static GetMyTicketResult Forbidden() => new(false, false, true, null);
}
