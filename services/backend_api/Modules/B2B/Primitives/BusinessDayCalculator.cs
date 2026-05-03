using System.Text.Json;

namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 business-day calculator (research §R2). Pure function; no DI.
///
/// Deliberately duplicates spec 020's calculator rather than extracting to
/// <c>Modules/Shared/</c>: trivial pure function (~30 lines), and extracting introduces
/// a versioning-coupling concern (when does spec 020's calc need to differ from 021's?)
/// for de minimis savings. Spec 020 + 021 each own + test their own copy.
///
/// Conventions:
/// <list type="bullet">
///   <item>Working week defaults to Sun–Thu (KSA + EG common case). Caller MAY pass an
///         alternative <see cref="WeekendDays"/> set.</item>
///   <item>Holidays list is parsed from a jsonb-shaped <c>["YYYY-MM-DD", ...]</c> string
///         (matching <c>quote_market_schemas.holidays_list</c>). Empty / null treated as
///         "no holidays".</item>
///   <item>The result is the date <em>at-or-after</em> <c>start</c> reached after stepping
///         forward <c>businessDays</c> business days. <c>businessDays = 0</c> returns the
///         next business day at-or-after <c>start</c>.</item>
/// </list>
/// </summary>
public static class BusinessDayCalculator
{
    /// <summary>KSA + EG common working week — Sun through Thu. Friday and Saturday are weekend.</summary>
    public static readonly IReadOnlySet<DayOfWeek> WeekendDays = new HashSet<DayOfWeek>
    {
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
    };

    /// <summary>
    /// Returns the date reached after stepping forward <paramref name="businessDays"/>
    /// business days from <paramref name="start"/>, skipping weekend days and dates listed
    /// in <paramref name="holidaysListJson"/>.
    /// </summary>
    /// <remarks>
    /// Time component is preserved from <paramref name="start"/>. Callers persisting an
    /// SLA deadline typically pass a midnight-anchored timestamp.
    /// </remarks>
    public static DateTimeOffset AddBusinessDays(
        DateTimeOffset start,
        int businessDays,
        string? holidaysListJson = null,
        IReadOnlySet<DayOfWeek>? weekendDays = null)
    {
        if (businessDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(businessDays),
                businessDays,
                "Business days must be >= 0. Backwards SLA arithmetic isn't supported in V1.");
        }

        var weekend = weekendDays ?? WeekendDays;
        var holidays = ParseHolidays(holidaysListJson);

        // Advance to the next business day at-or-after `start` first; this makes
        // `businessDays = 0` return the next business day rather than (potentially) a weekend.
        var cursor = start;
        while (IsNonBusinessDay(cursor, weekend, holidays))
        {
            cursor = cursor.AddDays(1);
        }

        var remaining = businessDays;
        while (remaining > 0)
        {
            cursor = cursor.AddDays(1);
            if (!IsNonBusinessDay(cursor, weekend, holidays))
            {
                remaining--;
            }
        }

        return cursor;
    }

    private static bool IsNonBusinessDay(
        DateTimeOffset date,
        IReadOnlySet<DayOfWeek> weekend,
        IReadOnlySet<DateOnly> holidays)
    {
        if (weekend.Contains(date.DayOfWeek))
        {
            return true;
        }

        var dateOnly = DateOnly.FromDateTime(date.UtcDateTime);
        return holidays.Contains(dateOnly);
    }

    private static IReadOnlySet<DateOnly> ParseHolidays(string? holidaysListJson)
    {
        if (string.IsNullOrWhiteSpace(holidaysListJson))
        {
            return new HashSet<DateOnly>();
        }

        // jsonb stores arrays as `["YYYY-MM-DD", ...]`. Empty array is `[]`.
        try
        {
            using var doc = JsonDocument.Parse(holidaysListJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<DateOnly>();
            }

            var set = new HashSet<DateOnly>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }
                if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var parsed))
                {
                    set.Add(parsed);
                }
            }
            return set;
        }
        catch (JsonException)
        {
            // Schema rows are admin-curated; a malformed list is a configuration bug.
            // Don't crash the caller — treat as "no holidays" and let the integration
            // tests flag the malformed jsonb separately. (The seeder only emits `[]`.)
            return new HashSet<DateOnly>();
        }
    }
}
