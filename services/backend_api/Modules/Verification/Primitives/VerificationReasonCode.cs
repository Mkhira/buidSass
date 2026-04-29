namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Stable error-reason codes returned by verification handlers. Enumerated here so
/// the contracts file in spec 020 §7 stays in sync with the implementation. Mapped
/// 1:1 to ICU keys in <c>verification.{en,ar}.icu</c>.
/// </summary>
/// <remarks>
/// Eligibility-side reason codes live separately on <see cref="EligibilityReasonCode"/>
/// because their consumers (Catalog/Cart/Checkout) read them directly from the
/// eligibility query, not from a verification handler error envelope.
/// </remarks>
public enum VerificationReasonCode
{
    // Submission validation
    RequiredFieldMissing,
    RegulatorIdentifierInvalid,
    DocumentsInvalid,
    DocumentScanInfected,
    DocumentScanPending,
    DocumentSizeExceeded,
    DocumentCountExceeded,
    DocumentAggregateSizeExceeded,
    DocumentMimeForbidden,

    // Submission lifecycle
    AlreadyPending,
    CooldownActive,
    AccountInactive,
    MarketUnsupported,
    RenewalNotEligible,
    RenewalAlreadyPending,

    // Reviewer-side
    AlreadyDecided,
    InvalidStateForAction,
    ReviewReasonRequired,
    ReviewerScopeMismatch,
    PiiAccessForbidden,

    // Concurrency + idempotency
    OptimisticConcurrencyConflict,
    IdempotencyKeyMissing,
    IdempotencyKeyConflict,

    // Cross-module / system
    LinkedEntityUnavailable,
    EligibilityCacheStale,
}

public static class VerificationReasonCodeExtensions
{
    /// <summary>
    /// ICU bundle key. Every entry MUST exist in BOTH locale bundles before the
    /// slice that emits the code can ship at DoD.
    /// </summary>
    public static string ToIcuKey(this VerificationReasonCode code) => code switch
    {
        VerificationReasonCode.RequiredFieldMissing => "verification.required_field_missing",
        VerificationReasonCode.RegulatorIdentifierInvalid => "verification.regulator_identifier_invalid",
        VerificationReasonCode.DocumentsInvalid => "verification.documents_invalid",
        VerificationReasonCode.DocumentScanInfected => "verification.document.scan_infected",
        VerificationReasonCode.DocumentScanPending => "verification.document.scan_pending",
        VerificationReasonCode.DocumentSizeExceeded => "verification.document.size_exceeded",
        VerificationReasonCode.DocumentCountExceeded => "verification.document.count_exceeded",
        VerificationReasonCode.DocumentAggregateSizeExceeded => "verification.document.aggregate_size_exceeded",
        VerificationReasonCode.DocumentMimeForbidden => "verification.document.mime_forbidden",
        VerificationReasonCode.AlreadyPending => "verification.already_pending",
        VerificationReasonCode.CooldownActive => "verification.cooldown_active",
        VerificationReasonCode.AccountInactive => "verification.account_inactive",
        VerificationReasonCode.MarketUnsupported => "verification.market_unsupported",
        VerificationReasonCode.RenewalNotEligible => "verification.renewal_not_eligible",
        VerificationReasonCode.RenewalAlreadyPending => "verification.renewal_already_pending",
        VerificationReasonCode.AlreadyDecided => "verification.already_decided",
        VerificationReasonCode.InvalidStateForAction => "verification.invalid_state_for_action",
        VerificationReasonCode.ReviewReasonRequired => "verification.review.reason_required",
        VerificationReasonCode.ReviewerScopeMismatch => "verification.reviewer.scope_mismatch",
        VerificationReasonCode.PiiAccessForbidden => "verification.pii.access_forbidden",
        VerificationReasonCode.OptimisticConcurrencyConflict => "verification.row.version_conflict",
        VerificationReasonCode.IdempotencyKeyMissing => "verification.idempotency.key_missing",
        VerificationReasonCode.IdempotencyKeyConflict => "verification.idempotency.key_conflict",
        VerificationReasonCode.LinkedEntityUnavailable => "verification.linked_entity_unavailable",
        VerificationReasonCode.EligibilityCacheStale => "verification.eligibility.cache_stale",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    /// <summary>
    /// Wire-level slug used in HTTP error envelopes (Problem Details `type` field).
    /// </summary>
    public static string ToWireValue(this VerificationReasonCode code) => code.ToIcuKey();

    public static IReadOnlyCollection<VerificationReasonCode> All { get; } =
        Enum.GetValues<VerificationReasonCode>();
}
