using System.Text.Json;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Admin.ListVerificationQueue;

/// <summary>
/// Spec 020 contracts §3.1 — reviewer queue. Defaults to non-terminal +
/// non-deferred rows (submitted | in_review | info_requested) ordered oldest-first.
/// SLA signal is computed per-row via <see cref="BusinessDayCalculator"/> against
/// the row's snapshotted schema (FR-026 — schema-as-submitted, never current).
/// </summary>
public sealed class ListVerificationQueueHandler(VerificationDbContext db, TimeProvider clock)
{
    public async Task<ListVerificationQueueResponse> HandleAsync(
        IReadOnlySet<string> reviewerMarkets,
        ListVerificationQueueQuery query,
        CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(query.Page, 1);

        // Default state filter: only non-terminal rows that need attention.
        // We work in the typed enum domain so EF can translate through the
        // VerificationState ↔ wire-string ValueConverter cleanly.
        var defaultStates = new HashSet<VerificationState>
        {
            VerificationState.Submitted,
            VerificationState.InReview,
            VerificationState.InfoRequested,
        };
        HashSet<VerificationState> stateFilter;
        if (query.StateFilter is { Count: > 0 } sf)
        {
            stateFilter = new HashSet<VerificationState>();
            foreach (var s in sf)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (VerificationStateExtensions.TryParseWireValue(s.Trim().ToLowerInvariant(), out var parsed))
                {
                    stateFilter.Add(parsed);
                }
            }
            if (stateFilter.Count == 0)
            {
                stateFilter = defaultStates;
            }
        }
        else
        {
            stateFilter = defaultStates;
        }

        // Market scope: intersect query filter with reviewer's assigned markets.
        var marketScope = string.IsNullOrWhiteSpace(query.MarketFilter)
            ? reviewerMarkets
            : new HashSet<string> { query.MarketFilter.Trim().ToLowerInvariant() }
                .Intersect(reviewerMarkets, StringComparer.Ordinal)
                .ToHashSet();

        if (marketScope.Count == 0)
        {
            return new ListVerificationQueueResponse(
                Items: Array.Empty<ListVerificationQueueRow>(),
                Page: page,
                PageSize: pageSize,
                TotalCount: 0);
        }

        var baseQuery = db.Verifications
            .AsNoTracking()
            .Where(v => marketScope.Contains(v.MarketCode));

        // Filter by state (enum domain — EF translates through the ValueConverter).
        baseQuery = baseQuery.Where(v => stateFilter.Contains(v.State));

        if (query.ProfessionFilter is { Count: > 0 } pf)
        {
            var profSet = pf.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToHashSet();
            baseQuery = baseQuery.Where(v => profSet.Contains(v.Profession));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Exact match on regulator_identifier — never substring (PII surface).
            // Normalize to uppercase: the submission validator enforces
            // ^[A-Z0-9-]{6,20}$ via the schema's required_fields jsonb, so
            // stored values are always uppercase. Reviewer search input may
            // arrive in any case (paste from email, user-typed); normalizing
            // here means a lowercased "scfhs-1234567" still finds the row.
            var search = query.Search.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(v => v.RegulatorIdentifier == search);
        }

        // Sort.
        baseQuery = string.Equals(query.Sort, "newest", StringComparison.OrdinalIgnoreCase)
            ? baseQuery.OrderByDescending(v => v.SubmittedAt)
            : baseQuery.OrderBy(v => v.SubmittedAt);

        var totalCount = await baseQuery.CountAsync(ct);

