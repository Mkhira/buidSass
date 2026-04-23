namespace BackendApi.Modules.Search.Primitives;

public sealed record SearchIndexConfig(string Name, string MarketCode, string Locale);

public static class IndexNames
{
    public static readonly SearchIndexConfig ProductsArKsa = new("products_ar_ksa", "ksa", "ar");
    public static readonly SearchIndexConfig ProductsArEg = new("products_ar_eg", "eg", "ar");
    public static readonly SearchIndexConfig ProductsEnKsa = new("products_en_ksa", "ksa", "en");
    public static readonly SearchIndexConfig ProductsEnEg = new("products_en_eg", "eg", "en");

    public static IReadOnlyList<SearchIndexConfig> All { get; } =
    [
        ProductsArKsa,
        ProductsArEg,
        ProductsEnKsa,
        ProductsEnEg,
    ];

    public static bool TryResolve(string marketCode, string locale, out SearchIndexConfig index)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        var normalizedLocale = locale.Trim().ToLowerInvariant();

        index = All.FirstOrDefault(i => i.MarketCode == normalizedMarket && i.Locale == normalizedLocale)
            ?? new SearchIndexConfig(string.Empty, normalizedMarket, normalizedLocale);

        return !string.IsNullOrWhiteSpace(index.Name);
    }

    public static bool TryParseIndex(string indexName, out SearchIndexConfig index)
    {
        var normalized = indexName.Trim().ToLowerInvariant();

        // Accept legacy dash naming for migration convenience.
        normalized = normalized switch
        {
            "products-ksa-ar" => "products_ar_ksa",
            "products-eg-ar" => "products_ar_eg",
            "products-ksa-en" => "products_en_ksa",
            "products-eg-en" => "products_en_eg",
            _ => normalized,
        };

        index = All.FirstOrDefault(i => i.Name == normalized)
            ?? new SearchIndexConfig(string.Empty, string.Empty, string.Empty);

        return !string.IsNullOrWhiteSpace(index.Name);
    }
}
