namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Blog categories — 6 fixed values per FR-006.</summary>
public enum BlogCategory
{
    Tips = 0,
    News = 1,
    Guides = 2,
    CaseStudies = 3,
    Clinical = 4,
    Other = 5,
}

public static class BlogCategoryWire
{
    public static string ToWire(this BlogCategory cat) => cat switch
    {
        BlogCategory.Tips => "tips",
        BlogCategory.News => "news",
        BlogCategory.Guides => "guides",
        BlogCategory.CaseStudies => "case_studies",
        BlogCategory.Clinical => "clinical",
        BlogCategory.Other => "other",
        _ => throw new InvalidOperationException($"Unknown BlogCategory: {cat}"),
    };

    public static BlogCategory FromWire(string s) => s switch
    {
        "tips" => BlogCategory.Tips,
        "news" => BlogCategory.News,
        "guides" => BlogCategory.Guides,
        "case_studies" => BlogCategory.CaseStudies,
        "clinical" => BlogCategory.Clinical,
        "other" => BlogCategory.Other,
        _ => throw new InvalidOperationException($"Unknown BlogCategory wire value: '{s}'."),
    };

    public static string IcuKey(this BlogCategory cat) => $"cms.blog_category.{cat.ToWire()}";
}
