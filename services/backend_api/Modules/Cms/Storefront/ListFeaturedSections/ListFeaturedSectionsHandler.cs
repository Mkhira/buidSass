using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Services;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Storefront.ListFeaturedSections;

/// <summary>
/// GET /v1/storefront/cms/featured-sections per spec 024 contract §7.2.
/// Composes <see cref="StorefrontContentResolver"/> + <see cref="FeaturedSectionResolver"/>;
/// emits <c>cms.featured_section.partial_broken</c> (when 0 &lt; unavailable
/// &lt; total) or <c>cms.featured_section.fully_broken</c> (when total_resolved
/// == 0) rate-limited 1/section/hour via the row's
/// <c>last_partial_broken_alert_at_utc</c> column.
/// </summary>
public sealed class ListFeaturedSectionsHandler
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;
    private static readonly TimeSpan BrokenAlertWindow = TimeSpan.FromHours(1);

    private readonly CmsDbContext _db;
    private readonly StorefrontContentResolver _resolver;
    private readonly FeaturedSectionResolver _refResolver;
    private readonly IPublisher _bus;
    private readonly TimeProvider _clock;

    public ListFeaturedSectionsHandler(
        CmsDbContext db,
        StorefrontContentResolver resolver,
        FeaturedSectionResolver refResolver,
        IPublisher bus,
        TimeProvider clock)
    {
        _db = db;
        _resolver = resolver;
        _refResolver = refResolver;
        _bus = bus;
        _clock = clock;
    }

    public async Task<ListFeaturedSectionsResponse> HandleAsync(
        ListFeaturedSectionsQuery query, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 1);

        var src = _db.FeaturedSections.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.SectionKind))
        {
            src = src.Where(s => s.SectionKindWire == query.SectionKind);
        }

        var safe = _resolver.ApplyStorefrontFilter(src, query.Market, nowUtc);
        var ordered = safe
            .ThenBy(s => s.DisplayPriority)
            .ThenBy(s => s.CreatedAtUtc);

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Live-resolve refs in parallel per row (each row already does
        // Task.WhenAll over its own refs).
        var resolveTasks = rows.Select(r => _refResolver.ResolveAsync(r, query.Locale, ct)).ToArray();
        var resolutions = await Task.WhenAll(resolveTasks);

        var items = new List<FeaturedSectionResponseItem>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var res = resolutions[i];

            // Emit operational events (rate-limited 1/section/hour).
            if (res.TotalReferences > 0)
            {
                if (res.TotalResolved == 0)
                {
                    await EmitFullyBrokenIfDueAsync(row, res, nowUtc, ct);
                }
                else if (res.TotalUnavailable > 0)
                {
                    await EmitPartialBrokenIfDueAsync(row, res, nowUtc, ct);
                }
            }

            var (title, subtitle) = query.Locale switch
            {
                "ar" => (row.TitleAr, row.SubtitleAr),
                _ => (row.TitleEn, row.SubtitleEn),
            };

            items.Add(new FeaturedSectionResponseItem(
                SectionId: row.Id,
                SectionKind: row.SectionKindWire,
                Title: title,
                Subtitle: subtitle,
                DisplayPriority: row.DisplayPriority,
                MarketCode: row.MarketCode,
                ReferencesResolved: res.Resolved,
                TotalReferences: res.TotalReferences,
                TotalResolved: res.TotalResolved,
                TotalUnavailable: res.TotalUnavailable,
                OmittedDueToUnavailableReferences: res.OmittedDueToUnavailableReferences));
        }

        return new ListFeaturedSectionsResponse(items, page, pageSize, rows.Count);
    }

    private async Task EmitPartialBrokenIfDueAsync(
        FeaturedSection row, FeaturedSectionResolution res, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (row.LastPartialBrokenAlertAtUtc is DateTimeOffset prev && nowUtc - prev < BrokenAlertWindow)
        {
            return;
        }
        try
        {
            await _db.FeaturedSections
                .Where(s => s.Id == row.Id
                    && (s.LastPartialBrokenAlertAtUtc == null
                        || s.LastPartialBrokenAlertAtUtc < nowUtc - BrokenAlertWindow))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastPartialBrokenAlertAtUtc, nowUtc), ct);
        }
        catch
        {
            // Best-effort.
        }

        await _bus.Publish(new CmsFeaturedSectionPartialBroken(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode,
            TotalReferences: res.TotalReferences,
            TotalUnavailable: res.TotalUnavailable), ct);
    }

    private async Task EmitFullyBrokenIfDueAsync(
        FeaturedSection row, FeaturedSectionResolution res, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (row.LastPartialBrokenAlertAtUtc is DateTimeOffset prev && nowUtc - prev < BrokenAlertWindow)
        {
            return;
        }
        try
        {
            await _db.FeaturedSections
                .Where(s => s.Id == row.Id
                    && (s.LastPartialBrokenAlertAtUtc == null
                        || s.LastPartialBrokenAlertAtUtc < nowUtc - BrokenAlertWindow))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastPartialBrokenAlertAtUtc, nowUtc), ct);
        }
        catch
        {
            // Best-effort.
        }

        await _bus.Publish(new CmsFeaturedSectionFullyBroken(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: nowUtc,
            EntityId: row.Id,
            VersionId: row.Id,
            MarketCode: row.MarketCode,
            TotalReferences: res.TotalReferences), ct);
    }
}
