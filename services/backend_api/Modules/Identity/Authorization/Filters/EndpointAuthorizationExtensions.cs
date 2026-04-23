namespace BackendApi.Modules.Identity.Authorization.Filters;

public static class EndpointAuthorizationExtensions
{
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        string permissionCode,
        string? requiredMarketCode = null)
    {
        builder.WithMetadata(new RequirePermissionMetadata(permissionCode, requiredMarketCode));
        builder.AddEndpointFilter<RequirePermissionEndpointFilter>();
        return builder;
    }

    public static RouteHandlerBuilder RequireStepUp(this RouteHandlerBuilder builder, string? permissionCode = null)
    {
        builder.WithMetadata(new RequireStepUpMetadata(permissionCode));
        builder.AddEndpointFilter<RequireStepUpEndpointFilter>();
        return builder;
    }
}
