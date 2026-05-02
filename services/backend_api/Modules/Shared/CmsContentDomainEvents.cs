using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// 21 domain events emitted by spec 024 CMS on the in-process MediatR bus
/// per data-model §6. Each event carries the standard envelope
/// (event_id, occurred_at_utc, actor_id?, entity_kind, entity_id, version_id,
/// market_code, locale?, payload). Subscribers: spec 025 (notifications),
/// spec 028 (analytics), spec 014 (storefront edge cache).
/// </summary>
public abstract record CmsContentDomainEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    string EntityKindWire,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string? Locale) : INotification;

// Lifecycle events (10) ────────────────────────────────────────────────────

public sealed record CmsBannerPublished(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string SlotKindWire) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "banner_slot", EntityId, VersionId, MarketCode, null);

public sealed record CmsBannerArchived(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string ArchiveReasonNote) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "banner_slot", EntityId, VersionId, MarketCode, null);

public sealed record CmsFeaturedSectionPublished(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string SectionKindWire) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "featured_section", EntityId, VersionId, MarketCode, null);

public sealed record CmsFeaturedSectionArchived(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string ArchiveReasonNote) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "featured_section", EntityId, VersionId, MarketCode, null);

public sealed record CmsFaqPublished(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string CategoryWire) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "faq_entry", EntityId, VersionId, MarketCode, null);

public sealed record CmsFaqArchived(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string ArchiveReasonNote) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "faq_entry", EntityId, VersionId, MarketCode, null);

public sealed record CmsBlogArticlePublished(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string AuthoredLocale,
    string CategoryWire) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "blog_article", EntityId, VersionId, MarketCode, AuthoredLocale);

public sealed record CmsBlogArticleArchived(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string ArchiveReasonNote) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "blog_article", EntityId, VersionId, MarketCode, null);

public sealed record CmsLegalPageVersionPublished(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string LegalPageKindWire,
    string VersionLabel,
    DateTimeOffset EffectiveAtUtc) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "legal_page_version", EntityId, VersionId, MarketCode, null);

public sealed record CmsLegalPageVersionSuperseded(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorId,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string LegalPageKindWire,
    Guid SupersededByVersionId) : CmsContentDomainEvent(EventId, OccurredAtUtc, ActorId, "legal_page_version", EntityId, VersionId, MarketCode, null);

// Operational events (11) ──────────────────────────────────────────────────

public sealed record CmsFeaturedSectionPartialBroken(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    int TotalReferences,
    int TotalUnavailable) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "featured_section", EntityId, VersionId, MarketCode, null);

public sealed record CmsFeaturedSectionFullyBroken(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    int TotalReferences) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "featured_section", EntityId, VersionId, MarketCode, null);

public sealed record CmsBannerScheduledPublishBlockedCapacity(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string SlotKindWire,
    int CurrentLiveCount,
    int Cap) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "banner_slot", EntityId, VersionId, MarketCode, null);

public sealed record CmsBannerCtaTargetBroken(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string CtaKindWire,
    string? CtaTarget) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "banner_slot", EntityId, VersionId, MarketCode, null);

public sealed record CmsCacheInvalidateBanner(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "banner_slot", EntityId, VersionId, MarketCode, null);

public sealed record CmsCacheInvalidateFeaturedSection(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "featured_section", EntityId, VersionId, MarketCode, null);

public sealed record CmsCacheInvalidateFaq(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "faq_entry", EntityId, VersionId, MarketCode, null);

public sealed record CmsCacheInvalidateLegalPage(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string LegalPageKindWire) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "legal_page_version", EntityId, VersionId, MarketCode, null);

public sealed record CmsCacheInvalidateBlogArticle(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    Guid VersionId,
    string MarketCode,
    string Slug) : CmsContentDomainEvent(EventId, OccurredAtUtc, null, "blog_article", EntityId, VersionId, MarketCode, null);

public sealed record CmsDraftStaleAlert(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    string EntityKindWire,
    Guid OwnerActorId,
    string MarketCode,
    DateTimeOffset DraftCreatedAtUtc) : CmsContentDomainEvent(EventId, OccurredAtUtc, OwnerActorId, EntityKindWire, EntityId, EntityId, MarketCode, null);

public sealed record CmsDraftOwnershipOrphaned(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid EntityId,
    string EntityKindWire,
    Guid PriorOwnerActorId,
    string MarketCode) : CmsContentDomainEvent(EventId, OccurredAtUtc, PriorOwnerActorId, EntityKindWire, EntityId, EntityId, MarketCode, null);
