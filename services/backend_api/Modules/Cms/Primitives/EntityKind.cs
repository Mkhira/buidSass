namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// The 5 CMS entity kinds. Per spec 024 data-model §2.
/// </summary>
public enum EntityKind
{
    BannerSlot = 0,
    FeaturedSection = 1,
    FaqEntry = 2,
    BlogArticle = 3,
    LegalPageVersion = 4,
}

public static class EntityKindWire
{
    public static string ToWire(this EntityKind kind) => kind switch
    {
        EntityKind.BannerSlot => "banner_slot",
        EntityKind.FeaturedSection => "featured_section",
        EntityKind.FaqEntry => "faq_entry",
        EntityKind.BlogArticle => "blog_article",
        EntityKind.LegalPageVersion => "legal_page_version",
        _ => throw new InvalidOperationException($"Unknown EntityKind: {kind}"),
    };

    public static EntityKind FromWire(string s) => s switch
    {
        "banner_slot" => EntityKind.BannerSlot,
        "featured_section" => EntityKind.FeaturedSection,
        "faq_entry" => EntityKind.FaqEntry,
        "blog_article" => EntityKind.BlogArticle,
        "legal_page_version" => EntityKind.LegalPageVersion,
        _ => throw new InvalidOperationException($"Unknown EntityKind wire value: '{s}'."),
    };

    /// <summary>ICU-key mapper for friendly bilingual names.</summary>
    public static string IcuKey(this EntityKind kind) => $"cms.entity_kind.{kind.ToWire()}";
}
