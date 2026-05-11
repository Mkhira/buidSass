namespace BackendApi.Modules.Pricing.Admin.BusinessPricing;

/// <summary>
/// Spec 007-b business-pricing admin DTOs (contract §4). Wire shape only;
/// persistence shape lives on <see cref="BackendApi.Modules.Pricing.Entities.ProductTierPrice"/>.
/// </summary>
/// <remarks>
/// The <c>sku</c> field on the wire is the catalog product identifier; in this
/// codebase pricing rows reference the product by its <c>product_id</c> GUID.
/// The wire shape exposes both — clients may pass <c>product_id</c> directly
/// when they already hold the GUID, or <c>sku</c> + a future catalog lookup
/// once spec 015 admin pickers are wired through. For US3 we accept and echo
/// <c>product_id</c> as the canonical reference.
/// </remarks>
public sealed record UpsertTierRowRequest(
    Guid TierId,
    Guid ProductId,
    string MarketCode,
    long NetMinor,
    bool AcknowledgeBelowCogs = false);

public sealed record UpsertCompanyOverrideRequest(
    Guid CompanyId,
    Guid ProductId,
    string MarketCode,
    long NetMinor,
    Guid? CopiedFromTierId,
    bool AcknowledgeBelowCogs = false);

public sealed record DeactivateOrReactivateBusinessPricingRequest(string ReasonNote);

public sealed record BusinessPricingRowResponse(
    Guid Id,
    Guid? TierId,
    Guid? CompanyId,
    Guid? CopiedFromTierId,
    Guid ProductId,
    string MarketCode,
    long NetMinor,
    string State,
    bool CompanyLinkBroken,
    DateTimeOffset? CompanyLinkBrokenAtUtc,
    Guid? VendorId,
    uint RowVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StateChangedAtUtc);

public sealed record BusinessPricingListResponse(
    IReadOnlyList<BusinessPricingRowResponse> Items,
    string? NextCursor);

// ---- Bulk import ----

public sealed record BulkImportPreviewRow(
    int RowNumber,
    string TierCode,
    Guid ProductId,
    long NetMinor,
    string[] Markets,
    string Outcome);  // "insert" | "update" | "skip"

public sealed record BulkImportRejectedRow(
    int Row,
    string Reason,
    string Code);

public sealed record BulkImportPreviewResponse(
    string PreviewToken,
    DateTimeOffset PreviewTokenExpiresAt,
    IReadOnlyList<BulkImportPreviewRow> WouldInsert,
    IReadOnlyList<BulkImportPreviewRow> WouldUpdate,
    IReadOnlyList<BulkImportPreviewRow> WouldSkip,
    IReadOnlyList<BulkImportRejectedRow> Rejected,
    long SnapshotFingerprint);

public sealed record BulkImportCommitRequest(
    string PreviewToken,
    string IdempotencyKey);

public sealed record BulkImportCommitResponse(
    int Inserted,
    int Updated,
    int Skipped,
    DateTimeOffset CommittedAtUtc);
