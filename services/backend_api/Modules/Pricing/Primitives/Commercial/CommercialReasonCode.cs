namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Owned reason-code surface for spec 007-b commercial-authoring (contract §11).
/// All codes are stable strings used in API responses, ICU keys, and audit rows.
/// </summary>
/// <remarks>
/// Total owned codes: 50 (49 launched in PR #78 per T010 / contract §11;
/// <c>BusinessPricingValidationError</c> added in PR #80 round 1).
/// Engine-emitted cross-reference codes (e.g. <c>pricing.coupon.expired</c>)
/// are owned by spec 007-a.
/// </remarks>
public static class CommercialReasonCode
{
    // ---------- Coupons ----------
    public const string CouponCodeDuplicate = "coupon.code.duplicate";
    public const string CouponLabelRequiredBilingual = "coupon.label.required_bilingual";
    public const string CouponScheduleInvalidWindow = "coupon.schedule.invalid_window";
    public const string CouponValueOutOfRange = "coupon.value.out_of_range";
    public const string CouponMarketsRequired = "coupon.markets.required";
    public const string CouponLimitNotZero = "coupon.limit.not_zero";
    public const string CouponLockedActivePricingField = "coupon.locked.active_pricing_field";
    public const string CouponActivationRequiresApproval = "coupon.activation.requires_approval";

    // ---------- Promotions ----------
    public const string PromotionLabelRequiredBilingual = "promotion.label.required_bilingual";
    public const string PromotionScheduleInvalidWindow = "promotion.schedule.invalid_window";
    public const string PromotionAppliesToTooMany = "promotion.applies_to.too_many";
    public const string PromotionAppliesToEmpty = "promotion.applies_to.empty";
    public const string PromotionTargetSkuInvalid = "promotion.target_sku_invalid";
    public const string PromotionLockedActivePricingField = "promotion.locked.active_pricing_field";
    public const string PromotionOverlapWarning = "promotion.overlap.warning";
    public const string PromotionActivationRequiresApproval = "promotion.activation.requires_approval";

    // ---------- Business pricing ----------
    public const string BusinessPricingForbidden = "commercial.business_pricing.forbidden";
    public const string BusinessPricingRowConflict = "business_pricing.row.conflict";
    public const string BusinessPricingRowXorViolation = "business_pricing.row.xor_violation";
    public const string BusinessPricingBelowCogsWarning = "business_pricing.below_cogs.warning";
    public const string BusinessPricingCsvHeaderInvalid = "business_pricing.csv.header_invalid";
    public const string BusinessPricingCsvRowInvalid = "business_pricing.csv.row_invalid";
    public const string BusinessPricingPreviewTokenExpired = "business_pricing.preview_token.expired";
    public const string BusinessPricingPreviewSnapshotChanged = "business_pricing.preview_snapshot.changed";
    /// <summary>
    /// Validation failure on a Business Pricing row write (missing required
    /// field, out-of-range value, etc.). Distinct from
    /// <see cref="BusinessPricingRowConflict"/> which signals a duplicate /
    /// uniqueness violation. CodeRabbit PR #80 round 1.
    /// </summary>
    public const string BusinessPricingValidationError = "business_pricing.validation_error";

    // ---------- Campaigns ----------
    public const string CampaignNameRequiredBilingual = "campaign.name.required_bilingual";
    public const string CampaignLinkTargetExpired = "campaign.link.target_expired";
    public const string CampaignLinkCouponNotDisplayable = "campaign.link.coupon_not_displayable";
    public const string CampaignLinkTargetNotFound = "campaign.link.target_not_found";
    public const string CampaignLinkBroken = "campaign.link.broken";

    // ---------- Preview profiles ----------
    public const string PreviewProfileNameRequired = "preview_profile.name.required";
    public const string PreviewProfileCartTooLarge = "preview_profile.cart.too_large";
    public const string PreviewProfilePromoteRequiresApprover = "preview_profile.promote.requires_approver";
    public const string PreviewProfileDemoteRequiresSuperAdmin = "preview_profile.demote.requires_super_admin";
    public const string PreviewProfileNotVisibleToActor = "preview_profile.not_visible_to_actor";