        var verificationRows = await baseQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new
            {
                v.Id,
                v.MarketCode,
                v.SchemaVersion,
                v.State,
                v.Profession,
                v.SubmittedAt,
            })
            .ToListAsync(ct);

        // Look up the schemas referenced by the visible rows so we can compute SLA per row.
        var schemaKeys = verificationRows
            .Select(r => new { r.MarketCode, r.SchemaVersion })
            .Distinct()
            .ToList();

        var schemaLookup = new Dictionary<(string Market, int Version), VerificationSchemaPolicy>();
        foreach (var key in schemaKeys)
        {
            var schema = await db.MarketSchemas
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.MarketCode == key.MarketCode && s.Version == key.SchemaVersion,
                    ct);
            if (schema is null)
            {
                continue;
            }
            schemaLookup[(key.MarketCode, key.SchemaVersion)] = ToPolicy(schema);
        }

        var ageMinBusinessDays = query.AgeMinBusinessDays;

        var items = new List<ListVerificationQueueRow>(verificationRows.Count);
        foreach (var row in verificationRows)
        {
            var policy = schemaLookup.TryGetValue((row.MarketCode, row.SchemaVersion), out var p)
                ? p
                : new VerificationSchemaPolicy(SlaWarningBusinessDays: 1, SlaDecisionBusinessDays: 2, Holidays: Array.Empty<DateOnly>());

            var ageBusinessDays = BusinessDayCalculator.BusinessDaysBetween(
                from: row.SubmittedAt,
                to: nowUtc,
                weekendDays: null,
                holidays: policy.Holidays);

            // Optional age-min filter (per contracts §3.1 query param).
            if (ageMinBusinessDays is { } min && ageBusinessDays < min)
            {
                continue;
            }

            items.Add(new ListVerificationQueueRow(
                Id: row.Id,
                State: row.State.ToWireValue(),
                MarketCode: row.MarketCode,
                Profession: row.Profession,
                SubmittedAt: row.SubmittedAt,
                SlaSignal: ComputeSlaSignal(row.State, ageBusinessDays, policy),
                AgeBusinessDays: ageBusinessDays));
        }

        return new ListVerificationQueueResponse(
            Items: items,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount);
    }

    /// <summary>
    /// SLA signal per FR-039:
    /// <list type="bullet">
    ///   <item><see cref="VerificationState.InfoRequested"/> pauses the timer — always "ok".</item>
    ///   <item>otherwise: ≥ <c>SlaDecisionBusinessDays</c> = "breach";
    ///         ≥ <c>SlaWarningBusinessDays</c> = "warning"; else "ok".</item>
    /// </list>
    /// </summary>
    private static string ComputeSlaSignal(VerificationState state, int ageBusinessDays, VerificationSchemaPolicy policy)
    {
        if (state == VerificationState.InfoRequested)
        {
            return "ok";
        }

        if (ageBusinessDays >= policy.SlaDecisionBusinessDays)
        {
            return "breach";
        }

        if (ageBusinessDays >= policy.SlaWarningBusinessDays)
        {
            return "warning";
        }

        return "ok";
    }

    private static VerificationSchemaPolicy ToPolicy(Entities.VerificationMarketSchema row)
    {
        var holidays = string.IsNullOrWhiteSpace(row.HolidaysListJson)
            ? Array.Empty<DateOnly>()
            : ParseHolidays(row.HolidaysListJson);
        return new VerificationSchemaPolicy(
            SlaWarningBusinessDays: row.SlaWarningBusinessDays,
            SlaDecisionBusinessDays: row.SlaDecisionBusinessDays,
            Holidays: holidays);
    }

    private static IReadOnlyList<DateOnly> ParseHolidays(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<string[]>(json);
            if (raw is null || raw.Length == 0)
            {
                return Array.Empty<DateOnly>();
            }
            var dates = new List<DateOnly>(raw.Length);
            foreach (var s in raw)
            {
                if (DateOnly.TryParse(s, out var d))
                {
                    dates.Add(d);
                }
            }
            return dates;
        }
        catch (JsonException)
        {
            return Array.Empty<DateOnly>();
        }
    }

    private sealed record VerificationSchemaPolicy(
        int SlaWarningBusinessDays,
        int SlaDecisionBusinessDays,
        IReadOnlyList<DateOnly> Holidays);
}
