namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// The single authoritative answer to "may this customer purchase this restricted SKU
/// right now?" per spec 020 contracts §4.1 / data-model §4.
/// </summary>
public enum EligibilityClass
{
    /// <summary>Customer + market + profession satisfy the SKU's restriction policy and approval is non-expired.</summary>
    Eligible,
    /// <summary>One of the verification reason codes blocks the purchase.</summary>
    Ineligible,
    /// <summary>SKU is not restricted in the customer's market — silent path.</summary>
    Unrestricted,
}

/// <summary>
/// Reason code attached to every <see cref="EligibilityResult"/>. Maps 1:1 to ICU
/// keys in <c>verification.{en,ar}.icu</c>; emitted into <c>openapi.verification.json</c>
/// for type-safe consumption by spec 014 (Flutter customer app).
/// </summary>
public enum EligibilityReasonCode
{
    Eligible,
    Unrestricted,
    VerificationRequired,
    VerificationPending,
    VerificationInfoRequested,
    VerificationRejected,
    VerificationExpired,
    VerificationRevoked,
    ProfessionMismatch,
    MarketMismatch,
    AccountInactive,
}

public static class EligibilityReasonCodeExtensions
{
    /// <summary>
    /// ICU message-bundle key for the reason. Every key MUST exist in BOTH
    /// <c>verification.en.icu</c> AND <c>verification.ar.icu</c> per Principle 4
    /// (verified by <c>EligibilityReasonCodeIcuKeysTests</c>).
    /// </summary>
    public static string ToIcuKey(this EligibilityReasonCode code) => code switch
    {
        EligibilityReasonCode.Eligible => "verification.eligibility.eligible",
        EligibilityReasonCode.Unrestricted => "verification.eligibility.unrestricted",
        EligibilityReasonCode.VerificationRequired => "verification.eligibility.required",
        EligibilityReasonCode.VerificationPending => "verification.eligibility.pending",
        EligibilityReasonCode.VerificationInfoRequested => "verification.eligibility.info_requested",
        EligibilityReasonCode.VerificationRejected => "verification.eligibility.rejected",
        EligibilityReasonCode.VerificationExpired => "verification.eligibility.expired",
        EligibilityReasonCode.VerificationRevoked => "verification.eligibility.revoked",
        EligibilityReasonCode.ProfessionMismatch => "verification.eligibility.profession_mismatch",
        EligibilityReasonCode.MarketMismatch => "verification.eligibility.market_mismatch",
        EligibilityReasonCode.AccountInactive => "verification.eligibility.account_inactive",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    public static IReadOnlyCollection<EligibilityReasonCode> All { get; } =
        Enum.GetValues<EligibilityReasonCode>();
}

/// <summary>
/// Result returned by <c>ICustomerVerificationEligibilityQuery.EvaluateAsync</c>.
/// </summary>
/// <param name="Class">Coarse-grained outcome.</param>
/// <param name="ReasonCode">Fine-grained reason for downstream UI / messaging.</param>
/// <param name="MessageKey">ICU key for the customer-visible string. Resolves to <see cref="EligibilityReasonCodeExtensions.ToIcuKey"/>.</param>
/// <param name="ExpiresAt">Set only when <see cref="Class"/> is <see cref="EligibilityClass.Eligible"/>; mirrors the active approval's <c>expires_at</c>.</param>
public sealed record EligibilityResult(
    EligibilityClass Class,
    EligibilityReasonCode ReasonCode,
    string MessageKey,
    DateTimeOffset? ExpiresAt);
