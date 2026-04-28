# Research: CMS

**Phase**: 0 (input to plan.md)
**Date**: 2026-04-29

This document captures the design decisions taken during planning. Each item below either resolves a NEEDS-CLARIFICATION-style question or documents a non-obvious technology / pattern choice. The 5 spec-authoring clarifications + 5 `/speckit-clarify` clarifications already on `spec.md` are summarised here as decisions for cross-artifact traceability.

---

## R1 — Bilingual mandatory rule (Clarification — spec authoring)

**Decision**: Banner / featured section / FAQ / legal page version MUST carry both `ar` and `en` bodies at publish; blog articles MAY publish single-locale with explicit `available_locales[]` + `localization_unavailable_for_requested_locale` storefront flag.

**Rationale**: Principle 4 forbids machine-translation of long-form copy. Marketing surfaces (banners, featured sections, FAQ) are short / scripted enough to demand both locales editorially; legal pages are regulatory contracts where AR and EN MUST be precisely equivalent. Long-form blog editorial in two languages doubles author cost without proportional value — an AR-only "Ramadan dental tips" article is more useful than an AR original + a thin EN translation.

**Alternatives considered**: (a) bilingual mandatory across all kinds — rejected; over-burdens blog authoring. (b) single-locale with auto-translate fallback — explicitly forbidden by Principle 4. (c) per-market policy toggle — rejected; the rule is editorial, not operational.

**Implementation hooks**: `LocaleCompletenessGate.cs` is per-kind; the publish slice calls `gate.CheckPublishable(entityKind, ar, en)` before persisting the `live` transition.

---

## R2 — Indefinite legal page version retention (Clarification — spec authoring)

**Decision**: Legal page versions are append-only and never hard-deleted. Superseded versions remain queryable forever via the version-history endpoint. `archived` does NOT apply to legal page versions; the `superseded` terminal state replaces it.

**Rationale**: Principle 25 (audit + traceability) and Principle 17 (post-purchase contract integrity). A customer placing an order in 2026-Q3 agreed to the privacy policy live at that moment; if a 2027-Q1 dispute requires re-derivation of the contract terms, the 2026-Q3 version MUST be queryable. Pruning old versions is forbidden even by `super_admin`.

**Alternatives considered**: (a) bounded version history (e.g., last 10 versions) — rejected; legally required to retain ALL versions. (b) `super_admin`-pruning with audit — rejected; the audit-log entry would not preserve the body content. (c) cold-storage archive with thaw on demand — rejected as premature; expected version count per `(kind, market)` is ≤ 4 per year.

**Implementation hooks**: Postgres `BEFORE DELETE` trigger on `cms.legal_page_versions` rejects all delete attempts. The `cms.legal_page.version.delete_forbidden` reason code is returned by the API layer.

---

## R3 — Signed preview tokens with bounded TTL and revocation (Clarification — /speckit-clarify Q3)

**Decision**: Preview tokens are signed opaque HMAC-SHA256 strings backed by a `cms.preview_tokens` row. Default TTL 24 h (range 1 h – 7 d, configurable per env via `CmsMarketSchema.preview_token_default_ttl_hours`). Tokens are revocable; the token-store row is checked on every preview-read call. Token-store rows are retained 30 days post-expiry then daily-worker-deleted.

**Rationale**: Stakeholder review of unpublished content is an operational requirement, but a leaked URL on social media must be revocable. JWTs lose revocation (only TTL expiry); a stateful token store gives immediate revocation. HMAC over `{entity_kind, entity_id, version_id, mint_timestamp_utc, ttl_seconds, actor_role_at_mint}` keeps signing cheap, supports key rotation cleanly (rotate-key invalidates all in-flight tokens — the next read returns `403 cms.preview.token_signature_invalid`), and the signing key sits in Phase 1E E1 Key Vault per A1 layered-config rule (NEVER in `appsettings.json`).

**Alternatives considered**: (a) JWT-only — rejected; loses revocation. (b) IP-binding — rejected; stakeholders review from corporate VPN with rotating egress IPs. (c) RBAC-only preview (no public token) — rejected; stakeholders are not RBAC subjects. (d) Long-lived shareable URL — rejected; security incident risk.

**Implementation hooks**: `PreviewTokenSigner.cs` signs/verifies; `MintPreviewToken/` slice writes to `cms.preview_tokens`; `RevokePreviewToken/` slice updates `revoked_at_utc`; `ReadPreviewedDraft/` storefront slice checks signature + store + revocation + expiry on every read; `CmsPreviewTokenCleanupWorker` runs daily and hard-deletes rows ≥ 30 days past `expires_at_utc`.

