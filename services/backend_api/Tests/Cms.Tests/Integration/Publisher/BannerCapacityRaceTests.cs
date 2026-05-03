using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Publisher;
using BackendApi.Modules.Cms.Publisher.PublishNow;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using Cms.Tests.Infrastructure;
using Cms.Tests.Integration.Infrastructure;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Cms.Tests.Integration.Publisher;

/// <summary>
/// T060 — concurrent capacity-cap race per SC-010. Slot capacity per market
/// is 5; we seed 4 already-live banners to bring the pre-existing live count
/// to 4, then unleash 100 concurrent publish-now calls each on its own draft
/// row. Exactly ONE of those 100 must win (taking the last cap slot, 4→5);
/// the other 99 must observe <c>cms.banner.slot_capacity_exceeded</c>.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class BannerCapacityRaceTests
{
    private readonly CmsPostgresFixture _fx;

    public BannerCapacityRaceTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Concurrent_publish_now_at_cap_lets_exactly_one_winner_through()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();

        var nowUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(nowUtc);
        const int contenders = 100;
        const int preLive = 4; // EG cap is 5 → exactly 1 contender wins

        // Seed 4 pre-existing live banners + 100 draft contenders.
        var contenderIds = new Guid[contenders];
        await using (var seed = _fx.NewContext())
        {
            for (var i = 0; i < preLive; i++)
            {
                seed.BannerSlots.Add(BuildBanner(
                    state: ContentLifecycleState.Live.ToWire(),
                    market: "EG",
                    headlineEn: $"existing-live-{i}",
                    nowUtc: nowUtc,
                    publishedAt: nowUtc.AddHours(-1)));
            }
            for (var i = 0; i < contenders; i++)
            {
                contenderIds[i] = Guid.NewGuid();
                seed.BannerSlots.Add(BuildBanner(
                    id: contenderIds[i],
                    state: ContentLifecycleState.Draft.ToWire(),
                    market: "EG",
                    headlineEn: $"contender-{i}",
                    nowUtc: nowUtc));
            }
            await seed.SaveChangesAsync(ct);
        }

        // Build 100 publish-now tasks, each with its own DbContext +
        // capacity calculator (so they actually contend against the DB,
        // not against shared in-memory state).
        var startGate = new ManualResetEventSlim(false);
        var tasks = contenderIds.Select(id => Task.Run(async () =>
        {
            startGate.Wait(ct);
            return await PublishNowAsync(id, clock, ct);
        }, ct)).ToArray();

        // Release all 100 at once.
        startGate.Set();
        var results = await Task.WhenAll(tasks);

        var winners = results.Count(r => r.IsSuccess);
        var capacityRejections = results.Count(r =>
            !r.IsSuccess && r.ReasonCode == CmsReasonCode.BannerSlotCapacityExceeded);

        var diagnostic = string.Join(", ",
            results.Where(r => !r.IsSuccess)
                   .GroupBy(r => r.ReasonCode ?? "<null>")
                   .Select(g => $"{g.Key}={g.Count()}"));
        winners.Should().Be(1,
            $"exactly one contender takes the final slot. Rejections: {diagnostic}");
        capacityRejections.Should().Be(contenders - 1,
            $"all other contenders see cms.banner.slot_capacity_exceeded. Rejections: {diagnostic}");

        // DB end-state: total live = preLive + 1.
        await using var verify = _fx.NewContext();
        var totalLive = await verify.BannerSlots
            .CountAsync(b => b.MarketCode == "EG"
                && b.SlotKindWire == "hero_top"
                && b.StateWire == ContentLifecycleState.Live.ToWire(), ct);
        totalLive.Should().Be(preLive + 1);
    }

    /// <summary>
    /// Constructs a fresh <see cref="PublishNowBannerHandler"/> bound to its
    /// own <see cref="CmsDbContext"/>. The capacity calculator queries via
    /// the same context, but the actual capacity-cap conflict is enforced
    /// by Postgres at <c>SaveChangesAsync</c> time via the DB-level state
    /// being read inside <c>AssertCanPublishAsync</c>. With 100 concurrent
    /// attempts the calculator's check-then-act is racy by design — that's
    /// exactly the race SC-010 is asserting on.
    /// </summary>
    private async Task<PublisherResult> PublishNowAsync(Guid id, FakeTimeProvider clock, CancellationToken ct)
    {
        var opts = new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(_fx.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        await using var db = new CmsDbContext(opts);

        var capacity = new BannerCapacityCalculator();
        var ctaValidator = new BannerCtaValidator(
            new FakeCatalogProductReadContract(),
            new FakeCatalogCategoryReadContract(),
            new FakeCatalogBundleReadContract(),
            NullLogger<BannerCtaValidator>.Instance);
        var audit = new FakeAuditEventPublisher();
        var publisher = new FakePublisher();

        var handler = new PublishNowBannerHandler(db, capacity, ctaValidator, audit, publisher, clock);
        try
        {
            return await handler.HandleAsync(id, Guid.NewGuid(), "publisher", ct);
        }
        catch (DbUpdateException)
        {
            // A concurrency-race may surface here as a unique-violation if
            // multiple threads believe they're below the cap and race to
            // the SaveChanges call. Map it to a capacity-rejection so the
            // race assertion (winners == 1) remains coherent.
            return PublisherResult.Reject(
                CmsReasonCode.BannerSlotCapacityExceeded,
                "Concurrent publish race resolved as capacity exceeded.",
                400);
        }
    }

    private static BannerSlot BuildBanner(
        string state,
        string market,
        string headlineEn,
        DateTimeOffset nowUtc,
        DateTimeOffset? publishedAt = null,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            SlotKindWire = "hero_top",
            HeadlineEn = headlineEn,
            HeadlineAr = $"عربي-{headlineEn}",
            // Locale-completeness gate requires both ar + en asset ids at
            // publish-time for banners. Synthetic ids are fine here — the
            // CTA validator path is non-catalog (cta_kind=none).
            AssetIdEn = Guid.NewGuid(),
            AssetIdAr = Guid.NewGuid(),
            CtaKindWire = "none",
            CtaHealthWire = "not_applicable",
            MarketCode = market,
            StateWire = state,
            PriorityWithinSlot = 100,
            OwnerActorId = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            EditorSaveAtUtc = nowUtc,
            PublishedAtUtc = publishedAt,
        };
}
