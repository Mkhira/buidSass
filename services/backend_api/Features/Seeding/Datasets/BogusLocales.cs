namespace BackendApi.Features.Seeding.Datasets;

/// <summary>
/// Central list of Bogus locales used across seeders. Phrase banks for editorial-grade
/// Arabic (Principle 4) are sourced from curated JSON — Bogus ar_* is only used
/// for non-user-visible fields (e.g., internal codes, addresses in tests).
/// </summary>
public static class BogusLocales
{
    public const string English = "en_US";
    public const string ArabicFallback = "ar";

    public static string ForMarket(string marketCode) => marketCode switch
    {
        "ksa" => "ar",
        "eg" => "ar",
        _ => English
    };
}