---

## R4 — Live-resolve featured-section references (Clarification — /speckit-clarify Q4)

**Decision**: Featured sections persist `references[]` as id-only `{kind, id}` jsonb. At storefront read time, each reference is resolved via the corresponding `Modules/Shared/` catalog read contract (`ICatalogProductReadContract`, `ICatalogCategoryReadContract`, `ICatalogBundleReadContract`); broken refs are filtered out of the customer response with the response shape `{section_id, references_resolved, total_references, total_resolved, total_unavailable}`. Sections with `total_resolved == 0` return `omitted_due_to_unavailable_references=true` and emit `cms.featured_section.fully_broken`.

**Rationale**: Catalog data (price, name, image, in-stock state) goes stale within minutes; eager-copy on publish would require a re-resolve worker on every catalog change event AND still has a staleness window. Live-resolve is the only correctness-preserving option. The 300 ms p95 budget for a 24-ref section is tight but achievable via parallel `Task.WhenAll` resolution against the in-process MediatR handlers.

**Alternatives considered**: (a) eager-copy on publish — rejected; staleness. (b) re-resolve worker triggered by catalog change events — rejected; complex and still racy. (c) cached-resolved-snapshot with TTL — rejected; same staleness window concern; CDN `max-age=60` already provides a similar amortization.

**Implementation hooks**: `Storefront/ListFeaturedSections/Handler.cs` invokes the three read contracts in parallel per reference; broken refs surface as `linked_entity_unavailable`. Contracts are stubbed via `FakeCatalogProductReadContract` etc. for 024-tests-without-spec-005.

---

## R5 — Strict per-market isolation with manual duplicate-to-market (Clarification — spec authoring)

**Decision**: Each entity row carries a single `market_code` (one of `EG`, `KSA`, `*`). No automatic cascade between markets. Editors trigger an explicit "duplicate to market" action that copies bodies + metadata into a fresh draft scoped to the target market; the duplicate is NOT auto-published. Cross-market `*` is reserved for universal content; `*`-scoped legal page versions require `super_admin`.

**Rationale**: Per-market policy text (tax language, payment provider lists, COD-availability, holiday timings) varies enough that auto-cascade would silently misrepresent one market with the other's text. Manual duplication makes the editor explicitly review market-specific differences. `super_admin` gating on `*` legal pages reflects that one wrong `*` legal page misroutes the contract for both markets simultaneously — a higher-risk action than a per-market legal page.

**Alternatives considered**: (a) auto-cascade with editor opt-out — rejected; default-on creates implicit cross-market authoring. (b) shared bodies across markets — rejected; loses per-market editorial control. (c) per-market policy linkage — rejected; over-engineered for V1.

