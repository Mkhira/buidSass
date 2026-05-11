namespace BackendApi.Modules.Pricing.Admin.PreviewProfiles;

/// <summary>
/// Spec 007-b PreviewProfile DTOs (contract §6). The cart-line entries are
/// JSON-encoded to keep the payload aligned with the persisted column shape
/// (see <see cref="BackendApi.Modules.Pricing.Entities.PreviewProfile.CartLinesJson"/>).
/// </summary>
public sealed record PreviewCartLine(string Sku, int Qty, bool Restricted);

public sealed record UpsertPreviewProfileRequest(
    Guid? Id,
    string Name,
    string MarketCode,
    string Locale,
    string AccountKind,
    Guid? TierId,
    string VerificationState,
    IReadOnlyList<PreviewCartLine> CartLines,
    string? Visibility);

public sealed record PromoteToSharedRequest(string ReasonNote);

public sealed record PreviewProfileResponse(
    Guid Id,
    string Name,
    string MarketCode,
    string Locale,
    string AccountKind,
    Guid? TierId,
    string VerificationState,
    IReadOnlyList<PreviewCartLine> CartLines,
    string Visibility,
    Guid CreatedBy,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PreviewProfileListResponse(IReadOnlyList<PreviewProfileResponse> Items);
