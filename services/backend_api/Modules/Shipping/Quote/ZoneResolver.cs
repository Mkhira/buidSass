using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Quote;

/// <summary>
/// Address → <see cref="ShippingZone"/> resolution per research §1:
/// city-list match first; postal-code-prefix is the tie-breaker for cities
/// with multiple postal regions; returns null if neither matches (AC-5).
/// </summary>
public sealed class ZoneResolver(ShippingDbContext db)
{
    public async Task<ShippingZone?> ResolveAsync(
        string marketCode,
        string? city,
        string? postalCode,
        CancellationToken ct)
    {
        if (!ShippingConstants.Markets.IsValid(marketCode))
        {
            return null;
        }

        var zones = await db.ShippingZones
            .Where(z => z.MarketCode == marketCode && z.DeletedAt == null)
            .ToListAsync(ct);
        if (zones.Count == 0)
        {
            return null;
        }

        // Primary match: city-list (case-insensitive against the JSON array).
        if (!string.IsNullOrWhiteSpace(city))
        {
            var citySlug = city.Trim().ToLowerInvariant();
            foreach (var z in zones)
            {
                if (CityListContains(z.CityListJson, citySlug))
                {
                    return z;
                }
            }
        }

        // Tie-breaker: postal-code prefix match (longest-prefix wins).
        if (!string.IsNullOrWhiteSpace(postalCode))
        {
            ShippingZone? bestMatch = null;
            int bestPrefixLen = -1;
            foreach (var z in zones)
            {
                foreach (var prefix in EnumeratePrefixes(z.PostalCodePrefixesJson))
                {
                    if (postalCode.StartsWith(prefix, StringComparison.Ordinal) && prefix.Length > bestPrefixLen)
                    {
                        bestMatch = z;
                        bestPrefixLen = prefix.Length;
                    }
                }
            }
            return bestMatch;
        }

        return null;
    }

    private static bool CityListContains(string json, string slug)
    {
        // The JSON is a string array; keep parsing dependency-free.
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.String &&
                    string.Equals(el.GetString()?.ToLowerInvariant(), slug, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed config — defer to postal-code path.
        }
        return false;
    }

    private static IEnumerable<string> EnumeratePrefixes(string json)
    {
        IEnumerable<string> Empty() => Array.Empty<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return Empty();
            }
            var prefixes = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s)) prefixes.Add(s);
                }
            }
            return prefixes;
        }
        catch (System.Text.Json.JsonException)
        {
            return Empty();
        }
    }
}
