using BackendApi.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.ListAgentQueue;

public sealed record ListAgentQueueQuery(
    string? MarketCode,
    string[]? Categories,
    string[]? Priorities,
    string[]? States,
    Guid? AssignedAgentId,
    string? SlaBreachStatus,
    string? LinkedEntityKind,
    DateTimeOffset? CreatedAfter,
    DateTimeOffset? CreatedBefore,
    int Skip,
    int Take);

public sealed record AgentQueueItem(
    Guid Id,
    string State,
    string Category,
    string Priority,
    string Subject,
    string MarketCode,
    Guid? AssignedAgentId,
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset ResolutionDueUtc,
    DateTimeOffset? BreachAcknowledgedAtFirstResponse,
    DateTimeOffset? BreachAcknowledgedAtResolution,
    string? LinkedEntityKind,
    Guid? LinkedEntityId,
    DateTimeOffset CreatedAtUtc);

public sealed record ListAgentQueueResult(IReadOnlyList<AgentQueueItem> Items, int Total);

public sealed class ListAgentQueueHandler
{
    private readonly SupportDbContext _db;

    public ListAgentQueueHandler(SupportDbContext db) => _db = db;

    public async Task<ListAgentQueueResult> HandleAsync(ListAgentQueueQuery q, CancellationToken ct)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q.MarketCode))
        {
            query = query.Where(t => t.MarketCode == q.MarketCode);
        }
        if (q.Categories is { Length: > 0 })
        {
            query = query.Where(t => q.Categories.Contains(t.Category));
        }
        if (q.Priorities is { Length: > 0 })
        {
            query = query.Where(t => q.Priorities.Contains(t.Priority));
        }
        if (q.States is { Length: > 0 })
        {
            query = query.Where(t => q.States.Contains(t.State));
        }
        else
        {
            // Default: open + in_progress + waiting_customer (operational queue scope).
            query = query.Where(t => t.State != "closed");
        }
        if (q.AssignedAgentId is not null)
        {
            query = query.Where(t => t.AssignedAgentId == q.AssignedAgentId);
        }
        if (q.LinkedEntityKind is not null)
        {
            query = query.Where(t => t.LinkedEntityKind == q.LinkedEntityKind);
        }
        if (q.CreatedAfter is not null)
        {
            query = query.Where(t => t.CreatedAtUtc >= q.CreatedAfter);
        }
        if (q.CreatedBefore is not null)
        {
            query = query.Where(t => t.CreatedAtUtc < q.CreatedBefore);
        }
        if (q.SlaBreachStatus is "breached_first_response")
        {
            query = query.Where(t => t.BreachAcknowledgedAtFirstResponse != null);
        }
        else if (q.SlaBreachStatus is "breached_resolution")
        {
            query = query.Where(t => t.BreachAcknowledgedAtResolution != null);
        }
        else if (q.SlaBreachStatus is "both")
        {
            query = query.Where(t => t.BreachAcknowledgedAtFirstResponse != null
                || t.BreachAcknowledgedAtResolution != null);
        }
        else if (q.SlaBreachStatus is "none")
        {
            query = query.Where(t => t.BreachAcknowledgedAtFirstResponse == null
                && t.BreachAcknowledgedAtResolution == null);
        }

        var total = await query.CountAsync(ct);

        var take = Math.Clamp(q.Take, 1, 200);
        var skip = Math.Max(0, q.Skip);

        // Default sort: priority DESC then first_response_due ASC then created ASC
        // (worst-priority + oldest-due first per contracts §2 default sort).
        var rows = await query
            .OrderBy(t => t.FirstResponseDueUtc)
            .ThenBy(t => t.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(t => new AgentQueueItem(
                t.Id, t.State, t.Category, t.Priority, t.Subject, t.MarketCode,
                t.AssignedAgentId, t.FirstResponseDueUtc, t.ResolutionDueUtc,
                t.BreachAcknowledgedAtFirstResponse, t.BreachAcknowledgedAtResolution,
                t.LinkedEntityKind, t.LinkedEntityId, t.CreatedAtUtc))
            .ToListAsync(ct);

        return new ListAgentQueueResult(rows, total);
    }
}
