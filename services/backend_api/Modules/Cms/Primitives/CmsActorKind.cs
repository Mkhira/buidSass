namespace BackendApi.Modules.Cms.Primitives;

/// <summary>Actor kinds in the CMS state-machine. Per spec 024 contract §1.</summary>
public enum CmsActorKind
{
    Editor = 0,
    Publisher = 1,
    LegalOwner = 2,
    SuperAdmin = 3,
    FinanceViewer = 4,
    B2bAccountManager = 5,
    System = 6,
}

public static class CmsActorKindWire
{
    public static string ToWire(this CmsActorKind a) => a switch
    {
        CmsActorKind.Editor => "editor",
        CmsActorKind.Publisher => "publisher",
        CmsActorKind.LegalOwner => "legal_owner",
        CmsActorKind.SuperAdmin => "super_admin",
        CmsActorKind.FinanceViewer => "finance_viewer",
        CmsActorKind.B2bAccountManager => "b2b_account_manager",
        CmsActorKind.System => "system",
        _ => throw new InvalidOperationException($"Unknown CmsActorKind: {a}"),
    };

    public static CmsActorKind FromWire(string s) => s switch
    {
        "editor" => CmsActorKind.Editor,
        "publisher" => CmsActorKind.Publisher,
        "legal_owner" => CmsActorKind.LegalOwner,
        "super_admin" => CmsActorKind.SuperAdmin,
        "finance_viewer" => CmsActorKind.FinanceViewer,
        "b2b_account_manager" => CmsActorKind.B2bAccountManager,
        "system" => CmsActorKind.System,
        _ => throw new InvalidOperationException($"Unknown CmsActorKind wire value: '{s}'."),
    };
}
