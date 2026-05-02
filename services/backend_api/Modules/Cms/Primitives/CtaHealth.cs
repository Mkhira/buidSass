namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Banner CTA validation health per FR-022a.</summary>
public enum CtaHealth
{
    Verified = 0,
    Broken = 1,
    TransientUnverified = 2,
    NotApplicable = 3,
}

public static class CtaHealthWire
{
    public static string ToWire(this CtaHealth h) => h switch
    {
        CtaHealth.Verified => "verified",
        CtaHealth.Broken => "broken",
        CtaHealth.TransientUnverified => "transient_unverified",
        CtaHealth.NotApplicable => "not_applicable",
        _ => throw new InvalidOperationException($"Unknown CtaHealth: {h}"),
    };

    public static CtaHealth FromWire(string s) => s switch
    {
        "verified" => CtaHealth.Verified,
        "broken" => CtaHealth.Broken,
        "transient_unverified" => CtaHealth.TransientUnverified,
        "not_applicable" => CtaHealth.NotApplicable,
        _ => throw new InvalidOperationException($"Unknown CtaHealth wire value: '{s}'."),
    };
}
