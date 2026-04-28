# Data Model: CMS

**Phase**: 1 (input to contracts/ and quickstart.md)
**Date**: 2026-04-29

This document specifies the 9 net-new tables in the `cms` schema, the unified content lifecycle (+ `superseded` terminal for legal), the audit-event kinds, the domain events emitted on the in-process bus, and the cross-module read / publish contracts declared in `Modules/Shared/`. All names use `snake_case` for SQL identifiers and `PascalCase` for the C# entity classes.

---

## §1. Schema

```sql
CREATE SCHEMA cms;
```

All tables sit under `cms.*`. Foreign keys to other schemas are LOGICAL only (no DB-level FK across module boundaries; cross-module pattern from specs 020 / 021 / 022 / 023 / 007-b).

---

## §2. Tables

### §2.1 `cms.banner_slots`

Versioned hero / category-strip / footer-strip / home-secondary banner rows.

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | `gen_random_uuid()`. Stable across versions of "the same banner" (a row IS a version; "same banner re-edited" creates a fresh row) |
| `slot_kind` | `text NOT NULL CHECK (slot_kind IN ('hero_top','category_strip','footer_strip','home_secondary'))` | FR-006 enum |
| `headline_ar` | `text NULL` | max 120 chars; required at publish (FR-007) |
| `headline_en` | `text NULL` | max 120 chars; required at publish |
| `subhead_ar` | `text NULL` | max 240 chars |
| `subhead_en` | `text NULL` | max 240 chars |
| `asset_id_ar` | `uuid NULL` | FK-style logical reference to `cms.assets.id`; required at publish |
| `asset_id_en` | `uuid NULL` | same |
| `cta_kind` | `text NOT NULL CHECK (cta_kind IN ('link','category','product','bundle','external_url','none'))` | FR-006 enum |
| `cta_target` | `text NULL` | shape depends on `cta_kind` (relative path, UUID, or absolute https URL) |
| `cta_health` | `text NOT NULL DEFAULT 'not_applicable' CHECK (cta_health IN ('verified','broken','transient_unverified','not_applicable'))` | FR-022a |
| `scheduled_start_utc` | `timestamptz NULL` | required at scheduled-publish |
| `scheduled_end_utc` | `timestamptz NULL` | required at scheduled-publish; MUST > start (FR-006 edge case) |
| `market_code` | `text NOT NULL CHECK (market_code IN ('EG','KSA','*'))` | FR-006 |
| `priority_within_slot` | `int NOT NULL DEFAULT 100` | sort `ASC`; FR-006 |
| `state` | `text NOT NULL DEFAULT 'draft' CHECK (state IN ('draft','scheduled','live','archived'))` | FR-001 |
| `vendor_id` | `uuid NULL` | P6; never populated in V1; indexed |
| `owner_actor_id` | `uuid NOT NULL` | author / draft owner; FR-034a |
| `ownership_orphaned` | `boolean NOT NULL DEFAULT false` | FR-034a |
| `last_stale_alert_at_utc` | `timestamptz NULL` | FR-034a; rate-limit boundary |
| `last_stale_alert_dismissed_at_utc` | `timestamptz NULL` | FR-034a |
| `created_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `editor_save_at_utc` | `timestamptz NOT NULL DEFAULT now()` | bumped on every save |
| `published_at_utc` | `timestamptz NULL` | stamped on `→ live` |
| `archived_at_utc` | `timestamptz NULL` | stamped on `→ archived` |
| `archive_reason_note` | `text NULL` | required on archive (FR-013) |
| `xmin` | `xid` | EF row_version (Postgres system column) |

**Indexes**: `(state, market_code, slot_kind, priority_within_slot, created_at_utc)` for the storefront banner read; `(state, scheduled_start_utc)` + `(state, scheduled_end_utc)` for the worker scans; `(owner_actor_id, state)` for editor's "my drafts"; `(vendor_id)` for Phase 2 vendor reads.

### §2.2 `cms.featured_sections`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `section_kind` | `text NOT NULL CHECK (section_kind IN ('home_top','home_mid','category_landing','b2b_landing'))` | FR-006 |
| `title_ar` | `text NULL` | required at publish |
| `title_en` | `text NULL` | required at publish |
| `subtitle_ar` | `text NULL` | |
| `subtitle_en` | `text NULL` | |
| `references` | `jsonb NOT NULL` | array of `{"kind":"product\|category\|bundle","id":"<uuid>"}`; min 1, max 24 (FR-006 + FR-019) |
| `display_priority` | `int NOT NULL DEFAULT 100` | |
| `market_code` | `text NOT NULL CHECK (market_code IN ('EG','KSA','*'))` | |
| `state` | `text NOT NULL DEFAULT 'draft' CHECK (state IN ('draft','scheduled','live','archived'))` | |
| `scheduled_publish_at_utc` | `timestamptz NULL` | |
| `vendor_id` | `uuid NULL` | |
| `owner_actor_id` | `uuid NOT NULL` | |
| `ownership_orphaned` | `boolean NOT NULL DEFAULT false` | |
| `last_stale_alert_at_utc` | `timestamptz NULL` | |
| `last_stale_alert_dismissed_at_utc` | `timestamptz NULL` | |
| `last_partial_broken_alert_at_utc` | `timestamptz NULL` | rate-limit per FR-019 |
| `created_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `editor_save_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `published_at_utc` | `timestamptz NULL` | |
| `archived_at_utc` | `timestamptz NULL` | |
| `archive_reason_note` | `text NULL` | |
| `xmin` | `xid` | |

**Indexes**: `(state, market_code, section_kind, display_priority, created_at_utc)` storefront sort; `(state, scheduled_publish_at_utc)` worker scan; jsonb GIN on `references` for "which sections reference product X" admin lookups.

### §2.3 `cms.faq_entries`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `category` | `text NOT NULL CHECK (category IN ('ordering','payment','shipping','returns','account','verification','b2b','other'))` | FR-006 |
| `question_ar` | `text NULL` | max 250; required at publish |
| `question_en` | `text NULL` | same |
| `answer_ar` | `text NULL` | max 4000 (markdown); required at publish |
| `answer_en` | `text NULL` | same |
| `display_order` | `int NOT NULL DEFAULT 100` | |
| `market_code` | `text NOT NULL CHECK (market_code IN ('EG','KSA','*'))` | |
| `state` | `text NOT NULL DEFAULT 'draft' CHECK (state IN ('draft','scheduled','live','archived'))` | |
| `scheduled_publish_at_utc` | `timestamptz NULL` | |
| `vendor_id` | `uuid NULL` | |
| `owner_actor_id` | `uuid NOT NULL` | |
| `ownership_orphaned` | `boolean NOT NULL DEFAULT false` | |
| `last_stale_alert_at_utc` | `timestamptz NULL` | |
| `last_stale_alert_dismissed_at_utc` | `timestamptz NULL` | |
| `created_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `editor_save_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `published_at_utc` | `timestamptz NULL` | |
| `archived_at_utc` | `timestamptz NULL` | |
| `archive_reason_note` | `text NULL` | |
| `xmin` | `xid` | guards bulk-reorder concurrency |

**Indexes**: `(state, market_code, category, display_order, created_at_utc)` storefront sort; `(category, market_code)` admin grouping.

### §2.4 `cms.blog_articles`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `category` | `text NOT NULL CHECK (category IN ('tips','news','guides','case_studies','clinical','other'))` | FR-006 |
| `slug` | `text NOT NULL` | unique per `(market_code, authored_locale)` |
| `authored_locale` | `text NOT NULL CHECK (authored_locale IN ('ar','en'))` | FR-006 |
| `title` | `text NOT NULL` | required at save |
| `summary` | `text NULL` | |
| `body` | `text NULL` | markdown; max 60 000 chars |
| `cover_asset_id` | `uuid NULL` | logical reference to `cms.assets.id` |
| `seo_meta_title` | `text NULL` | max 70 |
| `seo_meta_description` | `text NULL` | max 160 |
| `seo_og_image_id` | `uuid NULL` | logical reference to `cms.assets.id` |
| `seo_schema_org_kind` | `text NOT NULL DEFAULT 'BlogPosting' CHECK (seo_schema_org_kind IN ('Article','BlogPosting','NewsArticle','FAQPage'))` | FR-028 |
| `scheduled_publish_at_utc` | `timestamptz NULL` | |
| `market_code` | `text NOT NULL CHECK (market_code IN ('EG','KSA','*'))` | |
| `state` | `text NOT NULL DEFAULT 'draft' CHECK (state IN ('draft','scheduled','live','archived'))` | |
| `vendor_id` | `uuid NULL` | |
| `owner_actor_id` | `uuid NOT NULL` | |
| `ownership_orphaned` | `boolean NOT NULL DEFAULT false` | |
| `last_stale_alert_at_utc` | `timestamptz NULL` | |
| `last_stale_alert_dismissed_at_utc` | `timestamptz NULL` | |
| `created_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `editor_save_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `published_at_utc` | `timestamptz NULL` | |
| `archived_at_utc` | `timestamptz NULL` | |
| `archive_reason_note` | `text NULL` | |
| `xmin` | `xid` | |

**Indexes**: `UNIQUE (market_code, authored_locale, slug)`; `(state, market_code, category, published_at_utc DESC)` storefront sort; `(state, scheduled_publish_at_utc)` worker scan.

### §2.5 `cms.legal_page_versions`

Append-only per `(legal_page_kind, market_code)`. Hard-delete forbidden by `BEFORE DELETE` trigger.

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `legal_page_kind` | `text NOT NULL CHECK (legal_page_kind IN ('terms','privacy','returns','cookies'))` | FR-006 |
| `version_label` | `text NOT NULL` | e.g., `2.3.0`; free-text |
| `body_ar` | `text NULL` | required at publish (FR-007) |
| `body_en` | `text NULL` | required at publish |
| `effective_at_utc` | `timestamptz NULL` | required at publish |
| `market_code` | `text NOT NULL CHECK (market_code IN ('EG','KSA','*'))` | `*` requires `super_admin` |
| `state` | `text NOT NULL DEFAULT 'draft' CHECK (state IN ('draft','scheduled','live','superseded'))` | NOTE: `archived` not used; `superseded` replaces it for legal pages (Principle 17 + R2) |
| `superseded_at_utc` | `timestamptz NULL` | stamped on `→ superseded` |
| `superseded_by_version_id` | `uuid NULL` | self-FK to the new `live` version |
| `vendor_id` | `uuid NULL` | |
| `owner_actor_id` | `uuid NOT NULL` | |
| `ownership_orphaned` | `boolean NOT NULL DEFAULT false` | |
| `last_stale_alert_at_utc` | `timestamptz NULL` | |
| `last_stale_alert_dismissed_at_utc` | `timestamptz NULL` | |
| `created_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `editor_save_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `published_at_utc` | `timestamptz NULL` | |
| `xmin` | `xid` | guards concurrent publish race |

**Indexes**: `(legal_page_kind, market_code, state, effective_at_utc)` for "current `live` version per kind+market" + version-history; `(state, effective_at_utc)` worker scan.

**Triggers**:
- `BEFORE DELETE` → `RAISE EXCEPTION 'cms.legal_page.version.delete_forbidden'`.
- `BEFORE UPDATE` allowed only for the supersede transition (`state='live' → 'superseded'`) and the publish transitions on `state='draft'` rows (`→ scheduled` or `→ live`); other body-field updates on a non-`draft` row are blocked at the application layer (the API layer rejects PATCH on non-`draft` rows).

### §2.6 `cms.assets`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `storage_object_id` | `uuid NOT NULL` | logical reference to spec 015 storage |
| `mime` | `text NOT NULL` | |
| `size_bytes` | `bigint NOT NULL` | |
| `intended_locale` | `text NULL CHECK (intended_locale IS NULL OR intended_locale IN ('ar','en','*'))` | author hint |
| `original_filename` | `text NOT NULL` | |
| `storage_object_state` | `text NOT NULL DEFAULT 'active' CHECK (storage_object_state IN ('active','swept'))` | FR-009a |
| `dereferenced_at_utc` | `timestamptz NULL` | last dereference event |
| `swept_at_utc` | `timestamptz NULL` | when worker swept the storage object |
| `uploaded_by_actor_id` | `uuid NOT NULL` | |
| `uploaded_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `xmin` | `xid` | |

