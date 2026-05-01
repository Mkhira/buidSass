using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Storefront.ListBannerSlots;

/// <summary>
/// GET /v1/storefront/cms/banner-slots per spec 024 contract §7.1.
/// Composes the leak-safe <see cref="StorefrontContentResolver"/> with the
/// per-kind sort (<c>priority_within_slot ASC, created_at_utc ASC</c>) and
/// re-validates each catalog-bound CTA at read time per research §R9 (fail-open
/// on transient errors with <see cref="CtaHealth.TransientUnverified"/>).
/// Banners with broken CTAs are filtered out and an event is emitted (rate-limited
/// 1/banner/hour via the row's <c>last_stale_alert_at_utc</c> reuse).
/// Headline / subhead / asset_id are resolved to the requested locale.
/// </summary>
public sealed class ListBannerSlotsHandler
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;
    private static readonly TimeSpan CtaBrokenAlertWindow = TimeSpan.FromHours(1);

    private readonly CmsDbContext _db;
    private readonly StorefrontContentResolver _resolver;
    private readonly BannerCtaValidator _ctaValidator;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;

    public ListBannerSlotsHandler(
        CmsDbContext db,
        StorefrontContentResolver resolver,
        BannerCtaValidator ctaValidator,
        IPublisher bus,
        TimeProvider clock)
    {
        _db = db;
        _resolver = resolver;
        _ctaValidator = ctaValidator;
        _bus = bus;
        _clock = clock;
    }

    public async Task<ListBannerSlotsResponse> HandleAsync(ListBannerSlotsQuery query, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 1);

        var src = _db.BannerSlots.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.SlotKind))
        {
            src = src.Where(b => b.SlotKindWire == query.SlotKind);
        }

        var safe = _resolver.ApplyStorefrontFilter(src, query.Market, nowUtc);

        // Per-kind sort: priority_within_slot ASC, created_at_utc ASC.
        // Caller-tier sort already applied; we add the secondary keys here.
        var ordered = safe
            .ThenBy(b => b.PriorityWithinSlot)
            .ThenBy(b => b.CreatedAtUtc);

        // Materialise the page-window candidate set (worst case ≤ 200 rows
        // for the current TopK + capacity bounds; cheap to validate inline).
        var candidates = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Read-time CTA validation. Walk in parallel; fail-open on transient.
        var resolved = new List<(BannerSlot Row, CtaHealth Health)>(candidates.Count);
        await foreach (var pair in ResolveHealthsAsync(candidates, ct))
        {
            resolved.Add(pair);
        }

        // Filter out hard-broken banners; transient_unverified included.
        var visible = new List<(BannerSlot Row, CtaHealth Health)>(resolved.Count);
        foreach (var pair in resolved)
        {
            if (pair.Health == CtaHealth.Broken)
            {
                await EmitBrokenAsync(pair.Row, nowUtc, ct);
                continue;
            }
            visible.Add(pair);
        }

        // Total count uses the same filter (without paging + CTA filter — the
        // CTA filter only fires on the rendered page; under V1 sort the
        // first-page count is what customers see).
        var totalCount = visible.Count;

        var items = visible.Select(p => Project(p.Row, p.Health, query.Locale)).ToList();
        return new ListBannerSlotsResponse(items, page, pageSize, totalCount);
    }

    private async IAsyncEnumerable<(BannerSlot Row, CtaHealth Health)> ResolveHealthsAsync(
        IReadOnlyList<BannerSlot> rows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Validator is fail-open: never throws on transient errors.
        var tasks = rows.Select(async r => (Row: r, Health: await _ctaValidator.ResolveHealthAsync(r, ct))).ToArray();
        foreach (var t in tasks) yield return await t;
    }

    private async Task EmitBrokenAsync(BannerSlot row, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // Rate-limit to 1 alert per banner per hour. Reuse the row's
        // last_stale_alert_at_utc column as the timestamp boundary (V1 — no
        // dedicated column needed). Read-only path, so refresh via a
        // best-effort untracked update.
        if (row.LastStaleAlertAtUtc is DateTimeOffset prev && nowUtc - prev < CtaBrokenAlertWindow)
        {
            return;
        }

        try
        {
            await _db.BannerSlots
                .Where(b => b.Id == row.Id
                    && (b.LastStaleAlertAtUtc == null
                        || b.LastStaleAlertAtUtc < nowUtc - CtaBrokenAlertWindow))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.LastStaleAlertAtUtc, nowUtc), ct);
        }
        catch (Exception)
        {
            // Best-effort; rate-limit is observability, not correctness.
        }

        await _bus.Publish(new CmsBannerCtaTargetBroken(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode,
            CtaKindWire: row.CtaKindWire,
            CtaTarget: row.CtaTarget), ct);
    }

    private static BannerSlotResponseItem Project(BannerSlot row, CtaHealth health, string locale)
    {
        var (headline, subhead, assetId) = locale switch
        {
            "ar" => (row.HeadlineAr, row.SubheadAr, row.AssetIdAr),
            _ => (row.HeadlineEn, row.SubheadEn, row.AssetIdEn),
        };

        return new BannerSlotResponseItem(
            Id: row.Id,
            SlotKind: row.SlotKindWire,
            Headline: headline,
            Subhead: subhead,
            AssetId: assetId,
            CtaKind: row.CtaKindWire,
            CtaTarget: row.CtaTarget,
            CtaHealth: health.ToWire(),
            ScheduledStartUtc: row.ScheduledStartUtc,
            ScheduledEndUtc: row.ScheduledEndUtc,
            PriorityWithinSlot: row.PriorityWithinSlot,
            MarketCode: row.MarketCode);
    }
}
