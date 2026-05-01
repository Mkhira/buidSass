using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Storefront;
using Cms.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Cms.Tests.Integration.Storefront;

/// <summary>
/// T073 — SC-003 storefront leak detection. For each entity kind, seed every
/// non-`live` state and prove the resolver filters them all out.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class LeakDetectionTests
{
    private readonly CmsPostgresFixture _fx;

    public LeakDetectionTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Storefront_filter_excludes_every_non_live_state_for_banner_slots()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(nowUtc);

        await using var seed = _fx.NewContext();
        seed.BannerSlots.AddRange(
            BuildBanner("draft",     "EG",  nowUtc),
            BuildBanner("scheduled", "EG",  nowUtc),
            BuildBanner("archived",  "EG",  nowUtc),
            BuildBanner("live",      "EG",  nowUtc),
            // KSA banner is live in a different market — must not leak into EG storefront.
            BuildBanner("live",      "KSA", nowUtc));
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var visible = await resolver.ApplyStorefrontFilter(
                ctx.BannerSlots.AsNoTracking(),
                marketCode: "EG",
                clock.GetUtcNow())
            .ToListAsync(ct);

        visible.Should().HaveCount(1);
        visible[0].StateWire.Should().Be("live");
        visible[0].MarketCode.Should().Be("EG");
    }

    [Fact]
    public async Task Storefront_filter_excludes_closed_window_rows()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(nowUtc);

        await using var seed = _fx.NewContext();
        // Live but window not yet open.
        var future = BuildBanner("live", "EG", nowUtc);
        future.ScheduledStartUtc = nowUtc.AddDays(1);
        // Live but window already closed.
        var past = BuildBanner("live", "EG", nowUtc);
        past.ScheduledEndUtc = nowUtc.AddDays(-1);
        // Live with both nulls — always visible.
        var open = BuildBanner("live", "EG", nowUtc);
        seed.BannerSlots.AddRange(future, past, open);
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var visible = await resolver.ApplyStorefrontFilter(
                ctx.BannerSlots.AsNoTracking(), "EG", clock.GetUtcNow())
            .ToListAsync(ct);
        visible.Should().HaveCount(1);
        visible[0].Id.Should().Be(open.Id);
    }

    [Fact]
    public async Task Storefront_filter_returns_specific_market_first_then_star()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var seed = _fx.NewContext();
        seed.BannerSlots.AddRange(
            BuildBanner("live", "*",  nowUtc),
            BuildBanner("live", "EG", nowUtc));
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var sorted = await resolver.ApplyStorefrontFilter(
                ctx.BannerSlots.AsNoTracking(), "EG", nowUtc)
            .ToListAsync(ct);
        sorted.Should().HaveCount(2);
        sorted[0].MarketCode.Should().Be("EG");
        sorted[1].MarketCode.Should().Be("*");
    }

    private static BannerSlot BuildBanner(string state, string market, DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            SlotKindWire = "hero_top",
            HeadlineEn = $"banner-{state}-{market}",
            HeadlineAr = $"بانر-{state}-{market}",
            CtaKindWire = "none",
            MarketCode = market,
            StateWire = state,
            CtaHealthWire = "not_applicable",
            PriorityWithinSlot = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = state == "live" || state == "archived" ? nowUtc : null,
            ArchivedAtUtc = state == "archived" ? nowUtc : null,
            ArchiveReasonNote = state == "archived" ? "test" : null,
        };
}
