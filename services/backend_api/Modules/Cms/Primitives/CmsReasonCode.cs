namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// All 43 owned reason codes per spec 024 contracts §10. Stable string
/// constants — never renamed (they are part of the wire contract). Plus a few
/// parameterised templates for kind-scoped codes.
/// </summary>
public static class CmsReasonCode
{
    // RBAC
    public const string EditorRoleRequired = "cms.editor.role_required";
    public const string PublishRoleRequired = "cms.publish.role_required";
    public const string LegalPagePublishRoleRequired = "cms.legal_page.publish.role_required";
    public const string LegalPageCrossMarketRequiresSuperAdmin = "cms.legal_page.cross_market_requires_super_admin";

    // Publish gates
    public const string PublishLocaleCompletenessMissing = "cms.publish.locale_completeness_missing";
    public const string PublishEffectiveAtRequired = "cms.publish.effective_at_required";

    // Banner save
    public const string BannerScheduleWindowInvalid = "cms.banner.schedule_window_invalid";
    public const string BannerCtaKindTargetMismatch = "cms.banner.cta_kind_target_mismatch";
    public const string BannerExternalUrlHttpsRequired = "cms.banner.external_url_https_required";

    // Banner publish
    public const string BannerCtaTargetUnresolvable = "cms.banner.cta_target_unresolvable";
    public const string BannerSlotCapacityExceeded = "cms.banner.slot_capacity_exceeded";
    public const string BannerCampaignAlreadyBound = "cms.banner.campaign_already_bound";
    public const string BannerArchiveBlockedByCampaignBinding = "cms.banner.archive_blocked_by_campaign_binding";

    // Featured
    public const string FeaturedSectionEmptyReferences = "cms.featured_section.empty_references";
    public const string FeaturedSectionTooManyReferences = "cms.featured_section.too_many_references";
    public const string FeaturedSectionReferenceKindUnsupported = "cms.featured_section.reference_kind_unsupported";

    // FAQ
    public const string FaqReorderConflict = "cms.faq.reorder_conflict";

    // Blog
    public const string BlogSlugCollision = "cms.blog.slug_collision";
    public const string BlogSlugInvalidPattern = "cms.blog.slug_invalid_pattern";
    public const string BlogBodyTooLong = "cms.blog.body_too_long";

    // Asset
    public const string AssetMimeForbidden = "cms.asset.mime_forbidden";

    // Draft
    public const string DraftNotEditable = "cms.draft.not_editable";
    public const string DraftVersionConflict = "cms.draft.version_conflict";
    public const string DraftDeleteNotOwner = "cms.draft.delete_not_owner";

    // Archive
    public const string ArchiveReasonNoteRequired = "cms.archive.reason_note_required";

    // Legal page
    public const string LegalPageVersionDeleteForbidden = "cms.legal_page.version.delete_forbidden";
    public const string LegalPageVersionConflict = "cms.legal_page.version_conflict";
    public const string LegalPageScopeNotCrossMarket = "cms.legal_page.scope_not_cross_market";
    public const string LegalPageNotFoundForMarket = "cms.legal_page.not_found_for_market";

    // Preview tokens
    public const string PreviewTtlOutOfRange = "cms.preview.ttl_out_of_range";
    public const string PreviewEntityNotDraftable = "cms.preview.entity_not_draftable";
    public const string PreviewTokenSignatureInvalid = "cms.preview.token_signature_invalid";
    public const string PreviewTokenExpiredOrRevoked = "cms.preview.token_expired_or_revoked";
    public const string PreviewTokenNotFound = "cms.preview.token_not_found";
    public const string PreviewTokenAlreadyRevoked = "cms.preview.token_already_revoked";
    public const string PreviewEntityNotFound = "cms.preview.entity_not_found";
    public const string PreviewRateLimited = "cms.preview.rate_limited";

    // Market schema
    public const string MarketSchemaValueOutOfRange = "cms.market_schema.value_out_of_range";
    public const string MarketSchemaVersionConflict = "cms.market_schema.version_conflict";

    // Storefront
    public const string StorefrontMarketUnsupported = "cms.storefront.market_unsupported";
    public const string StorefrontLocaleUnsupported = "cms.storefront.locale_unsupported";
    public const string StorefrontRateLimited = "cms.storefront.rate_limited";

    // Idempotency + admin rate
    public const string IdempotencyKeyConflict = "cms.idempotency_key_conflict";
    public const string AdminRateLimitExceeded = "cms.admin_rate_limit_exceeded";

    // Parameterised templates (kind-scoped)
    public static string ArchiveForbiddenInState(EntityKind kind) =>
        $"cms.{kind.ToWire()}.archive_forbidden_in_state";

    public static string DeleteForbidden(EntityKind kind) =>
        $"cms.{kind.ToWire()}.delete_forbidden";

    public static string IllegalTransition(EntityKind kind) =>
        $"cms.{kind.ToWire()}.illegal_transition";

    /// <summary>ICU key for a fixed-string reason code.</summary>
    public static string IcuKey(string reasonCode) => $"cms.reason.{reasonCode}";
}
