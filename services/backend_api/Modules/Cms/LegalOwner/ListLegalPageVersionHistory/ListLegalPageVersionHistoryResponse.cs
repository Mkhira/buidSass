namespace BackendApi.Modules.Cms.LegalOwner.ListLegalPageVersionHistory;

public sealed record ListLegalPageVersionHistoryResponse(
    IReadOnlyList<LegalPageVersionHistoryItem> Items);

public sealed record LegalPageVersionHistoryItem(
    Guid VersionId,
    string LegalPageKindWire,
    string MarketCode,
    string VersionLabel,
    string State,
    DateTimeOffset? EffectiveAtUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? SupersededAtUtc,
    Guid? SupersededByVersionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset EditorSaveAtUtc,
    Guid OwnerActorId,
    uint Xmin);
