using BackendApi.Modules.Search.Primitives;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BackendApi.Modules.Search.Synonyms;

public sealed class SynonymsSeeder
{
    private static readonly IReadOnlyList<string> SearchableAttributes =
    [
        "name",
        "nameNormalized",
        "sku",
        "barcode",
        "brandName",
        "categoryBreadcrumb",
        "shortDescription",
    ];

    private static readonly IReadOnlyList<string> FilterableAttributes =
    [
        "brandId",
        "categoryIds",
        "sku",
        "barcode",
        "priceHintMinorUnits",
        "restricted",
        "availability",
    ];

    private static readonly IReadOnlyList<string> SortableAttributes =
    [
        "priceHintMinorUnits",
        "publishedAt",
        "featuredAt",
    ];

    public async Task SeedAsync(
        ISearchEngine searchEngine,
        IReadOnlyCollection<SearchIndexConfig> indexes,
        CancellationToken cancellationToken)
    {
        var synonyms = await LoadSynonymsAsync(cancellationToken);
        var stopwords = await LoadStopwordsAsync(cancellationToken);

        foreach (var index in indexes)
        {
            await searchEngine.ApplySettingsAsync(
                index.Name,
                SearchableAttributes,
                FilterableAttributes,
                SortableAttributes,
                stopwords[index.Locale],
                synonyms[index.Locale],
                cancellationToken);
        }
    }

    private static async Task<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>>> LoadSynonymsAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var locale in new[] { "ar", "en" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Modules", "Search", "Synonyms", $"synonyms.{locale}.yaml");
            if (!File.Exists(path))
            {
                result[locale] = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var yaml = await File.ReadAllTextAsync(path, cancellationToken);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var groups = deserializer.Deserialize<List<List<string>>>(yaml) ?? [];
            var map = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups.Where(g => g.Count > 1))
            {
                var normalized = group
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                    .Select(term => term.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var term in normalized)
                {
                    map[term] = normalized;
                }
            }

            result[locale] = map;
        }

        return result;
    }

    private static async Task<Dictionary<string, IReadOnlyCollection<string>>> LoadStopwordsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var locale in new[] { "ar", "en" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Modules", "Search", "Synonyms", $"stopwords.{locale}.txt");
            if (!File.Exists(path))
            {
                result[locale] = [];
                continue;
            }

            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            result[locale] = lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return result;
    }
}