**Indexes**: `(storage_object_state, dereferenced_at_utc)` for the GC worker scan.

**Triggers**: `BEFORE DELETE` blocks application deletes; the GC worker uses an EF `Update` to flip `storage_object_state` to `swept` (audited).

### §2.7 `cms.preview_tokens`

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `token_hash` | `bytea NOT NULL UNIQUE` | sha256 of the opaque token (the token itself is NEVER persisted) |
| `entity_kind` | `text NOT NULL CHECK (entity_kind IN ('banner_slot','featured_section','faq_entry','blog_article','legal_page_version'))` | |
| `entity_id` | `uuid NOT NULL` | |
| `version_id` | `uuid NOT NULL` | always equal to `entity_id` in the current schema; carried for forward compatibility |
| `actor_role_at_mint` | `text NOT NULL` | |
| `minted_by_actor_id` | `uuid NOT NULL` | |
| `minted_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `expires_at_utc` | `timestamptz NOT NULL` | minted_at + ttl |
| `revoked_at_utc` | `timestamptz NULL` | immediate revocation |
| `revoked_by_actor_id` | `uuid NULL` | |

**Indexes**: `UNIQUE (token_hash)`; `(expires_at_utc)` for the cleanup worker scan.

**Triggers**: `BEFORE DELETE` allows only the cleanup worker (rows with `expires_at_utc + 30d < now()`); other deletes are blocked.

### §2.8 `cms.banner_campaign_bindings`

Append-only banner ↔ campaign links.

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid PK` | |
| `banner_id` | `uuid NOT NULL` | logical reference to `cms.banner_slots.id` |
| `version_id` | `uuid NOT NULL` | always equal to `banner_id` in the current schema |
| `campaign_id` | `uuid NOT NULL` | logical reference to spec 007-b `pricing.campaigns` |
| `bound_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `released_at_utc` | `timestamptz NULL` | stamp on release |
| `binding_state` | `text NOT NULL DEFAULT 'active' CHECK (binding_state IN ('active','released_due_to_campaign_deactivation','released_by_editor'))` | |
| `release_actor_id` | `uuid NULL` | |
| `release_reason_note` | `text NULL` | |
| `xmin` | `xid` | |

**Indexes**: `(banner_id, binding_state)` for "is this banner bound right now"; `(campaign_id, binding_state)` for the campaign-deactivated subscriber.

**Triggers**: `BEFORE DELETE` blocks; release stamps + state flip is the only transition allowed on a non-`active` row.

### §2.9 `cms.market_schemas`

| Column | Type | Notes |
|---|---|---|
| `market_code` | `text PRIMARY KEY CHECK (market_code IN ('EG','KSA','*'))` | |
| `banner_max_live_per_slot` | `int NOT NULL DEFAULT 5 CHECK (banner_max_live_per_slot BETWEEN 1 AND 10)` | FR-021a |
| `featured_section_max_references` | `int NOT NULL DEFAULT 24 CHECK (featured_section_max_references BETWEEN 1 AND 100)` | |
| `preview_token_default_ttl_hours` | `int NOT NULL DEFAULT 24 CHECK (preview_token_default_ttl_hours BETWEEN 1 AND 168)` | |
| `draft_staleness_alert_days` | `int NOT NULL DEFAULT 30 CHECK (draft_staleness_alert_days BETWEEN 7 AND 365)` | FR-034a |
| `asset_grace_period_days` | `int NOT NULL DEFAULT 7 CHECK (asset_grace_period_days BETWEEN 0 AND 30)` | FR-009a |
| `last_edited_by_actor_id` | `uuid NOT NULL` | |
| `last_edited_at_utc` | `timestamptz NOT NULL DEFAULT now()` | |
| `xmin` | `xid` | |

Seeded for `EG`, `KSA`, `*` at initial migration; edits require `super_admin` per FR-031.

---

## §3. Content Lifecycle State Machine

```
                    publisher_publish_now
                ┌──────────────────────────┐
                │                          ▼
   ┌─────────┐  │  ┌────────────┐    ┌──────────┐    ┌──────────┐
   │  draft  │──┴──▶│ scheduled  │───▶│   live   │───▶│ archived │   (terminal soft-delete for 4 kinds)
   └─────────┘     └────────────┘    └──────────┘    └──────────┘
        │           publisher_schedule    worker_promote_to_live    publisher_archive
        │                                  worker_promote_to_archived (banner only on scheduled_end)
        │                                          │
        │                                          ▼
        │                                   ┌─────────────┐
        │                                   │ superseded  │   (terminal — legal_page_version only)
        │                                   └─────────────┘
        │                                       worker_supersede_legal_version
        │
        └─── editor_delete_unpublished_draft (HARD-DELETE; only on `draft`; rate-limited)
