using System.Text.Json;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Cms.Services;

/// <summary>
/// Live-resolves the polymorphic <c>references</c> jsonb of a featured section
/// against the cross-module catalog read contracts per spec 024 research §R4.
/// Refs are resolved in parallel via <see cref="Task.WhenAll(System.Collections.Generic.IEnumerable{Task})"/>;
/// transient errors propagate through the contracts as
/// <see cref="OperationCanceledException"/> / framework exceptions and are
/// caught here as "unavailable" (fail-open by way of filtering, since the
/// storefront would rather show fewer items than 5xx).
/// </summary>
public sealed class FeaturedSectionResolver
{
    private readonly ICatalogProductReadContract _products;
    private readonly ICatalogCategoryReadContract _categories;
    private readonly ICatalogBundleReadContract _bundles;

    public FeaturedSectionResolver(
        ICatalogProductReadContract products,
        ICatalogCategoryReadContract categories,
        ICatalogBundleReadContract bundles)
    {
        _products = products;
        _categories = categories;
        _bundles = bundles;
    }

    public async Task<FeaturedSectionResolution> ResolveAsync(
        FeaturedSection row,
        string locale,
        CancellationToken ct)
    {
        var refs = ParseReferences(row.ReferencesJson);
        if (refs.Count == 0)
        {
            return new FeaturedSectionResolution(
                Resolved: Array.Empty<ResolvedReference>(),
                TotalReferences: 0,
                TotalResolved: 0,
                TotalUnavailable: 0);
        }

        var tasks = new List<Task<ResolvedReference?>>(refs.Count);
        foreach (var (kind, id) in refs)
        {
            tasks.Add(ResolveOneAsync(kind, id, row.MarketCode, locale, ct));
        }
        var results = await Task.WhenAll(tasks);

        var resolved = new List<ResolvedReference>(results.Length);
        var unavailable = 0;
        foreach (var r in results)
        {
            if (r is null) { unavailable++; continue; }
            if (!r.IsAvailable) { unavailable++; continue; }
            resolved.Add(r);
        }

        return new FeaturedSectionResolution(
            Resolved: resolved,
            TotalReferences: refs.Count,
            TotalResolved: resolved.Count,
            TotalUnavailable: unavailable);
    }

    private async Task<ResolvedReference?> ResolveOneAsync(
        string kindWire, Guid targetId, string marketCode, string locale, CancellationToken ct)
    {
        try
        {
            switch (kindWire)
            {
                case "product":
                {
                    var p = await _products.ReadAsync(targetId, marketCode, ct);
                    return new ResolvedReference(
                        Kind: "product", Id: p.ProductId,
                        DisplayName: locale == "ar" ? p.DisplayNameAr : p.DisplayNameEn,
                        IsAvailable: p.IsAvailable,
                        UnavailableReasonWire: p.UnavailableReason?.ToString());
                }
                case "category":
                {
                    var c = await _categories.ReadAsync(targetId, marketCode, ct);
                    return new ResolvedReference(
                        Kind: "category", Id: c.CategoryId,
                        DisplayName: locale == "ar" ? c.DisplayNameAr : c.DisplayNameEn,
                        IsAvailable: c.IsAvailable,
                        UnavailableReasonWire: c.UnavailableReason?.ToString());
                }
                case "bundle":
                {
                    var b = await _bundles.ReadAsync(targetId, marketCode, ct);
                    return new ResolvedReference(
                        Kind: "bundle", Id: b.BundleId,
                        DisplayName: locale == "ar" ? b.DisplayNameAr : b.DisplayNameEn,
                        IsAvailable: b.IsAvailable,
                        UnavailableReasonWire: b.UnavailableReason?.ToString());
                }
                default:
                    return null;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null; // transient → treat as unavailable (filter out)
        }
    }

    /// <summary>
    /// Parses the polymorphic <c>references</c> jsonb (<c>[{"kind":"...","id":"..."}, ...]</c>)
    /// into a tolerant list. Malformed entries are skipped. Unsupported kinds
    /// are skipped (validated at save time per FR-006).
    /// </summary>
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
                if (kind is not ("product" or "category" or "bundle")) continue;
                var idStr = idEl.GetString();
                if (idStr is null || !Guid.TryParse(idStr, out var id)) continue;
                items.Add((kind, id));
            }
        }
        catch (JsonException)
        {
            // Bad jsonb → return empty; storefront responds with empty-resolved.
        }
        return items;
    }
}

public sealed record FeaturedSectionResolution(
    IReadOnlyList<ResolvedReference> Resolved,
    int TotalReferences,
    int TotalResolved,
    int TotalUnavailable)
{
    public bool OmittedDueToUnavailableReferences => TotalReferences > 0 && TotalResolved == 0;
}

public sealed record ResolvedReference(
    string Kind,
    Guid Id,
    string DisplayName,
    bool IsAvailable,
    string? UnavailableReasonWire);
