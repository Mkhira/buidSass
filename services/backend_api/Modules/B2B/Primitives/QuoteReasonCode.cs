namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 reason-code surface (research §R8 + contract §9). Stable codes are the
/// API contract; ICU-key copy MAY change but enum tokens MUST NOT. Spec 014 (Flutter)
/// and spec 015 (admin web) switch on these tokens to render localized messages and
/// pick CTAs.
///
/// New codes added here MUST also land entries in
/// <c>Modules/B2B/Messages/b2b.en.icu</c> and <c>b2b.ar.icu</c> — the
/// <c>QuoteReasonCodeIcuKeysTests</c> unit test enforces this.
/// </summary>
public enum QuoteReasonCode
{
    QuoteRequiredFieldMissing,
    QuoteCartEmpty,
    QuoteProductNotQuotable,
    QuoteNoActiveCompanyMembership,
    QuotePoRequired,
    QuotePoAlreadyUsed,
    QuotePoWarningAcknowledged,
    QuoteRateLimitExceeded,
    QuoteMarketMismatch,
    QuoteEligibilityRequired,
    QuoteInvalidStateForAction,
    QuoteNoChangesProvided,
    QuoteNoApproverAvailable,

    /// <summary>
    /// Reserved for forward compatibility (research §R10 — V1 has NO cool-down on rejected
    /// quotes). Never raised by V1 handlers but kept in the enum so future changes don't
    /// reshape the contract surface.
    /// </summary>
    QuoteCooldownActive,

    QuoteAlreadyDecided,
    QuoteReasonRequired,
    QuoteBelowBaselineReasonRequired,
    QuoteExpired,
    QuoteTaxPreviewDriftThresholdExceeded,
    QuoteIdempotencyReplay,
    QuoteAccountInactive,
    QuoteCompanySuspended,
    QuoteProductArchived,
    QuoteNotFound,
    QuoteDocumentNotFound,
    CompanyTaxIdInvalid,
    CompanyDuplicateTaxId,
    CompanyLastAdminCannotBeRemoved,
    CompanyLastApproverCannotBeRemovedWithRequired,
    CompanyMemberAlreadyExists,
    CompanyInvitationEmailInvalid,
    CompanyInvitationAlreadyPending,
    CompanyInvitationExpired,
    TemplateNameAlreadyExists,
}

public static class QuoteReasonCodeExtensions
{
    /// <summary>
    /// Stable contract token — exposed in HTTP error envelopes and audit payloads.
    /// </summary>
    public static string ToToken(this QuoteReasonCode code) => code switch
    {
        QuoteReasonCode.QuoteRequiredFieldMissing => "quote.required_field_missing",
        QuoteReasonCode.QuoteCartEmpty => "quote.cart_empty",
        QuoteReasonCode.QuoteProductNotQuotable => "quote.product_not_quotable",
        QuoteReasonCode.QuoteNoActiveCompanyMembership => "quote.no_active_company_membership",
        QuoteReasonCode.QuotePoRequired => "quote.po_required",
        QuoteReasonCode.QuotePoAlreadyUsed => "quote.po_already_used",
        QuoteReasonCode.QuotePoWarningAcknowledged => "quote.po_warning_acknowledged",
        QuoteReasonCode.QuoteRateLimitExceeded => "quote.rate_limit_exceeded",
        QuoteReasonCode.QuoteMarketMismatch => "quote.market_mismatch",
        QuoteReasonCode.QuoteEligibilityRequired => "quote.eligibility_required",
        QuoteReasonCode.QuoteInvalidStateForAction => "quote.invalid_state_for_action",
        QuoteReasonCode.QuoteNoChangesProvided => "quote.no_changes_provided",
        QuoteReasonCode.QuoteNoApproverAvailable => "quote.no_approver_available",
        QuoteReasonCode.QuoteCooldownActive => "quote.cooldown_active",
        QuoteReasonCode.QuoteAlreadyDecided => "quote.already_decided",
        QuoteReasonCode.QuoteReasonRequired => "quote.reason_required",
        QuoteReasonCode.QuoteBelowBaselineReasonRequired => "quote.below_baseline_reason_required",
        QuoteReasonCode.QuoteExpired => "quote.expired",
        QuoteReasonCode.QuoteTaxPreviewDriftThresholdExceeded => "quote.tax_preview_drift_threshold_exceeded",
        QuoteReasonCode.QuoteIdempotencyReplay => "quote.idempotency_replay",
        QuoteReasonCode.QuoteAccountInactive => "quote.account_inactive",
        QuoteReasonCode.QuoteCompanySuspended => "quote.company_suspended",
        QuoteReasonCode.QuoteProductArchived => "quote.product_archived",
        QuoteReasonCode.QuoteNotFound => "quote.not_found",
        QuoteReasonCode.QuoteDocumentNotFound => "quote.document_not_found",
        QuoteReasonCode.CompanyTaxIdInvalid => "company.tax_id_invalid",
        QuoteReasonCode.CompanyDuplicateTaxId => "company.duplicate_tax_id",
        QuoteReasonCode.CompanyLastAdminCannotBeRemoved => "company.last_admin_cannot_be_removed",
        QuoteReasonCode.CompanyLastApproverCannotBeRemovedWithRequired =>
            "company.last_approver_cannot_be_removed_with_required",
        QuoteReasonCode.CompanyMemberAlreadyExists => "company.member_already_exists",
        QuoteReasonCode.CompanyInvitationEmailInvalid => "company.invitation_email_invalid",
        QuoteReasonCode.CompanyInvitationAlreadyPending => "company.invitation_already_pending",
        QuoteReasonCode.CompanyInvitationExpired => "company.invitation_expired",
        QuoteReasonCode.TemplateNameAlreadyExists => "template.name_already_exists",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown QuoteReasonCode"),
    };

    /// <summary>
    /// ICU resource key for the localized customer-facing message. Maps 1:1 to the token —
    /// the convention is `b2b.reason.<token>` so future docs can mechanically derive both
    /// from a single source.
    /// </summary>
    public static string ToIcuKey(this QuoteReasonCode code) => $"b2b.reason.{code.ToToken()}";
}
