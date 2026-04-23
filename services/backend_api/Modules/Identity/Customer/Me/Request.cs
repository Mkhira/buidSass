namespace BackendApi.Modules.Identity.Customer.Me;

public sealed record CustomerMeRequest;

public sealed record CustomerMeResponse(
    Guid Id,
    string Email,
    string? PhoneE164,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? PhoneVerifiedAt,
    string Locale,
    IReadOnlyCollection<string> Roles);