```

**Allowed transitions**:

| From | To | Trigger | Actor | Constraints |
|---|---|---|---|---|
| draft | scheduled | `publisher_schedule` | publisher (or legal_owner for legal_page_version) | locale-completeness gate (FR-007); banner-CTA validation (FR-022a); banner capacity check (FR-021a); featured-section refs ≥ 1 valid (FR-019) |
| draft | live | `publisher_publish_now` | publisher (or legal_owner; or super_admin for `*`-scoped legal page) | same gates as above, plus immediate cache invalidate event |
| scheduled | live | `worker_promote_to_live` | system | gates re-checked at worker tick; failure leaves row in `scheduled` and emits blocked event |
| live | archived | `publisher_archive` | publisher (or legal_owner for legal_page_version — but only on the latest `live` version when no schedule transition is in flight) | banner-campaign binding check (FR-023); reason_note ≥ 10 chars |
| live | archived | `worker_promote_to_archived` | system | banner only (on `scheduled_end_utc`) |
| live | superseded | `worker_supersede_legal_version` | system | legal_page_version only; happens in the same txn as the new version's `→ live` |
| draft | (deleted) | `editor_delete_unpublished_draft` | editor (creator) or super_admin | hard-delete; only on `draft`; rate-limited 30/h/actor |

**Forbidden transitions** (compile-time guards in `CmsContentLifecycle.cs`):
- `archived → *` — terminal soft state.
- `superseded → *` — terminal.
- `live → draft` — no rollback; create a fresh draft for revisions.
- `scheduled → draft` — no unschedule; the editor archives the scheduled row and creates a fresh draft if changes are needed.
- `* → archived` directly from `draft` (skip `live`) — exception: `editor_delete_unpublished_draft` removes the row entirely; archive is for content that was once `live`.

**Reason codes on forbidden transitions**: `400 cms.{entity_kind}.illegal_transition` carries the source + target states for diagnostics.

---

## §4. Locale-Completeness Gate (FR-007)

Implemented in `LocaleCompletenessGate.cs`. Returns `Allowed` or `Blocked(reason)`.

| Entity kind | Required at publish |
|---|---|
| `banner_slot` | both `headline_ar` + `headline_en` non-null + non-empty AND both `asset_id_ar` + `asset_id_en` non-null |
| `featured_section` | both `title_ar` + `title_en` non-null + non-empty AND `references` length ≥ 1 |
| `faq_entry` | `question_ar` + `question_en` + `answer_ar` + `answer_en` all non-null + non-empty |
| `blog_article` | the body of `authored_locale` non-null + non-empty AND `seo_meta_title` + `seo_meta_description` present |
| `legal_page_version` | both `body_ar` + `body_en` non-null + non-empty AND `effective_at_utc` non-null |

Reason code on blocked publish: `400 cms.publish.locale_completeness_missing` with a per-locale field-level breakdown.

---

## §5. Audit Event Kinds

Every audit row carries `actor_id`, `actor_role`, `entity_kind`, `entity_id`, `version_id`, `from_state?`, `to_state?`, `triggered_by` (FR-002 enum), `timestamp_utc`, `reason_note?`, `before_jsonb?`, `after_jsonb?`. The 19 kinds:

1. `cms.draft.created` — first save of a new draft
2. `cms.draft.updated` — subsequent save (xmin advance)
3. `cms.draft.deleted` — FR-005a draft-delete path
4. `cms.draft.stale_alert_dismissed` — FR-034a
5. `cms.draft.ownership_orphaned_flagged` — FR-034a
6. `cms.draft.ownership_reassigned` — FR-034a
7. `cms.content.scheduled` — `draft → scheduled`
8. `cms.content.published` — `draft|scheduled → live` (banner / featured / FAQ / blog / legal)
9. `cms.content.archived` — `live → archived`
10. `cms.legal_page.version.superseded` — `live → superseded`
11. `cms.banner.cta_validated` — at publish
12. `cms.banner.cta_health_changed` — read-time staleness flip
13. `cms.banner.campaign_bound` — FR-023
14. `cms.banner.campaign_unbound` — FR-023
15. `cms.banner.scheduled_publish_blocked_capacity` — worker capacity hit
16. `cms.faq.reordered` — bulk-reorder bulk-update
17. `cms.preview_token.minted` — FR-014
18. `cms.preview_token.revoked` — FR-016
19. `cms.asset.swept` — FR-009a

The audit-log table is owned by spec 003 (append-only); 024 writes via `IAuditEventPublisher`.

---

## §6. Domain Events (in-process MediatR bus)

21 `INotification` records under `Modules/Shared/CmsContentDomainEvents.cs`. Each carries a stable shape: `{event_id, occurred_at_utc, actor_id?, entity_kind, entity_id, version_id, market_code, locale?, payload}`.

**Lifecycle events** (10):
1. `cms.banner.published`
2. `cms.banner.archived`
3. `cms.featured_section.published`
4. `cms.featured_section.archived`
5. `cms.faq.published`
6. `cms.faq.archived`
7. `cms.blog_article.published`
8. `cms.blog_article.archived`
9. `cms.legal_page.version.published`
10. `cms.legal_page.version.superseded`

**Operational events** (11):
11. `cms.featured_section.partial_broken`
12. `cms.featured_section.fully_broken`
13. `cms.banner.scheduled_publish_blocked_capacity`
14. `cms.banner.cta_target_broken`
15. `cms.cache.invalidate.banner`
16. `cms.cache.invalidate.featured_section`
17. `cms.cache.invalidate.faq`
18. `cms.cache.invalidate.legal_page`
19. `cms.cache.invalidate.blog_article`
20. `cms.draft.stale_alert`
21. `cms.draft.ownership_orphaned`

Plus 2 housekeeping events (counted in `cms.asset.dereferenced` + `cms.asset.swept`) emitted on the same bus but consumed only by 024's own GC worker.

**Subscribers**:
- spec 025 (notifications): cache-invalidate events + partial/fully-broken events + stale-alert events + ownership-orphaned events (Phase 1E wires the actual notification routing).
- spec 028 (analytics): all lifecycle events (publish-rate metrics, time-to-publish, archive-rate).
- spec 014 (storefront edge cache): cache-invalidate events (no-op until Phase 1E E1 ships the CDN).
- spec 007-b (campaigns): no consumer at V1 — 007-b only emits to 024 (`pricing.campaign.deactivated`); 024 is a sink, not a source, on this channel.

---

## §7. Cross-Module Read / Publish Contracts (declared under `Modules/Shared/`)

| Interface | Provenance | Consumed by 024 in |
|---|---|---|
| `ICatalogProductReadContract` | declared by spec 005 if shipped, else by 024 | featured-section ref resolution + banner-CTA validation |
| `ICatalogCategoryReadContract` | same | same |
| `ICatalogBundleReadContract` | same | same |
| `ICmsCampaignBindingPublisher` | declared by 024 | sink interface — spec 007-b's `pricing.campaign.deactivated` event delivery (in-process bus) |

Contract shape (representative):

```csharp
public interface ICatalogProductReadContract
{
    Task<CatalogProductRead> ReadAsync(Guid productId, string marketCode, CancellationToken ct);
}

