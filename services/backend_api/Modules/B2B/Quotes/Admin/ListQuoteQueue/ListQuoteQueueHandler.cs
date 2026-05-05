using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Admin.ListQuoteQueue;

/// <summary>
/// Spec 021 T085 — admin queue handler (contract §4.1). Returns paginated
/// <see cref="ListQuoteQueueRow"/> rows scoped to the caller's market with optional
/// filters. Per-row <c>sla_signal</c> is computed from the snapshotted market
/// schema's warning/breach business-day thresholds via
/// <see cref="BusinessDayCalculator"/>.
/// </summary>
public sealed class ListQuoteQueueHandler
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    private static readonly string[] NonTerminalStates = { "requested", "drafted", "revised", "pending-approver" };

    private readonly B2BDbContext _db;
    private readonly TimeProvider _time;

    public ListQuoteQueueHandler(B2BDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<ListQuoteQueueResponse> HandleAsync(
        string callerMarketCode,
        ListQuoteQueueRequest req,
        CancellationToken ct)
    {
        // CodeRabbit Round 1: market is ALWAYS the caller's claim — `req.Market` is
        // ignored to prevent cross-market scope expansion. The endpoint already
        // rejects mismatched query values with 400 quote.market_mismatch; this
        // assignment is the defense-in-depth layer at the handler boundary.
        var market = callerMarketCode.ToLowerInvariant();
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize <= 0 ? DefaultPageSize : req.PageSize, 1, MaxPageSize);

        var states = ParseStatesCsv(req.StatesCsv);

        var query = _db.Quotes.AsNoTracking().Where(q => q.MarketCode == market);
        query = query.Where(q => states.Contains(q.State));

        if (req.CompanyId is { } companyId)
        {
            query = query.Where(q => q.CompanyId == companyId);
        }
        if (req.CustomerId is { } customerId)
        {
            query = query.Where(q => q.CustomerId == customerId);
        }
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var search = req.Search.Trim();
            query = query.Where(q => q.PoNumber != null && EF.Functions.ILike(q.PoNumber, $"%{search}%"));
        }

        var sortDescending = string.Equals(req.Sort, "newest", StringComparison.OrdinalIgnoreCase);
        query = sortDescending
            ? query.OrderByDescending(q => q.RequestedAt)
            : query.OrderBy(q => q.RequestedAt);

        // CodeRabbit Round 1: when `age_min_business_days` filtering is active,
        // SLA age is derived in-memory (per-row schema lookup), so we cannot apply
        // it in SQL and pre-page. Materialize first, filter+paginate in memory
        // so `total` reflects the filtered count and pages don't drop qualifying
        // rows. When the filter is absent the SQL path is intact.
        var rows = await query
            .Select(q => new
            {
                q.Id,
                q.State,
                q.MarketCode,
                q.CompanyId,
                q.CustomerId,
                q.PoNumber,
                q.RequestedAt,
                q.ExpiresAt,
                q.SchemaVersion,
            })
            .ToListAsync(ct);

        // Resolve per-market schema thresholds. We typically only have ONE market in
        // play per query so a single lookup is enough; in heterogeneous-market admin
        // queries (rare) we batch-fetch all referenced schema versions.
        var schemaKeys = rows.Select(r => new { r.MarketCode, r.SchemaVersion }).Distinct().ToList();
        var schemas = new Dictionary<(string, int), QuoteMarketPolicy>();
        foreach (var k in schemaKeys)
        {
            var row = await _db.QuoteMarketSchemas
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.MarketCode == k.MarketCode && s.Version == k.SchemaVersion, ct);
            if (row is null) continue;
            schemas[(k.MarketCode, k.SchemaVersion)] = QuoteMarketPolicy.FromEntity(row);
        }

        var nowUtc = _time.GetUtcNow();
        var filtered = rows.Select(r =>
        {
            var schema = schemas.TryGetValue((r.MarketCode, r.SchemaVersion), out var s) ? s : null;
            var (age, signal) = ResolveSlaSignal(r.RequestedAt, nowUtc, schema);
            if (req.AgeMinBusinessDays is { } minAge && age < minAge) return null;
            return new ListQuoteQueueRow(
                Id: r.Id,
                State: r.State,
                MarketCode: r.MarketCode,
                CompanyId: r.CompanyId,
                CustomerId: r.CustomerId,
                PoNumber: r.PoNumber,
                RequestedAt: r.RequestedAt,
                ExpiresAt: r.ExpiresAt,
                AgeBusinessDays: age,
                SlaSignal: signal,
                TotalsSummary: null);
        }).Where(r => r is not null).Cast<ListQuoteQueueRow>().ToList();

        var total = filtered.Count;
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new ListQuoteQueueResponse(items, page, pageSize, total);
    }

    private static IReadOnlyList<string> ParseStatesCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return NonTerminalStates;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToArray();
        return parts.Length == 0 ? NonTerminalStates : parts;
    }

    private static (int age, string signal) ResolveSlaSignal(
        DateTimeOffset requestedAt,
        DateTimeOffset nowUtc,
        QuoteMarketPolicy? schema)
    {
        // Count business days elapsed from requestedAt to nowUtc by walking forward
        // and tallying non-weekend, non-holiday calendar days. The BusinessDayCalculator
        // owns the AddBusinessDays primitive; this is the inverse — we count rather
        // than step. Same weekend convention (Fri/Sat) and same holiday list parse.
        var weekend = BusinessDayCalculator.WeekendDays;
        var holidaysJson = schema?.HolidaysListJson;
        var holidays = ParseHolidays(holidaysJson);
        var age = 0;
        var cursor = requestedAt.UtcDateTime.Date;
        var until = nowUtc.UtcDateTime.Date;
        while (cursor < until)
        {
            cursor = cursor.AddDays(1);
            if (weekend.Contains(cursor.DayOfWeek)) continue;
            if (holidays.Contains(DateOnly.FromDateTime(cursor))) continue;
            age++;
        }
        var warning = schema?.SlaWarningBusinessDays ?? 1;
        var breach = schema?.SlaDecisionBusinessDays ?? 2;
        var signal = age >= breach ? "breach"
            : age >= warning ? "warning"
            : "ok";
        return (age, signal);
    }

    private static HashSet<DateOnly> ParseHolidays(string? json)
    {
        var set = new HashSet<DateOnly>();
        if (string.IsNullOrWhiteSpace(json)) return set;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return set;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.String
                    && DateOnly.TryParse(el.GetString(), out var d))
                {
                    set.Add(d);
                }
            }
        }
        catch (System.Text.Json.JsonException) { }
        return set;
    }
}
