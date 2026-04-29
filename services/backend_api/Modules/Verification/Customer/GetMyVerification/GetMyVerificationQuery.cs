namespace BackendApi.Modules.Verification.Customer.GetMyVerification;

public sealed record GetMyVerificationResponse(
    Guid Id,
    string State,
    string MarketCode,
    string Profession,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExpiresAt,
    Guid? SupersedesId,
    Guid? SupersededById,
    IReadOnlyList<TransitionPayload> Transitions,
    IReadOnlyList<DocumentMetadataPayload> Documents);

public sealed record TransitionPayload(
    string PriorState,
    string NewState,
    DateTimeOffset OccurredAt,
    string Reason);

public sealed record DocumentMetadataPayload(
    Guid Id,
    string ContentType,
    long SizeBytes,
    string ScanStatus,
    DateTimeOffset UploadedAt,
    bool IsPurged);
