namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityMfaOptions
{
    public const string SectionName = "Identity:Mfa";

    public string[] RequiredRoles { get; set; } =
    [
        "platform.super_admin",
        "platform.finance_viewer",
    ];
}
