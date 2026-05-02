namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Featured-section reference kinds per FR-006.</summary>
public enum ReferenceKind
{
    Product = 0,
    Category = 1,
    Bundle = 2,
}

public static class ReferenceKindWire
{
    public static string ToWire(this ReferenceKind kind) => kind switch
    {
        ReferenceKind.Product => "product",
        ReferenceKind.Category => "category",
        ReferenceKind.Bundle => "bundle",
        _ => throw new InvalidOperationException($"Unknown ReferenceKind: {kind}"),
    };

    public static ReferenceKind FromWire(string s) => s switch
    {
        "product" => ReferenceKind.Product,
        "category" => ReferenceKind.Category,
        "bundle" => ReferenceKind.Bundle,
        _ => throw new InvalidOperationException($"Unknown ReferenceKind wire value: '{s}'."),
    };
}
