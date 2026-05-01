namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Banner CTA kinds per FR-006.</summary>
public enum CtaKind
{
    Link = 0,
    Category = 1,
    Product = 2,
    Bundle = 3,
    ExternalUrl = 4,
    None = 5,
}

public static class CtaKindWire
{
    public static string ToWire(this CtaKind kind) => kind switch
    {
        CtaKind.Link => "link",
        CtaKind.Category => "category",
        CtaKind.Product => "product",
        CtaKind.Bundle => "bundle",
        CtaKind.ExternalUrl => "external_url",
        CtaKind.None => "none",
        _ => throw new InvalidOperationException($"Unknown CtaKind: {kind}"),
    };

    public static CtaKind FromWire(string s) => s switch
    {
        "link" => CtaKind.Link,
        "category" => CtaKind.Category,
        "product" => CtaKind.Product,
        "bundle" => CtaKind.Bundle,
        "external_url" => CtaKind.ExternalUrl,
        "none" => CtaKind.None,
        _ => throw new InvalidOperationException($"Unknown CtaKind wire value: '{s}'."),
    };
}
