namespace BackendApi.Modules.Identity.Admin.ListAdminMfaFactors;

public sealed record ListAdminMfaFactorsRequest(Guid AccountId);

public sealed record ListAdminMfaFactorsResponse(IReadOnlyCollection<AdminMfaFactorItem> Factors);

public sealed record AdminMfaFactorItem(
    Guid Id,
    string Kind,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);