    // ---------- Approvals ----------
    public const string CommercialSelfApprovalForbidden = "commercial.self_approval.forbidden";
    public const string CommercialApprovalNoteTooShort = "commercial.approval.note_too_short";
    public const string CommercialApprovalAlreadyRecorded = "commercial.approval.already_recorded";
    public const string CommercialApprovalNotPending = "commercial.approval.not_pending";

    // ---------- Thresholds ----------
    public const string CommercialThresholdForbidden = "commercial.threshold.forbidden";
    public const string CommercialThresholdInvalidValue = "commercial.threshold.invalid_value";

    // ---------- Lifecycle / cross-cutting ----------
    public const string CommercialDeactivationReasonRequired = "commercial.deactivation.reason_required";
    public const string CommercialReactivationExpiredTerminal = "commercial.reactivation.expired_terminal";
    public const string CommercialRowVersionConflict = "commercial.row.version_conflict";
    public const string CommercialRowDeleteForbidden = "commercial.row.delete_forbidden";
    public const string CommercialRowExpiredTerminal = "commercial.row.expired_terminal";
    public const string CommercialRowInvalidTransition = "commercial.row.invalid_transition";
    public const string CommercialAppliesToTooMany = "commercial.applies_to.too_many";
    public const string CommercialTextTooLong = "commercial.text.too_long";
    public const string CommercialRateLimitExceeded = "commercial.rate_limit_exceeded";

    /// <summary>All owned codes for ICU-key coverage tests (T046). 49 launched in PR #78; +1 (`BusinessPricingValidationError`) added in PR #80 round 1.</summary>
    public static readonly IReadOnlyList<string> AllCodes =
    [
        CouponCodeDuplicate,
        CouponLabelRequiredBilingual,
        CouponScheduleInvalidWindow,
        CouponValueOutOfRange,
        CouponMarketsRequired,
        CouponLimitNotZero,
        CouponLockedActivePricingField,
        CouponActivationRequiresApproval,
        PromotionLabelRequiredBilingual,
        PromotionScheduleInvalidWindow,
        PromotionAppliesToTooMany,
        PromotionAppliesToEmpty,
        PromotionTargetSkuInvalid,
        PromotionLockedActivePricingField,
        PromotionOverlapWarning,
        PromotionActivationRequiresApproval,
        BusinessPricingForbidden,
        BusinessPricingRowConflict,
        BusinessPricingRowXorViolation,
        BusinessPricingBelowCogsWarning,
        BusinessPricingCsvHeaderInvalid,
        BusinessPricingCsvRowInvalid,
        BusinessPricingPreviewTokenExpired,
        BusinessPricingPreviewSnapshotChanged,
        BusinessPricingValidationError,
        CampaignNameRequiredBilingual,
        CampaignLinkTargetExpired,
        CampaignLinkCouponNotDisplayable,
        CampaignLinkTargetNotFound,
        CampaignLinkBroken,
        PreviewProfileNameRequired,
        PreviewProfileCartTooLarge,
        PreviewProfilePromoteRequiresApprover,
        PreviewProfileDemoteRequiresSuperAdmin,
        PreviewProfileNotVisibleToActor,
        CommercialSelfApprovalForbidden,
        CommercialApprovalNoteTooShort,
        CommercialApprovalAlreadyRecorded,
        CommercialApprovalNotPending,
        CommercialThresholdForbidden,
        CommercialThresholdInvalidValue,
        CommercialDeactivationReasonRequired,
        CommercialReactivationExpiredTerminal,
        CommercialRowVersionConflict,
        CommercialRowDeleteForbidden,
        CommercialRowExpiredTerminal,
        CommercialRowInvalidTransition,
        CommercialAppliesToTooMany,
        CommercialTextTooLong,
        CommercialRateLimitExceeded,
    ];
}
