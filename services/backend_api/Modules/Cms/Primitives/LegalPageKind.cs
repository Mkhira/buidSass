namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Legal page kinds per FR-006.</summary>
public enum LegalPageKind
{
    Terms = 0,
    Privacy = 1,
    Returns = 2,
    Cookies = 3,
}

public static class LegalPageKindWire
{
    public static string ToWire(this LegalPageKind kind) => kind switch
    {
        LegalPageKind.Terms => "terms",
        LegalPageKind.Privacy => "privacy",
        LegalPageKind.Returns => "returns",
        LegalPageKind.Cookies => "cookies",
        _ => throw new InvalidOperationException($"Unknown LegalPageKind: {kind}"),
    };

    public static LegalPageKind FromWire(string s) => s switch
    {
        "terms" => LegalPageKind.Terms,
        "privacy" => LegalPageKind.Privacy,
        "returns" => LegalPageKind.Returns,
        "cookies" => LegalPageKind.Cookies,
        _ => throw new InvalidOperationException($"Unknown LegalPageKind wire value: '{s}'."),
    };

    public static string IcuKey(this LegalPageKind kind) => $"cms.legal_page_kind.{kind.ToWire()}";
}
