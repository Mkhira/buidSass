namespace BackendApi.Modules.Cms.Primitives;

/// <summary>FAQ categories — 8 fixed values per FR-006.</summary>
public enum FaqCategory
{
    Ordering = 0,
    Payment = 1,
    Shipping = 2,
    Returns = 3,
    Account = 4,
    Verification = 5,
    B2b = 6,
    Other = 7,
}

public static class FaqCategoryWire
{
    public static string ToWire(this FaqCategory cat) => cat switch
    {
        FaqCategory.Ordering => "ordering",
        FaqCategory.Payment => "payment",
        FaqCategory.Shipping => "shipping",
        FaqCategory.Returns => "returns",
        FaqCategory.Account => "account",
        FaqCategory.Verification => "verification",
        FaqCategory.B2b => "b2b",
        FaqCategory.Other => "other",
        _ => throw new InvalidOperationException($"Unknown FaqCategory: {cat}"),
    };

    public static FaqCategory FromWire(string s) => s switch
    {
        "ordering" => FaqCategory.Ordering,
        "payment" => FaqCategory.Payment,
        "shipping" => FaqCategory.Shipping,
        "returns" => FaqCategory.Returns,
        "account" => FaqCategory.Account,
        "verification" => FaqCategory.Verification,
        "b2b" => FaqCategory.B2b,
        "other" => FaqCategory.Other,
        _ => throw new InvalidOperationException($"Unknown FaqCategory wire value: '{s}'."),
    };

    public static string IcuKey(this FaqCategory cat) => $"cms.faq_category.{cat.ToWire()}";
}
