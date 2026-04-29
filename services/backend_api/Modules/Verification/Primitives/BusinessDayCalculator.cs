namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Pure business-day arithmetic per spec 020 research §R2.
/// Working week defaults to Sunday–Thursday (KSA + EG convention); both the
/// weekend-day set and the holiday list are caller-supplied so per-market
/// schemas can override.
/// </summary>
public static class BusinessDayCalculator
{
    /// <summary>
    /// Default weekend (Friday + Saturday) for the dental commerce platform.
    /// Markets that ever need a different working week override via
    /// <c>VerificationMarketSchema.holidays_list</c> (see data-model §2.4) plus
    /// the <paramref name="weekendDays"/> parameter on
    /// <see cref="AddBusinessDays"/>.
    /// </summary>
    public static readonly IReadOnlySet<DayOfWeek> DefaultWeekend =
        new HashSet<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Saturday };

    /// <summary>
    /// Returns the UTC instant that is <paramref name="businessDays"/> business days
    /// after <paramref name="start"/>. Same-day call (zero business days) returns
    /// <paramref name="start"/> unchanged. Holidays are interpreted as full UTC
    /// dates (the day is skipped entirely).
    /// </summary>
    /// <param name="start">Start instant.</param>
    /// <param name="businessDays">Number of business days to add. MUST be ≥ 0.</param>
    /// <param name="weekendDays">Weekend day set; defaults to <see cref="DefaultWeekend"/>.</param>
    /// <param name="holidays">Holiday dates (UTC) to skip in addition to weekends.</param>
    public static DateTimeOffset AddBusinessDays(
        DateTimeOffset start,
        int businessDays,
        IReadOnlySet<DayOfWeek>? weekendDays = null,
        IReadOnlyCollection<DateOnly>? holidays = null)
    {
        if (businessDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(businessDays), businessDays, "businessDays MUST be ≥ 0.");
        }

        var weekend = weekendDays ?? DefaultWeekend;
        var holidaySet = holidays is null ? null : new HashSet<DateOnly>(holidays);

        if (businessDays == 0)
        {
            return start;
        }

        var cursor = start;
        var advanced = 0;
        while (advanced < businessDays)
        {
            cursor = cursor.AddDays(1);
            if (IsBusinessDay(cursor, weekend, holidaySet))
            {
                advanced++;
            }
        }

        return cursor;
    }

    /// <summary>
    /// Returns the count of business days between two instants (signed).
    /// Compares calendar dates: same calendar day → 0; one business day later
    /// → 1; etc. The "from" calendar day is counted; the "to" calendar day is
    /// excluded (matches SLA breach intuition — "submitted today, snapshot
    /// today" means 0 business days have elapsed).
    /// </summary>
    public static int BusinessDaysBetween(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlySet<DayOfWeek>? weekendDays = null,
        IReadOnlyCollection<DateOnly>? holidays = null)
    {
        if (from == to)
        {
            return 0;
        }

        var weekend = weekendDays ?? DefaultWeekend;
        var holidaySet = holidays is null ? null : new HashSet<DateOnly>(holidays);
        var sign = to >= from ? 1 : -1;
        var (lo, hi) = to >= from ? (from, to) : (to, from);

        // Compare calendar dates so sub-day spans on the same business day
        // count as 0 (the SLA-signal contract).
        var cursorDate = DateOnly.FromDateTime(lo.UtcDateTime);
        var hiDate = DateOnly.FromDateTime(hi.UtcDateTime);

        var count = 0;
        while (cursorDate < hiDate)
        {
            if (IsBusinessDay(cursorDate, weekend, holidaySet))
            {
                count++;
            }
            cursorDate = cursorDate.AddDays(1);
        }

        return sign * count;
    }

    private static bool IsBusinessDay(
        DateTimeOffset instant,
        IReadOnlySet<DayOfWeek> weekend,
        IReadOnlySet<DateOnly>? holidays)
        => IsBusinessDay(DateOnly.FromDateTime(instant.UtcDateTime),
            instant.UtcDateTime.DayOfWeek, weekend, holidays);

    private static bool IsBusinessDay(
        DateOnly date,
        IReadOnlySet<DayOfWeek> weekend,
        IReadOnlySet<DateOnly>? holidays)
        => IsBusinessDay(date, date.DayOfWeek, weekend, holidays);

    private static bool IsBusinessDay(
        DateOnly date,
        DayOfWeek dow,
        IReadOnlySet<DayOfWeek> weekend,
        IReadOnlySet<DateOnly>? holidays)
    {
        if (weekend.Contains(dow))
        {
            return false;
        }
        if (holidays is not null && holidays.Contains(date))
        {
            return false;
        }
        return true;
    }
}
