namespace BackendApi.Modules.Identity.Authorization.Filters;

public sealed record RequirePermissionMetadata(
    string PermissionCode,
    string? RequiredMarketCode = null);

public sealed record RequireStepUpMetadata(string? PermissionCode = null);
