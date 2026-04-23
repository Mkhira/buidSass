namespace BackendApi.Modules.Identity.Admin.Me;

public sealed record AdminMeRequest;

public sealed record AdminMeResponse(
    Guid Id,
    string Email,
    string Locale,
    string MarketCode,
    IReadOnlyCollection<string> Permissions);
