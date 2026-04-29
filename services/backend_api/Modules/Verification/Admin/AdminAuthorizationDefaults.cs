namespace BackendApi.Modules.Verification.Admin;

/// <summary>
/// Verification admin slice authorization wiring. The scheme name is set by
/// <c>Modules/Identity/IdentityModule.cs</c> via <c>AddJwtBearer("AdminJwt", ...)</c>.
/// Centralizing the constant here means one change-site if Identity ever
/// renames the scheme.
/// </summary>
public static class AdminAuthorizationDefaults
{
    public const string AuthenticationScheme = "AdminJwt";
}