**Implementation hooks**: `Editor/DuplicateToMarket/` slice (named in plan but not surfaced as a separate phase since it's a thin wrapper over `SaveBannerDraft` etc.). `*` legal page publish goes through `SuperAdmin/PublishCrossMarketLegalPageVersion/`.

---

## R6 — Banner slot capacity cap with cross-market accounting (Clarification — /speckit-clarify Q1)

**Decision**: Up to `CmsMarketSchema.banner_max_live_per_slot` (V1 default 5; range 1–10) banners MAY be `live` per `(slot_kind, market_code, locale)` simultaneously. `*`-scoped banners count against every per-market cap they appear in (a `*`-scoped `hero_top` banner reduces effective EG capacity by 1 AND KSA by 1). Storefront response is the deterministic ordered list; carousel rendering is owned by spec 014.

**Rationale**: Capacity is delivered-banner-count (what the customer sees), not authored-banner-count. Marketers operate weekly rotations with typically 2–4 concurrent banners; 5 is comfortable headroom. Per-market `*` accounting (1 slot for `*` total) creates surprising capacity exhaustion when EG editors don't realize KSA already has 5; counting `*` everywhere is the predictable behaviour for the editor mental model "what does the customer see?".

**Alternatives considered**: (a) unbounded — rejected; home-page chaos. (b) single-banner per slot (`hero_top` mode) — rejected; carousel hero rotations are a normal marketing pattern. (c) carousel rotation logic shipped in 024 — rejected; rotation is presentation-layer concern owned by spec 014.

**Implementation hooks**: `BannerCapacityCalculator.cs` runs at publish-time (publish-now + scheduled-publish + worker-promotion). Worker-promotion that hits the cap leaves the row in `scheduled` and emits `cms.banner.scheduled_publish_blocked_capacity` for editor attention (rate-limited 1/banner/hour).

---

## R7 — Reference-counted asset cleanup with 7-day grace (Clarification — /speckit-clarify Q2)

**Decision**: When a draft is hard-deleted (FR-005a draft-delete path) or any entity transitions to `archived` / `superseded`, 024 emits a `cms.asset.dereferenced` event for each `asset_id` on the affected row. `CmsAssetGarbageCollectorWorker` runs daily, recounts references across ALL CMS entities in any state, and sweeps assets with zero remaining references AND ≥ `CmsMarketSchema.asset_grace_period_days` (V1 default 7 days; range 0–30) days past dereferencing. Sweep deletes the storage object via spec 015 storage abstraction; the `CmsAsset` metadata row is preserved with `storage_object_state=swept` (audit trail integrity).

**Rationale**: Indefinite retention grows storage cost monotonically. Immediate deletion is unsafe (an editor restoring an accidentally-archived banner needs the asset back). 7-day grace is the recoverability buffer; per-market configurable for privacy-incident response. Reference recount across all states (not just `live`) prevents prematurely sweeping an asset that's still on a draft of a different entity.

**Alternatives considered**: (a) indefinite retention — rejected; cost. (b) immediate deletion — rejected; ops safety. (c) manual `super_admin` sweep — rejected; doesn't scale. (d) 30-day grace — rejected; too long for privacy-incident response.

**Implementation hooks**: `CmsAssetGarbageCollectorWorker.cs` runs at 02:00 UTC daily (off-peak). Each asset's reference recount uses a single SQL query joining all 5 entity tables. Sweep is audited (one audit row per swept asset).

---

## R8 — Two-tier sort: specific-market first, then `*` (Clarification — /speckit-clarify Q3)

**Decision**: Storefront read endpoints use a two-tier sort: (a) all rows where `market_code` matches the request first; (b) all `*`-scoped rows second. Within each tier, the per-kind sort key applies (`priority_within_slot ASC`, `display_priority ASC`, `display_order ASC`, `published_at_utc DESC`).

**Rationale**: Editor's intent is "specific market wins"; a global `*` cookie banner outranking a market-specific Ramadan campaign would contradict the editor's most-specific-match expectation. Equal interleaving by priority silently demotes critical market campaigns when the `*` row was set to a higher numeric priority by mistake.

**Alternatives considered**: (a) priority-only interleaved — rejected; demotes specific-market intent. (b) `*` first — rejected; backwards. (c) market-only with `*` fallback when empty — too brittle (`*` content can't co-exist with market content). (d) per-market sort policy — over-engineered.

**Implementation hooks**: SQL `ORDER BY (CASE WHEN market_code = $market THEN 0 ELSE 1 END) ASC, priority_within_slot ASC, created_at_utc ASC` pattern; applied uniformly in the `StorefrontContentResolver` shared by all storefront slices.

---

## R9 — Banner CTA validation at BOTH publish-time and storefront-read-time (Clarification — /speckit-clarify Q4)

**Decision**: Banner `cta_kind ∈ {product, category, bundle}` MUST validate via `Modules/Shared/` catalog read contracts at publish-time (rejects with `400 cms.banner.cta_target_unresolvable` if unresolvable) AND at storefront-read-time (filters out banners with broken refs; emits `cms.banner.cta_target_broken` rate-limited 1/banner/hour). Transient catalog errors fail-open with `cta_health=transient_unverified` (banner included; storefront decides whether to suppress).

**Rationale**: Publish-time validation prevents shipping a broken banner. Read-time re-validation handles post-publish catalog churn. Fail-open on transient errors is the right call because suppressing customer-facing marketing during a catalog blip is worse than a possibly-stale CTA — the storefront layer can apply its own resilience policy on the `transient_unverified` flag.

**Alternatives considered**: (a) publish-time only — rejected; post-publish staleness. (b) read-time only — rejected; can ship broken banners. (c) eager-archive-on-deactivation worker — rejected; adds another worker for a problem already solvable read-time. (d) fail-closed on transient errors — rejected; hurts customer experience during catalog maintenance.

**Implementation hooks**: `Storefront/ListBannerSlots/Handler.cs` re-validates each catalog-bound CTA in parallel with featured-section reference resolution. The same `FakeCatalogProductReadContract` etc. doubles cover both pathways in tests.

---

## R10 — Stale-draft soft alerting; no auto-archive (Clarification — /speckit-clarify Q5)

**Decision**: Drafts older than `CmsMarketSchema.draft_staleness_alert_days` (V1 default 30; range 7–365) are flagged `stale=true` on admin-queue reads and surfaced via `cms.draft.stale_alert` events (rate-limited 1/draft/week, suppressible per draft via `dismiss-stale-alert` endpoint with `reason_note ≥ 10 chars`). Drafts whose owner's `cms.editor` role was revoked are flagged `ownership_orphaned=true`. NEVER auto-archives — drafts only transition by explicit editor / publisher / legal-owner action.

**Rationale**: Auto-archive of a slow-moving legal page draft, an embargoed-launch banner, or a long-research blog article is data loss without consent. Editors own content lifecycle (Principle 25). Soft alerts give ops a clear signal without forcing destructive automation. Ownership-orphan flag prompts a publisher to reassign or hard-delete (the FR-005a draft-delete path remains the only operational on-ramp for clearing genuinely-abandoned drafts).

**Alternatives considered**: (a) auto-archive after N days — rejected; data loss. (b) ownership-reassignment on offboarding — rejected; can't auto-pick a successor owner. (c) hard-purge after offboarding+grace — rejected; same data-loss concern. (d) no automation — rejected; admin queue clutter accumulates indefinitely without operational signal.

**Implementation hooks**: `CmsStaleDraftAlertWorker.cs` runs at 03:00 UTC daily. Subscribes to spec 004's `customer.role_revoked` channel via `EditorRoleRevokedHandler.cs` for the orphan flag.

---

## R11 — Banner ↔ campaign binding via append-only `BannerCampaignBinding` rows

**Decision**: A banner-slot version's binding to a 007-b campaign is captured in a `cms.banner_campaign_bindings` row (banner_id, version_id, campaign_id, bound_at_utc, released_at_utc?, binding_state). Bindings are append-only — releasing creates a `released_at_utc` stamp + `binding_state=released_*` rather than deleting the row. 024 subscribes to spec 007-b's `pricing.campaign.deactivated` event and auto-releases any active binding for the deactivated campaign.

**Rationale**: A banner may be bound and unbound multiple times across its lifecycle (campaign A → unbind → campaign B); a single FK column on the banner row would lose history. Append-only binding rows preserve the audit trail. Auto-release on campaign deactivation prevents orphaned bindings without forcing the editor to manually unbind every campaign that ends.

**Alternatives considered**: (a) FK column on banner row — rejected; loses history. (b) full sync RPC `Get currently-active campaign for banner X` — rejected; tight coupling to 007-b. (c) no auto-release on deactivation (manual only) — rejected; predictable orphan accumulation.

**Implementation hooks**: `CampaignDeactivatedHandler.cs` consumes `pricing.campaign.deactivated` and stamps the matching `BannerCampaignBinding` row with `released_at_utc` + `binding_state=released_due_to_campaign_deactivation`. `Publisher/ArchiveContent/` rejects with `409 cms.banner.archive_blocked_by_campaign_binding` when an active binding exists.

---

## R12 — Worker idempotency strategy (FR-011, FR-024–FR-025, FR-034a parallels)

**Decision**: All four workers are idempotent on a stable key:
- `CmsScheduledPublishWorker`: `(entity_kind, entity_id, target_state)` + xmin row_version check before transition.
- `CmsAssetGarbageCollectorWorker`: `asset_id` + `storage_object_state` precondition (only sweeps `active` rows; sets to `swept`).
- `CmsStaleDraftAlertWorker`: `(draft_id, alert_window_start_utc)` (alert window is the rate-limit boundary).
- `CmsPreviewTokenCleanupWorker`: `token_hash` + `expires_at_utc + 30 days < now()` precondition.

Workers use the existing Postgres advisory-lock pattern from spec 020 to coordinate horizontally; the lock key is the worker class name. Re-running a worker on the same row in the same target state MUST NOT emit duplicate audit rows or duplicate domain events.

**Rationale**: Horizontal pod scaling + crash-recovery would double-fire workers without idempotency. SC-005 verifies idempotency under a 100-iteration repeat-worker stress test.

**Implementation hooks**: Each worker's `ProcessAsync` method opens a transaction, takes the advisory lock, runs the per-row idempotent transition with an xmin precondition, commits. Emit-event happens AFTER commit (outbox pattern via spec 003's `IDomainEventBuffer`).

---

## R13 — Storefront leak prevention via shared `StorefrontContentResolver` (FR-005, SC-003)

**Decision**: All five storefront read endpoints route through a single `StorefrontContentResolver` that applies the live + scheduling-window + market+locale tier-sort filters at the EF query level (using `IQueryable<T>` extension methods). Application-side post-filtering is forbidden because it leaks if the query is misused. Leak-detection tests seed every non-`live` state across all 5 entity kinds and assert zero leakage on every storefront endpoint.

**Rationale**: SC-003 demands 0% leakage. The cleanest way to guarantee this is to make the safe filter the only reachable path for storefront reads. A single resolver class also keeps the two-tier sort logic centralised — every endpoint inherits the rule for free.

**Alternatives considered**: (a) per-handler filter logic — rejected; copy-paste leak risk. (b) row-level security via Postgres RLS — over-engineered; the filter is simple and EF-tractable.

**Implementation hooks**: `StorefrontContentResolver.cs` exposes `IQueryable<T> ApplyStorefrontFilter<T>(IQueryable<T> source, string marketCode, string locale)` where `T : ICmsContentRow`. Every storefront read slice composes its query through this method.

---

## R14 — `vendor_id` slot on every entity row, never populated in V1 (Principle 6)

**Decision**: Every CMS entity table carries a `vendor_id UUID NULL` column; indexed for future `WHERE vendor_id = ?` reads. V1 always null in admin UI; populated only when an entity is vendor-scoped, which is V1-default-NULL for all kinds.

**Rationale**: P6 multi-vendor-readiness without paying schema-migration cost in Phase 2. Same pattern as specs 020 / 021 / 022 / 023 / 007-b.

**Alternatives considered**: omit column — rejected; would force a migration touching every CMS row in Phase 2.

**Implementation hooks**: All EF entity configurations include `vendor_id` as nullable indexed; the admin UI hides the column at V1; the storefront filter will gain a `vendor_id IS NULL OR vendor_id = ?` clause in Phase 2.

---

## R15 — Storefront cache strategy: TTL-based in V1, edge-cache events forward-compatible (FR-020)

**Decision**: Storefront responses include `Cache-Control: public, max-age=60, stale-while-revalidate=300` and a stable `ETag` derived from the response payload hash. Cache-invalidate events (`cms.cache.invalidate.banner` etc.) are emitted on every publisher action from V1 but consumed by spec 025's edge-cache subscriber only when 1E E1 ships the CDN.

**Rationale**: TTL-based caching is the V1 cache strategy. Emitting events from V1 unblocks the 1E subscribers without retrofitting CMS publisher slices later. The 60 s `max-age` keeps origin pressure low while bounding staleness.

**Alternatives considered**: (a) no caching — rejected; storefront read load. (b) longer TTL — rejected; staleness on banner rotations. (c) CDN purge wired in V1 — rejected; depends on Phase 1E E1 infrastructure.

**Implementation hooks**: `CmsScheduledPublishWorker` and `Publisher/*` slices emit `cms.cache.invalidate.{kind}` after every state transition; the events are no-op subscribed in V1 (recorded as "would-have-purged" log entries).

---

## R16 — Idempotency-key envelope on every state-transitioning POST (FR-033)

**Decision**: Every state-transitioning POST (draft create, draft update, publish-now, schedule-publish, archive, bind-banner-to-campaign, unbind, mint-preview-token, revoke-preview-token, dismiss-stale-alert, edit-market-schema) requires an `Idempotency-Key` header. The spec 003 platform middleware short-circuits duplicate requests within 24 h to return the original 200 response.

**Rationale**: Network-flaky double-taps from the admin UI must not create duplicate drafts, double-publish (race against the cap), or double-revoke a preview token. Spec 003 owns the middleware; 024 just declares the requirement on the routes.

**Implementation hooks**: `[RequireIdempotencyKey]` attribute on every state-transitioning endpoint; idempotency conflicts return `409 cms.idempotency_key_conflict` with the original response payload.

---

## R17 — Single-locale blog articles with explicit `available_locales[]` (FR-007 exception)

**Decision**: Blog articles MAY publish single-locale (one of `ar` or `en`). The storefront response carries `available_locales[]` and `localization_unavailable_for_requested_locale` flag when the requested locale doesn't match the authored locale.

**Rationale**: Long-form blog editorial in two languages doubles author cost without proportional value. The flag lets spec 014 render a "this article is not available in {locale}" placeholder linking to the available-locale version, preserving Principle 4's no-machine-translation rule while keeping single-locale articles useful.

**Alternatives considered**: bilingual mandatory across all kinds (R1 alternative).

**Implementation hooks**: `LocaleCompletenessGate.CheckPublishable(EntityKind.BlogArticle, ...)` returns OK with one locale present; the storefront `GetBlogArticle/Handler.cs` populates `available_locales[]` from the entity row.

---

## R18 — Polymorphic `references` jsonb on featured sections (vs typed FKs)

**Decision**: Featured sections persist `references` as a jsonb array of `{kind: 'product'|'category'|'bundle', id: UUID}`. No DB-level FK to catalog tables. Resolution is logical-only via `Modules/Shared/` read contracts.

**Rationale**: Three nullable FK columns on every row would proliferate noise and break the cross-module pattern from specs 020 / 021 / 022 / 023 (all polymorphic links). Postgres jsonb operators support per-kind queries (`references @> '[{"kind":"product"}]'`) without typed columns.

**Alternatives considered**: typed FK columns (rejected, see above), join-table `cms_featured_section_references` with discriminator (rejected, EF migrations multiply, query complexity rises).

**Implementation hooks**: `FeaturedSection.References` is `JsonDocument` mapped via EF's `HasColumnType("jsonb")`. Validation gates `kind ∈ {product, category, bundle}` at save-time.

---

## R19 — Public storefront read endpoints with per-IP rate-limit (FR-031 + Principle 3)

**Decision**: All five storefront read endpoints + the preview-token read endpoint are unauthenticated. Rate-limited per IP + per `entity_kind` (V1 default 600 req/min/IP, configurable per env). Admin authoring endpoints all require auth + RBAC.

**Rationale**: Principle 3 — "unauthenticated users MAY browse" — auth on storefront reads breaks the constitutional browse rule (anonymous home views, anonymous FAQ reads, anonymous legal page reads). Rate-limit prevents enumeration / scraping abuse without forcing auth.

**Alternatives considered**: (a) auth-required storefront — rejected; constitutional violation. (b) no rate limit — invites scraping. (c) per-customer rate limit — anonymous customers have no `customer_id`.

**Implementation hooks**: `[AllowAnonymous]` on every storefront slice; `[EnableRateLimiting("cms-storefront")]` policy configured at module bootstrap. Preview-token read uses a tighter per-IP limit (60 req/min) since it's stakeholder-only.

---

## R20 — System-generated AR + EN ICU keys; AR strings flagged for editorial review (Principle 4)

**Decision**: Every system-generated operator-visible string (state labels, category labels, validation badges, broken-CTA flags, stale-draft alerts, JSON-LD scaffolding) MUST have both `ar` and `en` ICU keys. AR strings flagged in `AR_EDITORIAL_REVIEW.md`. Customer-facing entity content is editor-authored; 024 MUST NOT auto-translate.

**Rationale**: Principle 4 — "Arabic quality MUST be editorial-grade, not machine-translated". The split between system chrome (bilingual mandatory, ICU-keyed, editorially reviewed) and customer-authored content (one locale per body, never auto-translated) is the same pattern used by specs 020 / 021 / 022 / 023.

**Implementation hooks**: `Modules/Cms/Messages/cms.en.icu` + `cms.ar.icu` carry every system-generated key; `AR_EDITORIAL_REVIEW.md` lists every AR key + an editorial-review checkbox. SC-008 gates the AR review at DoD.

---

## Decisions to defer (NOT input to this plan)

- **Edge-cache invalidation wiring**: The `cms.cache.invalidate.{kind}` events are emitted from V1 but consumed only when Phase 1E E1 ships the CDN. The cache architecture is owned by spec 025 + Phase 1E E1.
- **Vendor-scoped CMS authoring**: `vendor_id` slot reserved; admin UI does NOT expose vendor scoping in V1. Phase 2 marketplace work owns the activation.
- **Edit history of body content (revisions)**: V1 captures only the published version + the current draft. Revision history (rollback to a prior draft, diff-view between versions) is Phase 1.5.
- **Multi-step approval workflow beyond editor → publisher**: V1 ships single approval gate. Phase 1.5 may layer review-board approvals on top.
- **A/B testing of banner / featured-section variants**: Phase 1.5; V1 publishes one version per `(slot, market, locale, version)` and serves it to all customers.
- **Bulk import of FAQ / blog from CSV / JSON**: Phase 1.5; the seeder covers the bulk-load V1 path for Staging only.
- **Storefront-side timezone display of scheduled content**: Admin UI converts editor's local timezone to UTC at save (display convenience only); storefront responses carry UTC. Timezone display on the storefront is owned by spec 014.