public sealed record CatalogProductRead(
    Guid ProductId,
    string MarketCode,
    string DisplayNameAr,
    string DisplayNameEn,
    Guid? VendorId,
    bool IsAvailable,                // false when archived / soft-deleted / market-mismatched
    LinkedEntityUnavailableReason? UnavailableReason);

public enum LinkedEntityUnavailableReason
{
    Archived,
    SoftDeleted,
    MarketMismatched,
    NotFound,
}
```

024 calls `ReadAsync` in parallel (`Task.WhenAll`) for featured-section refs (up to 24) and for each banner CTA (1 per banner). Transient errors (timeout, 5xx) propagate as `OperationCanceledException` / framework exceptions — the storefront slice catches and applies fail-open (`cta_health=transient_unverified`) per FR-022a.

The `ICmsCampaignBindingPublisher` interface is the OUTBOUND publisher signature for 024's binding-related events (`cms.banner.campaign_bound`, `cms.banner.campaign_unbound`); spec 007-b's deactivation flow consumes via the in-process MediatR bus on the `pricing.campaign.deactivated` event delivered by spec 003's bus infrastructure (no direct dependency).

---

## §8. Reference data seeding (CmsReferenceDataSeeder)

On every environment (Dev / Staging / Production), idempotent:

1. `cms.market_schemas` rows for `EG`, `KSA`, `*` with V1 default values.

That's the only reference data; all content (banners, featured sections, FAQ, blog articles, legal page versions) is editor-authored. The Dev / Staging-only `CmsV1DevSeeder` (Phase S of plan.md) populates synthetic content with a `SeedGuard` that prevents production execution.

---

## §9. Per-row invariants & guards

- **Banner capacity** (FR-021a): the publish path computes `BannerCapacityCalculator.Count(slot_kind, market_code)` which sums (a) all `live` banners with `(market_code = $market AND state='live' AND now() ∈ scheduled window)` + (b) all `*`-scoped `live` banners in the same window. If `count + 1 > CmsMarketSchema.banner_max_live_per_slot`, reject with `400 cms.banner.slot_capacity_exceeded`.
- **FAQ display_order**: NOT unique (collisions allowed; ties broken by `created_at_utc ASC`). Bulk reorder uses xmin guard per affected row.
- **Blog slug**: `UNIQUE (market_code, authored_locale, slug)`. Slug pattern: `^[a-z0-9]+(-[a-z0-9]+)*$` (kebab-case lowercase).
- **Legal page version effective_at**: STRICT ASC ordering per `(legal_page_kind, market_code)`. The worker takes a `FOR UPDATE SKIP LOCKED` lock on the `(legal_page_kind, market_code)` partition before the supersede transition to prevent a concurrent publish double-superseding.
- **Asset reference recount** (FR-009a worker): a single SQL union across all 5 entity tables counts active references; the worker only sweeps when count = 0 AND age threshold met.
- **Preview token verification**: HMAC-SHA256 over `{entity_kind, entity_id, version_id, mint_timestamp_utc, ttl_seconds, actor_role_at_mint}` using a 256-bit server secret. Verify recomputes the HMAC and compares constant-time; rejects on mismatch with `403 cms.preview.token_signature_invalid`.

---

## §10. Forbidden / nullable patterns checklist

- ❌ `customer_id` columns on any CMS entity row — V1 forbids customer-supplied content (Principle 4 + Principle 25).
- ❌ DB-level FK to `catalog.products` / `catalog.categories` / `catalog.bundles` — cross-module pattern forbids it.
- ❌ Hard-delete on any non-`draft` row — `BEFORE DELETE` triggers reject.
- ❌ Mutable `created_at_utc` — once stamped, never updated.
- ❌ Arbitrary state writes — every transition routes through `CmsContentLifecycle.AssertCanTransition(from, to, kind, actor)` with a compile-time guard.
- ⚠️ Nullable `vendor_id` — present on every row but null in V1; populated only in Phase 2.
- ⚠️ Nullable `archived_at_utc` / `published_at_utc` / `superseded_at_utc` — null until the corresponding transition.
