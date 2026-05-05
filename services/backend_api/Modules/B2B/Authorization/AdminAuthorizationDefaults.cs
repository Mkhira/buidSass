namespace BackendApi.Modules.B2B.Authorization;

/// <summary>
/// Spec 021 admin slice authorization wiring. The scheme name is set by
/// <c>Modules/Identity/IdentityModule.cs</c> via <c>AddJwtBearer("AdminJwt", ...)</c>.
/// Mirror of <c>Modules.Verification.Admin.AdminAuthorizationDefaults</c> kept
/// per-module so spec 021 doesn't take a runtime dep on spec 020.
/// </summary>
public static class AdminAuthorizationDefaults
{
    public const string AuthenticationScheme = "AdminJwt";
}
