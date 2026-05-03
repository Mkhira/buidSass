using System.Text.Json;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.GetFeaturedSectionAdminDetail;

/// <summary>
/// Admin-side featured-section detail handler per spec 024 task T121. Resolves
/// the polymorphic <c>references</c> jsonb against the cross-module catalog
/// read contracts so the authoring UI can render per-reference availability
/// badges and surface broken-reference counts. Distinct from
/// <see cref="BackendApi.Modules.Cms.Services.FeaturedSectionResolver"/> which
/// powers the storefront read path: this admin variant returns BOTH locales,
/// returns broken references in-line (storefront filters them out), and
/// preserves the row's xmin for the UI's optimistic-concurrency PATCH path.
/// </summary>
public sealed class GetFeaturedSectionAdminDetailHandler
{
    private readonly CmsDbContext _db;
    private readonly ICatalogProductReadContract _products;
    private readonly ICatalogCategoryReadContract _categories;
    private readonly ICatalogBundleReadContract _bundles;

    public GetFeaturedSectionAdminDetailHandler(
        CmsDbContext db,
        ICatalogProductReadContract products,
        ICatalogCategoryReadContract categories,
        ICatalogBundleReadContract bundles)
    {
        _db = db;
        _products = products;
        _categories = categories;
        _bundles = bundles;
    }

    public async Task<FeaturedSectionAdminDetailResponse?> HandleAsync(
        GetFeaturedSectionAdminDetailQuery query, CancellationToken ct)
    {
        var row = await _db.FeaturedSections
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.Id, ct);
        if (row is null) return null;

        var refs = ParseReferences(row.ReferencesJson);
        var resolved = new List<FeaturedSectionAdminReferenceItem>(refs.Count);
        var available = 0;
        var broken = 0;

        // Resolve refs in parallel — same pattern as the storefront resolver,
        // but admin keeps broken refs in the response.
        var tasks = refs.Select(r => ResolveOneAsync(r.Kind, r.Id, row.MarketCode, ct)).ToArray();
        var resolvedItems = await Task.WhenAll(tasks);
        foreach (var item in resolvedItems)
        {
            if (item is null)
            {
                broken++;
                continue;
            }
            if (item.IsAvailable) available++;
            else broken++;
            resolved.Add(item);
        }

        return new FeaturedSectionAdminDetailResponse(
            Id: row.Id,
            SectionKind: row.SectionKindWire,
            TitleAr: row.TitleAr,
            TitleEn: row.TitleEn,
            SubtitleAr: row.SubtitleAr,
            SubtitleEn: row.SubtitleEn,
            DisplayPriority: row.DisplayPriority,
            MarketCode: row.MarketCode,
            State: row.StateWire,
            ScheduledPublishAtUtc: row.ScheduledPublishAtUtc,
            CreatedAtUtc: row.CreatedAtUtc,
            EditorSaveAtUtc: row.EditorSaveAtUtc,
            PublishedAtUtc: row.PublishedAtUtc,
            ArchivedAtUtc: row.ArchivedAtUtc,
            OwnerActorId: row.OwnerActorId,
            OwnershipOrphaned: row.OwnershipOrphaned,
            TotalReferences: refs.Count,
            TotalAvailable: available,
            TotalBroken: broken,
            References: resolved,
            Xmin: row.Xmin);
    }

    private async Task<FeaturedSectionAdminReferenceItem?> ResolveOneAsync(
        string kind, Guid id, string marketCode, CancellationToken ct)
    {
        try
        {
            switch (kind)
            {
                case "product":
                    {
                        var p = await _products.ReadAsync(id, marketCode, ct);
                        return new FeaturedSectionAdminReferenceItem(
                            Kind: "product",
                            Id: p.ProductId,
                            DisplayNameEn: p.DisplayNameEn,
                            DisplayNameAr: p.DisplayNameAr,
                            IsAvailable: p.IsAvailable,
                            UnavailableReasonWire: p.UnavailableReason?.ToString());
                    }
                case "category":
                    {
                        var c = await _categories.ReadAsync(id, marketCode, ct);
                        return new FeaturedSectionAdminReferenceItem(
                            Kind: "category",
                            Id: c.CategoryId,
                            DisplayNameEn: c.DisplayNameEn,
                            DisplayNameAr: c.DisplayNameAr,
                            IsAvailable: c.IsAvailable,
                            UnavailableReasonWire: c.UnavailableReason?.ToString());
                    }
                case "bundle":
                    {
                        var b = await _bundles.ReadAsync(id, marketCode, ct);
                        return new FeaturedSectionAdminReferenceItem(
                            Kind: "bundle",
                            Id: b.BundleId,
                            DisplayNameEn: b.DisplayNameEn,
                            DisplayNameAr: b.DisplayNameAr,
                            IsAvailable: b.IsAvailable,
                            UnavailableReasonWire: b.UnavailableReason?.ToString());
                    }
                default:
                    // Save-time validation rejects unsupported kinds; if one
                    // sneaks in via a manual DB edit, surface it as broken.
                    return new FeaturedSectionAdminReferenceItem(
                        Kind: kind,
                        Id: id,
                        DisplayNameEn: null,
                        DisplayNameAr: null,
                        IsAvailable: false,
                        UnavailableReasonWire: "UnsupportedKind");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Transient → broken badge. Same fail-open semantics as the
            // storefront resolver (better than 5xx-ing the editor UI).
            return null;
        }
    }

    private static IReadOnlyList<(string Kind, Guid Id)> ParseReferences(string referencesJson)
    {
        if (string.IsNullOrWhiteSpace(referencesJson)) return Array.Empty<(string, Guid)>();
        var items = new List<(string, Guid)>();
        try
        {
            using var doc = JsonDocument.Parse(referencesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return items;
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (!elem.TryGetProperty("kind", out var kindEl)) continue;
                if (!elem.TryGetProperty("id", out var idEl)) continue;
                var kind = kindEl.GetString();
                if (kind is null) continue;
                var idStr = idEl.GetString();
                if (idStr is null || !Guid.TryParse(idStr, out var id)) continue;
                items.Add((kind, id));
            }
        }
        catch (JsonException)
        {
            // Bad jsonb — return what we parsed; admin sees TotalReferences = 0.
        }
        return items;
    }
}
