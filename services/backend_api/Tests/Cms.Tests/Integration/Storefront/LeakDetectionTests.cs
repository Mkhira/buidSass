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

    // SC-003 — leak prevention covers every entity kind that participates in
    // the storefront read path. Banner uses the windowed filter (above);
    // FAQ / FeaturedSection / BlogArticle / LegalPageVersion all rely on
    // ApplyStorefrontFilterStateOnly (their visibility is implicit in
    // state=live without a separate window column).

    [Fact]
    public async Task FaqEntries_excluded_when_not_live()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var seed = _fx.NewContext();
        seed.FaqEntries.AddRange(
            BuildFaq("draft", "EG", nowUtc),
            BuildFaq("scheduled", "EG", nowUtc),
            BuildFaq("archived", "EG", nowUtc),
            BuildFaq("live", "EG", nowUtc),
            BuildFaq("live", "KSA", nowUtc));
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var visible = await resolver
            .ApplyStorefrontFilterStateOnly(ctx.FaqEntries.AsNoTracking(), "EG")
            .ToListAsync(ct);
        visible.Should().HaveCount(1);
        visible[0].StateWire.Should().Be("live");
        visible[0].MarketCode.Should().Be("EG");
    }

    [Fact]
    public async Task FeaturedSections_excluded_when_not_live()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var seed = _fx.NewContext();
        seed.FeaturedSections.AddRange(
            BuildFeatured("draft", "EG", nowUtc),
            BuildFeatured("scheduled", "EG", nowUtc),
            BuildFeatured("archived", "EG", nowUtc),
            BuildFeatured("live", "EG", nowUtc),
            BuildFeatured("live", "*", nowUtc));
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var visible = await resolver
            .ApplyStorefrontFilterStateOnly(ctx.FeaturedSections.AsNoTracking(), "EG")
            .ToListAsync(ct);
        visible.Should().HaveCount(2);
        visible.Select(v => v.MarketCode).Should().BeEquivalentTo(new[] { "EG", "*" });
        visible[0].MarketCode.Should().Be("EG");
    }

    [Fact]
    public async Task BlogArticles_excluded_when_not_live()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var seed = _fx.NewContext();
        seed.BlogArticles.AddRange(
            BuildBlog("draft", "EG", "ar", "draft-slug", nowUtc),
            BuildBlog("scheduled", "EG", "en", "sched-slug", nowUtc),
            BuildBlog("archived", "EG", "ar", "arch-slug", nowUtc),
            BuildBlog("live", "EG", "en", "live-slug", nowUtc));
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var visible = await resolver
            .ApplyStorefrontFilterStateOnly(ctx.BlogArticles.AsNoTracking(), "EG")
            .ToListAsync(ct);
        visible.Should().HaveCount(1);
        visible[0].StateWire.Should().Be("live");
        visible[0].Slug.Should().Be("live-slug");
    }

    [Fact]
    public async Task LegalPageVersions_excluded_when_not_live()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();
        var nowUtc = DateTimeOffset.UtcNow;

        await using var seed = _fx.NewContext();
        seed.LegalPageVersions.AddRange(
            BuildLegal("draft", "terms", "EG", "v1.0", nowUtc),
            BuildLegal("scheduled", "terms", "EG", "v1.1", nowUtc),
            BuildLegal("superseded", "terms", "EG", "v0.9", nowUtc),
            BuildLegal("live", "terms", "EG", "v2.0", nowUtc));
        await seed.SaveChangesAsync(ct);

        await using var ctx = _fx.NewContext();
        var resolver = new StorefrontContentResolver();
        var visible = await resolver
            .ApplyStorefrontFilterStateOnly(ctx.LegalPageVersions.AsNoTracking(), "EG")
            .ToListAsync(ct);
        visible.Should().HaveCount(1);
        visible[0].StateWire.Should().Be("live");
        visible[0].VersionLabel.Should().Be("v2.0");
    }

    private static FaqEntry BuildFaq(string state, string market, DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            CategoryWire = "ordering",
            QuestionEn = $"q-{state}",
            QuestionAr = $"س-{state}",
            AnswerEn = "answer",
            AnswerAr = "إجابة",
            MarketCode = market,
            StateWire = state,
            DisplayOrder = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = state == "live" || state == "archived" ? nowUtc : null,
            ArchivedAtUtc = state == "archived" ? nowUtc : null,
            ArchiveReasonNote = state == "archived" ? "test" : null,
        };

    private static FeaturedSection BuildFeatured(string state, string market, DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            SectionKindWire = "home_top",
            TitleEn = $"feat-{state}",
            TitleAr = $"مميز-{state}",
            ReferencesJson = "[]",
            MarketCode = market,
            StateWire = state,
            DisplayPriority = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = state == "live" || state == "archived" ? nowUtc : null,
            ArchivedAtUtc = state == "archived" ? nowUtc : null,
            ArchiveReasonNote = state == "archived" ? "test" : null,
        };

    private static BlogArticle BuildBlog(string state, string market, string locale, string slug, DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            CategoryWire = "tips",
            Slug = slug,
            AuthoredLocale = locale,
            Title = $"blog-{state}",
            Body = "body",
            MarketCode = market,
            StateWire = state,
            SeoMetaTitle = "title",
            SeoMetaDescription = "desc",
            SeoSchemaOrgKind = "BlogPosting",
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = state == "live" || state == "archived" ? nowUtc : null,
            ArchivedAtUtc = state == "archived" ? nowUtc : null,
            ArchiveReasonNote = state == "archived" ? "test" : null,
        };

    private static LegalPageVersion BuildLegal(string state, string kind, string market, string label, DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            LegalPageKindWire = kind,
            VersionLabel = label,
            BodyAr = "ع",
            BodyEn = "en",
            EffectiveAtUtc = nowUtc,
            MarketCode = market,
            StateWire = state,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = state == "live" || state == "superseded" ? nowUtc : null,
            SupersededAtUtc = state == "superseded" ? nowUtc : null,
            SupersededByVersionId = state == "superseded" ? Guid.NewGuid() : null,
        };
}
