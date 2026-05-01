namespace BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;

/// <summary>Request body for POST/PATCH /v1/admin/cms/legal-pages/{kind}/versions/drafts per spec 024 contract §5.</summary>
public sealed record SaveLegalPageVersionDraftRequest(
    string VersionLabel,
    string? BodyAr,
    string? BodyEn,
    DateTimeOffset? EffectiveAtUtc,
    string MarketCode,
    uint? Xmin);
