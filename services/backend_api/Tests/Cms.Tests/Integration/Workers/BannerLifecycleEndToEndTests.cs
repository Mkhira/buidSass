using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.LegalOwner;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Cms.Workers;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using Cms.Tests.Infrastructure;
using Cms.Tests.Integration.Infrastructure;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Cms.Tests.Integration.Workers;

/// <summary>
/// T059 — end-to-end banner lifecycle integration test. Drives the
/// <see cref="CmsScheduledPublishWorker"/> against Postgres with a
/// <see cref="FakeTimeProvider"/> in place of the system clock; advances
/// time across the scheduled-start and scheduled-end boundaries and asserts
/// that the worker promotes draft→scheduled (via direct seeding to mirror the
/// editor + publisher slices), scheduled→live, and live→archived per
/// spec 024 quickstart §1.
/// </summary>
[Collection(nameof(CmsPostgresCollection))]
public sealed class BannerLifecycleEndToEndTests
{
    private readonly CmsPostgresFixture _fx;

    public BannerLifecycleEndToEndTests(CmsPostgresFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Worker_promotes_scheduled_to_live_then_archived_as_clock_advances()
    {
        var ct = CancellationToken.None;
        await _fx.ResetAsync();

        var startUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startUtc);
        var bannerId = Guid.NewGuid();

        // Editor → Publisher: a "scheduled" banner ready for promotion.
        // This mirrors the path SaveBannerDraft → SchedulePublishBanner takes
        // without re-running those slice handlers; the worker is the unit
        // under test here. (Both slices are covered by their own contract
        // tests in CmsContractTests / CmsExtendedContractTests.)
        await using (var seed = _fx.NewContext())
        {
            seed.BannerSlots.Add(new BannerSlot
            {
                Id = bannerId,
                SlotKindWire = "hero_top",
                HeadlineEn = "Summer banner",
                HeadlineAr = "بانر الصيف",
                CtaKindWire = "none",
                CtaHealthWire = "not_applicable",
                MarketCode = "EG",
                StateWire = ContentLifecycleState.Scheduled.ToWire(),
                PriorityWithinSlot = 100,
                OwnerActorId = Guid.NewGuid(),
                CreatedAtUtc = startUtc,
                EditorSaveAtUtc = startUtc,
                ScheduledStartUtc = startUtc.AddDays(7),  // promotes after +7d
                ScheduledEndUtc = startUtc.AddDays(14),   // archives after +14d
            });
            await seed.SaveChangesAsync(ct);
        }

        // Build a service provider exposing the worker's dependencies.
        var sp = BuildScopeFactory(clock, out var fakeAudit, out var fakePublisher);

        var worker = new CmsScheduledPublishWorker(sp, clock, NullLogger<CmsScheduledPublishWorker>.Instance);

        // 1) Tick before scheduled_start_utc — row stays scheduled.
        await worker.TickAsync(ct);
        await using (var verify = _fx.NewContext())
        {
            var row = await verify.BannerSlots.FirstAsync(b => b.Id == bannerId, ct);
            row.StateWire.Should().Be(ContentLifecycleState.Scheduled.ToWire());
        }

        // 2) Advance to scheduled_start — worker promotes to live.
        clock.Advance(TimeSpan.FromDays(7));
        await worker.TickAsync(ct);
        await using (var verify = _fx.NewContext())
        {
            var row = await verify.BannerSlots.FirstAsync(b => b.Id == bannerId, ct);
            row.StateWire.Should().Be(ContentLifecycleState.Live.ToWire());
            row.PublishedAtUtc.Should().NotBeNull();
        }

        // 3) Advance past scheduled_end — worker archives.
        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        await worker.TickAsync(ct);
        await using (var verify = _fx.NewContext())
        {
            var row = await verify.BannerSlots.FirstAsync(b => b.Id == bannerId, ct);
            row.StateWire.Should().Be(ContentLifecycleState.Archived.ToWire());
            row.ArchivedAtUtc.Should().NotBeNull();
            row.ArchiveReasonNote.Should().NotBeNullOrEmpty();
        }

        // Domain events: one CmsBannerPublished + one CmsBannerArchived
        // (and cache-invalidate notifications). Don't pin exact counts of
        // cache events — assert presence of the lifecycle events.
        fakePublisher.Events.Should().Contain(e => e is CmsBannerPublished);
        fakePublisher.Events.Should().Contain(e => e is CmsBannerArchived);

        // Audit trail records both transitions.
        fakeAudit.Events.Should().Contain(e =>
            e.EntityType == "cms.banner_slot" && e.Action == "cms.content.published");
        fakeAudit.Events.Should().Contain(e =>
            e.EntityType == "cms.banner_slot" && e.Action == "cms.content.archived");
    }

    private IServiceScopeFactory BuildScopeFactory(
        FakeTimeProvider clock,
        out FakeAuditEventPublisher audit,
        out FakePublisher publisher)
    {
        audit = new FakeAuditEventPublisher();
        publisher = new FakePublisher();
        var auditCapture = audit;
        var publisherCapture = publisher;
        var connStr = _fx.ConnectionString;

        var services = new ServiceCollection();
        services.AddDbContext<CmsDbContext>(opt =>
        {
            opt.UseNpgsql(connStr);
            opt.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<BannerCapacityCalculator>();
        services.AddSingleton<ICatalogProductReadContract, FakeCatalogProductReadContract>();
        services.AddSingleton<ICatalogCategoryReadContract, FakeCatalogCategoryReadContract>();
        services.AddSingleton<ICatalogBundleReadContract, FakeCatalogBundleReadContract>();
        services.AddSingleton<BannerCtaValidator>();
        services.AddSingleton<LegalPageSupersessionTransaction>();
        services.AddSingleton<IAuditEventPublisher>(_ => auditCapture);
        services.AddSingleton<IPublisher>(_ => publisherCapture);
        services.AddLogging();

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
