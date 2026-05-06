using BackendApi.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.ListMyTickets;

public sealed record ListMyTicketsQuery(
    Guid CustomerId,
    int Skip,
    int Take,
    string? StateFilter);

public sealed record TicketListItem(
    Guid Id,
    string State,
    string Category,
    string Priority,
    string Subject,
    string MarketCode,
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset ResolutionDueUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ListMyTicketsResult(IReadOnlyList<TicketListItem> Items, int Total);

public sealed class ListMyTicketsHandler
{
    private readonly SupportDbContext _db;

    public ListMyTicketsHandler(SupportDbContext db) => _db = db;

    public async Task<ListMyTicketsResult> HandleAsync(ListMyTicketsQuery q, CancellationToken ct)
    {
        var query = _db.Tickets.AsNoTracking()
            .Where(t => t.CustomerId == q.CustomerId);

        if (!string.IsNullOrWhiteSpace(q.StateFilter))
        {
            query = query.Where(t => t.State == q.StateFilter);
        }

        var total = await query.CountAsync(ct);

        var take = Math.Clamp(q.Take, 1, 200);
        var skip = Math.Max(0, q.Skip);

        var rows = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(t => new TicketListItem(
                t.Id, t.State, t.Category, t.Priority, t.Subject, t.MarketCode,
                t.FirstResponseDueUtc, t.ResolutionDueUtc, t.CreatedAtUtc, t.UpdatedAtUtc))
            .ToListAsync(ct);

        return new ListMyTicketsResult(rows, total);
    }
}
