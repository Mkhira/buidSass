namespace BackendApi.Modules.Verification.Admin.GetVerificationDetail;

/// <summary>
/// Reviewer detail response per spec 020 contracts §3.2. Renders the schema
/// as it was at submission (FR-026), the full transition history, and document
/// metadata only (bodies fetched via the OpenHistoricalDocument slice).
/// </summary>
public sealed record GetVerificationDetailResponse(
    Guid Id,
    Guid CustomerId,
    string MarketCode,
    int SchemaVersion,
    string Profession,
    string RegulatorIdentifier,
    string State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    Guid? DecidedBy,
    DateTimeOffset? ExpiresAt,
    Guid? SupersedesId,
    Guid? SupersededById,
    string? VoidReason,
    string CustomerLocale,
    SchemaSnapshotPayload SchemaSnapshot,
    IReadOnlyList<TransitionPayload> Transitions,
    IReadOnlyList<DocumentMetadataPayload> Documents,
    object? RegulatorAssist,    // null when IRegulatorAssistLookup returns null (V1 default)
    uint Xmin);

public sealed record SchemaSnapshotPayload(
    string MarketCode,
    int Version,
    string RequiredFieldsJson,
    string AllowedDocumentTypesJson,
    int RetentionMonths,
    int CooldownDays,
    int ExpiryDays,
    string ReminderWindowsDaysJson,
    int SlaDecisionBusinessDays,
    int SlaWarningBusinessDays);

public sealed record TransitionPayload(
    Guid Id,
    string PriorState,
    string NewState,
    string ActorKind,
    Guid? ActorId,
    string Reason,
    string MetadataJson,
    DateTimeOffset OccurredAt);

public sealed record DocumentMetadataPayload(
    Guid Id,
    string ContentType,
    long SizeBytes,
    string ScanStatus,
    DateTimeOffset UploadedAt,
    DateTimeOffset? PurgedAt);
