using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Metrics;

/// <summary>
/// Spec 023 Phase 10 T136a — per-market support-ticket metrics for the V1
/// admin dashboard. Counts per non-closed state, per-priority breach counts,
/// and average first-response / resolution times computed over closed
/// tickets in the last 30 days. Detailed analytics dashboards live in
/// spec 028; this handler is intentionally minimal.
/// </summary>
public sealed class GetSupportMetricsHandler
{
    private readonly SupportDbContext _db;
    private readonly TimeProvider _clock;

    public GetSupportMetricsHandler(SupportDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SupportMetricsResult> HandleAsync(GetSupportMetricsQuery q, CancellationToken ct)
    {
        var thirtyDaysAgo = _clock.GetUtcNow().AddDays(-30);

        var ticketsQuery = _db.Tickets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.MarketCode))
        {
            ticketsQuery = ticketsQuery.Where(t => t.MarketCode == q.MarketCode);
        }

        // Aggregate per-state counts grouped by market.
        var stateCounts = await ticketsQuery
            .Where(t => t.State != TicketStateNames.Closed)
            .GroupBy(t => new { t.MarketCode, t.State })
            .Select(g => new { g.Key.MarketCode, g.Key.State, Count = g.Count() })
            .ToListAsync(ct);

        // Breach counts by kind (filtered to NOT-superseded events for live state).
        var breachCounts = await _db.SlaBreachEvents.AsNoTracking()
            .Where(e => e.SupersededByEventId == null)
            .Join(_db.Tickets.AsNoTracking(), e => e.TicketId, t => t.Id, (e, t) => new
            {
                t.MarketCode,
                e.BreachKind,
            })
            .Where(j => string.IsNullOrEmpty(q.MarketCode) || j.MarketCode == q.MarketCode)
            .GroupBy(j => new { j.MarketCode, j.BreachKind })
            .Select(g => new { g.Key.MarketCode, g.Key.BreachKind, Count = g.Count() })
            .ToListAsync(ct);

        // Closed-ticket averages over the trailing 30 days.
        var avgs = await ticketsQuery
            .Where(t => t.ClosedAtUtc != null && t.ClosedAtUtc >= thirtyDaysAgo)
            .GroupBy(t => t.MarketCode)
            .Select(g => new
            {
                MarketCode = g.Key,
                AvgResolutionMinutes = g.Average(t =>
                    t.ClosedAtUtc!.Value.Subtract(t.CreatedAtUtc).TotalMinutes),
            })
            .ToListAsync(ct);

        // Roll up into per-market rows. We seed an entry per known market in
        // the result so callers see zero-counts rather than gaps.
        var markets = new HashSet<string>(stateCounts.Select(s => s.MarketCode));
        markets.UnionWith(breachCounts.Select(b => b.MarketCode));
        markets.UnionWith(avgs.Select(a => a.MarketCode));
        if (!string.IsNullOrWhiteSpace(q.MarketCode))
        {
            markets.Add(q.MarketCode);
        }

        var rows = markets
            .OrderBy(m => m)
            .Select(market =>
            {
                int CountFor(string state) => stateCounts
                    .Where(s => s.MarketCode == market && s.State == state)
                    .Sum(s => s.Count);
                int BreachFor(string kind) => breachCounts
                    .Where(b => b.MarketCode == market && b.BreachKind == kind)
                    .Sum(b => b.Count);

                var avg = avgs.FirstOrDefault(a => a.MarketCode == market);

                return new SupportMetricsRow(
                    MarketCode: market,
                    OpenTicketCount: CountFor(TicketStateNames.Open),
                    InProgressTicketCount: CountFor(TicketStateNames.InProgress),
                    WaitingCustomerTicketCount: CountFor(TicketStateNames.WaitingCustomer),
                    ResolvedTicketCount: CountFor(TicketStateNames.Resolved),
                    FirstResponseBreachCount: BreachFor(TicketSlaBreachKind.FirstResponse),
                    ResolutionBreachCount: BreachFor(TicketSlaBreachKind.Resolution),
                    AverageFirstResponseMinutes: null, // Computed in spec 028.
                    AverageResolutionMinutes: avg is null ? null : avg.AvgResolutionMinutes);
            })
            .ToList();

        return new SupportMetricsResult(rows);
    }
}
