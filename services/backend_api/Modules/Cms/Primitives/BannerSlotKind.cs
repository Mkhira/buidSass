namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Banner slot kinds per FR-006.</summary>
public enum BannerSlotKind
{
    HeroTop = 0,
    CategoryStrip = 1,
    FooterStrip = 2,
    HomeSecondary = 3,
}

public static class BannerSlotKindWire
{
    public static string ToWire(this BannerSlotKind kind) => kind switch
    {
        BannerSlotKind.HeroTop => "hero_top",
        BannerSlotKind.CategoryStrip => "category_strip",
        BannerSlotKind.FooterStrip => "footer_strip",
        BannerSlotKind.HomeSecondary => "home_secondary",
        _ => throw new InvalidOperationException($"Unknown BannerSlotKind: {kind}"),
    };

    public static BannerSlotKind FromWire(string s) => s switch
    {
        "hero_top" => BannerSlotKind.HeroTop,
        "category_strip" => BannerSlotKind.CategoryStrip,
        "footer_strip" => BannerSlotKind.FooterStrip,
        "home_secondary" => BannerSlotKind.HomeSecondary,
        _ => throw new InvalidOperationException($"Unknown BannerSlotKind wire value: '{s}'."),
    };
}
