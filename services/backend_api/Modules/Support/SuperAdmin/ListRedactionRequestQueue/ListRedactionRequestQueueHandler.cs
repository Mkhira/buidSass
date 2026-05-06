using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.SuperAdmin.ListRedactionRequestQueue;

/// <summary>
/// Spec 023 Phase 10 T130 — super-admin-only redaction-request queue.
/// Filters tickets to <c>category=redaction_request</c> and orders by
/// SLA-pressure (oldest first-response-due first; FR-016 priority order).
/// </summary>
public sealed class ListRedactionRequestQueueHandler
{
    private readonly SupportDbContext _db;

    public ListRedactionRequestQueueHandler(SupportDbContext db) => _db = db;

    public async Task<ListRedactionRequestQueueResult> HandleAsync(
        ListRedactionRequestQueueQuery q, CancellationToken ct)
    {
        var page = q.Page < 1 ? 1 : q.Page;
        var pageSize = q.PageSize switch
        {
            < 1 => 50,
            > 200 => 200,
            _ => q.PageSize,
        };

        var query = _db.Tickets.AsNoTracking()
            .Where(t => t.Category == TicketCategoryNames.RedactionRequest
                     && t.State != TicketStateNames.Closed);

        if (!string.IsNullOrWhiteSpace(q.MarketCode))
        {
            query = query.Where(t => t.MarketCode == q.MarketCode);
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(t => t.FirstResponseDueUtc)
            .ThenBy(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new RedactionRequestQueueRow(
                t.Id,
                t.CustomerId,
                t.MarketCode,
                t.State,
                t.Subject,
                t.CreatedAtUtc,
                t.FirstResponseDueUtc,
                t.BreachAcknowledgedAtFirstResponse))
            .ToListAsync(ct);

        return new ListRedactionRequestQueueResult(rows, page, pageSize, total);
    }
}
