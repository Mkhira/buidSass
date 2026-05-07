using BackendApi.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.ListTicketsByCustomer;

/// <summary>
/// T125 — admin-side per-customer ticket listing. The endpoint applies the
/// support.agent / support.lead / super_admin permission gate before
/// invoking; the handler itself trusts the caller is permission-cleared.
/// </summary>
public sealed class ListTicketsByCustomerHandler
{
    private readonly SupportDbContext _db;

    public ListTicketsByCustomerHandler(SupportDbContext db) => _db = db;

    public async Task<ListTicketsByCustomerResult> HandleAsync(
        ListTicketsByCustomerQuery q, CancellationToken ct)
    {
        // CodeRabbit Loop-1: enforce ADR-010 partition. Agents/leads see only
        // their market's tickets; super-admin gets the cross-market view to
        // support investigations spanning markets. The same-market filter is
        // applied as the very first predicate so the index on
        // (market_code, customer_id) is utilised.
        var query = _db.Tickets.AsNoTracking()
            .Where(t => t.CustomerId == q.CustomerId);
        if (!q.IsSuperAdmin)
        {
            query = query.Where(t => t.MarketCode == q.MarketCode);
        }

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
            .Select(t => new AdminCustomerTicketListItem(
                t.Id, t.State, t.Category, t.Priority, t.Subject, t.MarketCode,
                t.AssignedAgentId, t.FirstResponseDueUtc, t.ResolutionDueUtc,
                t.CreatedAtUtc, t.UpdatedAtUtc))
            .ToListAsync(ct);

        return new ListTicketsByCustomerResult(rows, total);
    }
}
