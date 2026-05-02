using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Storefront;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class StorefrontContentResolverTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string LiveWire = ContentLifecycleState.Live.ToWire();

    private readonly StorefrontContentResolver _resolver = new();

    [Fact]
    public void Filters_out_every_non_live_state()
    {
        var rows = new[]
        {
            Row("draft", "EG"),
            Row("scheduled", "EG"),
            Row(LiveWire, "EG"),
            Row("archived", "EG"),
            Row("superseded", "EG"),
        };

        var visible = _resolver.ApplyStorefrontFilter(rows.AsQueryable(), "EG", NowUtc).ToList();

        visible.Should().HaveCount(1);
        visible[0].StateWire.Should().Be(LiveWire);
    }

    [Fact]
    public void Filters_out_rows_outside_scheduling_window()
    {
        var rows = new[]
        {
            Row(LiveWire, "EG", start: NowUtc.AddHours(1), end: NowUtc.AddHours(2)),  // future window
            Row(LiveWire, "EG", start: NowUtc.AddHours(-2), end: NowUtc.AddHours(-1)), // past window
            Row(LiveWire, "EG", start: NowUtc.AddHours(-1), end: NowUtc.AddHours(1)),  // open window
            Row(LiveWire, "EG", start: null, end: null),                               // unbounded
        };

        var visible = _resolver.ApplyStorefrontFilter(rows.AsQueryable(), "EG", NowUtc).ToList();

        visible.Should().HaveCount(2);
    }

    [Fact]
    public void Two_tier_sort_specific_market_before_star()
    {
        var rows = new[]
        {
            Row(LiveWire, "*"),
            Row(LiveWire, "EG"),
            Row(LiveWire, "*"),
            Row(LiveWire, "EG"),
        };

        var sorted = _resolver.ApplyStorefrontFilter(rows.AsQueryable(), "EG", NowUtc).ToList();

        sorted.Should().HaveCount(4);
        sorted.Take(2).Should().AllSatisfy(r => r.MarketCode.Should().Be("EG"));
        sorted.Skip(2).Take(2).Should().AllSatisfy(r => r.MarketCode.Should().Be("*"));
    }

    [Fact]
    public void Other_markets_are_excluded_entirely()
    {
        var rows = new[]
        {
            Row(LiveWire, "EG"),
            Row(LiveWire, "KSA"),
            Row(LiveWire, "*"),
        };

        var sorted = _resolver.ApplyStorefrontFilter(rows.AsQueryable(), "EG", NowUtc).ToList();

        sorted.Should().HaveCount(2);
        sorted.Select(r => r.MarketCode).Should().BeEquivalentTo(new[] { "EG", "*" });
    }

    [Fact]
    public void Empty_market_throws()
    {
        var act = () => _resolver.ApplyStorefrontFilter(Array.Empty<TestRow>().AsQueryable(), "", NowUtc);
        act.Should().Throw<ArgumentException>();
    }

    private static TestRow Row(
        string state,
        string market,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null) =>
        new(state, market, start, end);

    public sealed record TestRow(
        string StateWire,
        string MarketCode,
        DateTimeOffset? ScheduledStartUtc,
        DateTimeOffset? ScheduledEndUtc) : ICmsContentRow
    {
        public DateTimeOffset? ScheduledPublishAtUtc => null;
    }
}
