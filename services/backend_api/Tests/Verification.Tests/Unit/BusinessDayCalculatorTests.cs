using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;

namespace Verification.Tests.Unit;

/// <summary>
/// Pure-function coverage of <see cref="BusinessDayCalculator"/> per spec 020
/// research §R2 (Sun–Thu working week; per-market holiday list; deterministic
/// SLA arithmetic).
/// </summary>
public sealed class BusinessDayCalculatorTests
{
    [Fact]
    public void AddBusinessDays_zero_is_identity()
    {
        var start = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero); // Sunday — business day
        BusinessDayCalculator.AddBusinessDays(start, 0).Should().Be(start);
    }

    [Fact]
    public void AddBusinessDays_negative_throws()
    {
        var start = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero);
        var act = () => BusinessDayCalculator.AddBusinessDays(start, -1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("businessDays");
    }

    [Fact]
    public void AddBusinessDays_skips_weekend_when_default_weekend_used()
    {
        // 2026-05-07 is a Thursday in UTC. Adding 1 business day should land on
        // 2026-05-10 (Sunday) skipping Friday + Saturday.
        var thursday = new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero);
        var result = BusinessDayCalculator.AddBusinessDays(thursday, 1);
        result.Date.Should().Be(new DateTime(2026, 5, 10));
    }

    [Fact]
    public void AddBusinessDays_two_business_days_from_thursday_lands_monday()
    {
        // From Thu 2026-05-07: +1 = Sun 2026-05-10 (after the weekend), +2 = Mon 2026-05-11.
        var thursday = new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero);
        var result = BusinessDayCalculator.AddBusinessDays(thursday, 2);
        result.Date.Should().Be(new DateTime(2026, 5, 11));
    }

    [Fact]
    public void AddBusinessDays_skips_holidays()
    {
        // Sun 2026-05-03 + 2 business days normally → Tue 2026-05-05.
        // With Mon 2026-05-04 as a holiday → +1 lands on Tue, +2 lands on Wed.
        var sunday = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero);
        var holidays = new[] { new DateOnly(2026, 5, 4) };
        var result = BusinessDayCalculator.AddBusinessDays(
            sunday, 2, weekendDays: null, holidays: holidays);
        result.Date.Should().Be(new DateTime(2026, 5, 6));
    }

    [Fact]
    public void BusinessDaysBetween_same_instant_is_zero()
    {
        var t = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero);
        BusinessDayCalculator.BusinessDaysBetween(t, t).Should().Be(0);
    }

    [Fact]
    public void BusinessDaysBetween_skips_weekend_with_default_weekend_set()
    {
        // Thu 2026-05-07 → Mon 2026-05-11 spans Thu, Fri (weekend),
        // Sat (weekend), Sun, Mon. Inclusive-exclusive count = 3 business days
        // (Thu + Sun + ... Mon is exclusive). Let's enumerate: Thu (yes),
        // Fri (skip), Sat (skip), Sun (yes), Mon (excluded since `to` is exclusive).
        // = 2 business days.
        var thu = new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero);
        var mon = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);
        BusinessDayCalculator.BusinessDaysBetween(thu, mon).Should().Be(2);
    }

    [Fact]
    public void BusinessDaysBetween_signed_when_to_before_from()
    {
        var thu = new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero);
        var mon = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);
        BusinessDayCalculator.BusinessDaysBetween(mon, thu).Should().Be(-2);
    }

    [Fact]
    public void DefaultWeekend_is_friday_and_saturday()
    {
        BusinessDayCalculator.DefaultWeekend.Should().BeEquivalentTo(new[]
        {
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
        });
    }

    [Fact]
    public void Spanning_multiple_weekends_advances_correctly()
    {
        // From Sun 2026-05-03 + 7 business days. AddBusinessDays advances the
        // cursor first then counts: +1=Mon-04, +2=Tue-05, +3=Wed-06, +4=Thu-07,
        // (Fri+Sat skipped), +5=Sun-10, +6=Mon-11, +7=Tue-12.
        var sunday = new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero);
        var result = BusinessDayCalculator.AddBusinessDays(sunday, 7);
        result.Date.Should().Be(new DateTime(2026, 5, 12));
    }
}
