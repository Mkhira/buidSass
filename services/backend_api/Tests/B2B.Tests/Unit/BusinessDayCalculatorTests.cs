using BackendApi.Modules.B2B.Primitives;
using FluentAssertions;

namespace B2B.Tests.Unit;

/// <summary>
/// Spec 021 T017 — Sun–Thu business week, holidays from a jsonb-shaped list, deterministic
/// arithmetic across weekend spans. Every test fixes time at UTC noon to keep the
/// DateTimeOffset semantics free of TZ surprises (the calculator uses UtcDateTime for
/// holiday lookups).
/// </summary>
public sealed class BusinessDayCalculatorTests
{
    private static DateTimeOffset Day(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Adding_zero_business_days_to_a_business_day_returns_same_day()
    {
        // Sunday — a business day in KSA / EG.
        var sunday = Day(2026, 5, 3);
        var result = BusinessDayCalculator.AddBusinessDays(sunday, 0);
        result.Should().Be(sunday);
    }

    [Fact]
    public void Adding_zero_business_days_to_a_weekend_advances_to_next_business_day()
    {
        // Friday → next business day is Sunday.
        var friday = Day(2026, 5, 1);
        var result = BusinessDayCalculator.AddBusinessDays(friday, 0);
        result.Should().Be(Day(2026, 5, 3));
    }

    [Fact]
    public void Adding_one_business_day_skips_the_friday_saturday_weekend()
    {
        // Thursday + 1 business day → Sunday (Fri + Sat skipped).
        var thursday = Day(2026, 4, 30);
        var result = BusinessDayCalculator.AddBusinessDays(thursday, 1);
        result.Should().Be(Day(2026, 5, 3));
    }

    [Fact]
    public void Adding_two_business_days_with_holiday_skips_holiday_too()
    {
        // Sunday + 2 business days normally = Tuesday. Mark Monday as holiday → Wednesday.
        var sunday = Day(2026, 5, 3);
        var holidays = "[\"2026-05-04\"]";
        var result = BusinessDayCalculator.AddBusinessDays(sunday, 2, holidays);
        result.Should().Be(Day(2026, 5, 6));
    }

    [Fact]
    public void Five_business_days_from_sunday_is_following_sunday()
    {
        var sunday = Day(2026, 5, 3);
        var result = BusinessDayCalculator.AddBusinessDays(sunday, 5);
        result.Should().Be(Day(2026, 5, 10));
    }

    [Fact]
    public void Negative_business_days_throws()
    {
        var act = () => BusinessDayCalculator.AddBusinessDays(Day(2026, 5, 3), -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Empty_or_null_holidays_list_treats_only_weekend_as_skip()
    {
        var sunday = Day(2026, 5, 3);
        BusinessDayCalculator.AddBusinessDays(sunday, 1, null).Should().Be(Day(2026, 5, 4));
        BusinessDayCalculator.AddBusinessDays(sunday, 1, "").Should().Be(Day(2026, 5, 4));
        BusinessDayCalculator.AddBusinessDays(sunday, 1, "[]").Should().Be(Day(2026, 5, 4));
    }

    [Fact]
    public void Malformed_holidays_jsonb_falls_through_to_no_holidays()
    {
        var sunday = Day(2026, 5, 3);
        // Invalid: not an array. Should not throw.
        var result = BusinessDayCalculator.AddBusinessDays(sunday, 1, "{\"oops\": true}");
        result.Should().Be(Day(2026, 5, 4));
    }

    [Fact]
    public void Custom_weekend_set_overrides_default()
    {
        // Caller passes Mon–Tue as weekend (synthetic): Sunday + 1 → Wednesday.
        var sunday = Day(2026, 5, 3);
        var customWeekend = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday };
        var result = BusinessDayCalculator.AddBusinessDays(sunday, 1, null, customWeekend);
        result.Should().Be(Day(2026, 5, 6));
    }
}
