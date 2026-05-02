namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Featured section kinds per FR-006.</summary>
public enum FeaturedSectionKind
{
    HomeTop = 0,
    HomeMid = 1,
    CategoryLanding = 2,
    B2bLanding = 3,
}

public static class FeaturedSectionKindWire
{
    public static string ToWire(this FeaturedSectionKind kind) => kind switch
    {
        FeaturedSectionKind.HomeTop => "home_top",
        FeaturedSectionKind.HomeMid => "home_mid",
        FeaturedSectionKind.CategoryLanding => "category_landing",
        FeaturedSectionKind.B2bLanding => "b2b_landing",
        _ => throw new InvalidOperationException($"Unknown FeaturedSectionKind: {kind}"),
    };

    public static FeaturedSectionKind FromWire(string s) => s switch
    {
        "home_top" => FeaturedSectionKind.HomeTop,
        "home_mid" => FeaturedSectionKind.HomeMid,
        "category_landing" => FeaturedSectionKind.CategoryLanding,
        "b2b_landing" => FeaturedSectionKind.B2bLanding,
        _ => throw new InvalidOperationException($"Unknown FeaturedSectionKind wire value: '{s}'."),
    };
}
