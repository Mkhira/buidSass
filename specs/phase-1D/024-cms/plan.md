# Implementation Plan: CMS

**Branch**: `phase_1D_creating_specs` (working) · target merge: `024-cms` | **Date**: 2026-04-29 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1D/024-cms/spec.md`

## Summary

Deliver the Phase-1D CMS module that turns Principle 16 ("Admin-managed CMS is part of V1; banners, featured sections, blog and educational content, guides, FAQ, legal pages, SEO pages") into a single backend module covering all 8 deliverable items from the implementation plan plus the 10 clarifications already resolved on the spec (5 from authoring + 5 from `/speckit-clarify`):

1. **Five-entity unified lifecycle** (Principle 24): banner slots, featured sections, FAQ entries, blog articles, and legal page versions all share a 4-state machine `draft → scheduled → live → archived`; legal page versions add a `superseded` terminal state when a newer effective version reaches `live`. Encoded in `CmsContentLifecycle.cs` with compile-time transition guards. Hard-delete on any non-draft state forbidden (FR-005a).
2. **Bilingual + RTL editorial** (Principle 4, FR-007, FR-008): `ar` + `en` bodies mandatory for banner / featured section / FAQ / legal page; blog articles allowed single-locale with a `localization_unavailable_for_requested_locale` storefront flag. No machine translation. Admin authoring UI switches to RTL on `ar`.
3. **Per-market scoping with `*` cross-market fallback** (Principle 5, FR-021): two-tier sort — specific-market rows always rank ahead of `*`-scoped rows; legal page storefront read is single-row substitution (specific market → `*` → 404). Cross-market `*` legal pages require `super_admin`. Manual "duplicate to market" for editor convenience; no auto-cascade.
4. **Banner slot capacity + CTA validation** (FR-021a, FR-022a): up to `CmsMarketSchema.banner_max_live_per_slot` (V1 default 5; range 1–10) `live` banners per `(slot_kind, market_code, locale)`; `*`-scoped banners count against every per-market cap they appear in. Banner `cta_kind ∈ {product, category, bundle}` validates the target via `Modules/Shared/` catalog read contracts at publish-time AND storefront-read-time; unresolvable targets reject publish (`400 cms.banner.cta_target_unresolvable`) and are filtered out of storefront responses with `cms.banner.cta_target_broken` admin alerts (rate-limited 1/banner/hour); transient catalog errors fail-open with `cta_health=transient_unverified`.
5. **Featured section live-resolved references** (FR-019, FR-022): id-only references (`product`, `category`, `bundle`); resolved via `Modules/Shared/` catalog read contracts at storefront-read-time; broken refs filtered silently with response shape `{section_id, references_resolved:[...], total_references, total_resolved, total_unavailable}`; `cms.featured_section.partial_broken` / `fully_broken` events emitted (rate-limited 1/section/hour).
6. **Scheduled publish worker + idempotency** (FR-010, FR-011): `CmsScheduledPublishWorker` runs on a 60 s cadence; promotes `scheduled → live` (banners/featured/FAQ/blog/legal) and `live → archived` (banners on `scheduled_end_utc`); transitions are idempotent on `(entity_kind, entity_id, target_state)`. Legal-page `scheduled → live` is a single transaction that ALSO transitions the prior `live` version of the same `(legal_page_kind, market_code)` to `superseded` with `superseded_at_utc` + `superseded_by_version_id`.
7. **Indefinite legal page version retention** (FR-005a, Clarification Q2): legal page versions never hard-deleted; superseded versions remain queryable forever via the version-history endpoint (regulatory + dispute-trace requirement). Banner / featured / FAQ / blog also never hard-deleted post-`draft` — `archived` is the only "removal" path. Drafts that never published MAY be hard-deleted by creator / `super_admin` (FR-005a draft-delete path; rate-limited 30/hour/actor).
8. **Reference-counted asset cleanup with 7-day grace** (FR-009a, Clarification Q2 of /speckit-clarify): `CmsAssetGarbageCollectorWorker` runs daily; assets dereferenced (draft hard-deleted OR entity transitioned to `archived` / `superseded`) AND with zero remaining references AND ≥ `CmsMarketSchema.asset_grace_period_days` days past dereferencing are deleted via spec 015 storage abstraction; the `CmsAsset` metadata row is preserved with `storage_object_state=swept`. The grace window is per-market (V1 default 7 days; range 0–30).
9. **Signed preview tokens with bounded TTL** (FR-014–FR-016, Clarification Q3 of /speckit-clarify): editors mint signed opaque HMAC-SHA256 tokens (default TTL 24 h, range 1 h – 7 d); tokens grant unauthenticated read access to draft entity render with `X-Robots-Tag: noindex, nofollow` and a "DRAFT" banner injection; revocable; token-store rows retained 30 days post-expiry then daily-worker deleted (the only hard-delete path for storage objects besides FR-009a asset sweep).
10. **Stale-draft soft alerting; no auto-archive** (FR-034a, Clarification Q5 of /speckit-clarify): `CmsStaleDraftAlertWorker` runs daily; flags drafts > `CmsMarketSchema.draft_staleness_alert_days` (V1 default 30; range 7–365) and emits `cms.draft.stale_alert` events (rate-limited 1/draft/week, suppressible per draft via dismiss-stale-alert endpoint). Drafts whose owner's `cms.editor` role was revoked (subscribed via spec 004's `customer.role_revoked` channel) flagged `ownership_orphaned=true`. NEVER auto-archives — drafts only transition by explicit editor / publisher / legal-owner action.
11. **Banner ↔ campaign binding with auto-release** (FR-023): a banner-slot version may be bound to a 007-b campaign for hero placement; binding is captured in `BannerCampaignBinding`; 024 subscribes to spec 007-b's `pricing.campaign.deactivated` event and auto-releases the binding. Banner archive on a banner with active binding rejects with `409 cms.banner.archive_blocked_by_campaign_binding` until the campaign deactivates or an editor manually unbinds.
12. **SEO + JSON-LD support** (FR-028, FR-029): blog articles carry a mandatory `seo` block (`meta_title`, `meta_description`, `og_image_id`, `schema_org_kind`); other entity kinds carry an optional `seo` block; storefront emits JSON-LD for legal pages + blog articles (storefront responsibility, owned by spec 014).
13. **Multi-vendor readiness** (Principle 6): `vendor_id` slot reserved on every CMS entity row; never populated in V1.
14. **`cms-v1` seeder**: idempotent; populates ≥ 1 row in each of `draft`, `scheduled`, `live`, `archived` states across all 5 entity kinds; bilingual coverage for the 4 dual-locale-mandatory kinds; per-market + cross-market `*` examples; legal page version-history with 2 versions per `(kind, market)`.

No customer-facing UI ships in this spec. Customer storefront is owned by Phase 1C spec 014; admin authoring UI is owned by spec 015. 024 ships only the backend authoring + storefront read contracts and seeders against which 014 / 015 build their screens.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16 (per ADR-022 + ADR-010).

**Primary Dependencies**:

- `MediatR` v12.x + `FluentValidation` v11.x — vertical-slice handlers (ADR-003).
- `Microsoft.EntityFrameworkCore` v9.x — code-first migrations on the new `cms` schema (ADR-004).
- `Microsoft.AspNetCore.Authorization` (built-in) — `[RequirePermission("cms.*")]` attributes from spec 004's RBAC.
- `Modules/AuditLog/IAuditEventPublisher` (existing) — every state transition + every authoring save + every preview-token mint/revoke + every reorder bulk-update + every banner-campaign binding/unbinding + every redaction-style asset sweep.
- `Modules/Identity` consumables — RBAC primitives + new permissions `cms.editor`, `cms.publisher`, `cms.legal_owner`. The existing customer-role-lifecycle subscriber from spec 004 is reused for the FR-034a `ownership_orphaned` flag.
- `Modules/Shared/IAuditEventPublisher`, `Modules/Shared/AppDbContext` — existing; reused.
- `Modules/Shared/Storage/IStorageObjectStore` (existing, owned by spec 015) — reused for signed-URL upload + retrieval + sweep.
- New shared interfaces declared under `Modules/Shared/` (see Project Structure):
  - `ICatalogProductReadContract` — already declared under spec 005 if it has shipped; otherwise newly declared here. Read for product references in featured sections + banner CTAs.
  - `ICatalogCategoryReadContract` — same provenance pattern.
  - `ICatalogBundleReadContract` — same provenance pattern.
  - `ICmsCampaignBindingPublisher` — declared here; spec 007-b consumes if it needs to know about cms-side binding/unbinding (V1 — events only; spec 007-b reads bindings via the event stream, not a sync RPC).
  - `CmsContentDomainEvents.cs` — 21 `INotification` records subscribed by spec 025 (notifications) and spec 028 (analytics).
- `MessageFormat.NET` (already vendored by spec 003) — ICU AR/EN keys for every operator-visible reason code, state label, category label, validation badge, JSON-LD scaffold.
- `System.Security.Cryptography.HMACSHA256` (BCL) — preview-token signing; signing key sourced from spec 015's layered configuration / Phase 1E E1 Key Vault (NOT in `appsettings.json`).

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). New `cms` schema; **9 net-new tables**:

- `cms.banner_slots` — versioned banner rows; carries lifecycle, scheduling window, slot_kind, per-locale assets, CTA, `market_code`, `priority_within_slot`, `vendor_id`, `cta_health`.
- `cms.featured_sections` — versioned featured-section rows; carries lifecycle, `section_kind`, bilingual title/subtitle, `references` (jsonb array of `{kind, id}`), `display_priority`, `market_code`, `vendor_id`.
- `cms.faq_entries` — versioned FAQ rows; bilingual question/answer; `category` (8 fixed values); `display_order`; `market_code`; `vendor_id`.
- `cms.blog_articles` — versioned blog rows; markdown body; `slug` unique per `(market_code, authored_locale)`; `seo` jsonb; `category` (6 fixed values); `cover_asset_id`; `vendor_id`.
- `cms.legal_page_versions` — append-only versioned legal page rows per `(legal_page_kind, market_code)`; both `body_ar` + `body_en` mandatory; `effective_at_utc`; `version_label`; lifecycle including `superseded` terminal state.
- `cms.assets` — asset metadata rows: storage_object_id (FK-style logical reference to spec 015 storage), MIME, size, intended-locale, original_filename, `storage_object_state` ∈ {`active`, `swept`} (preservation of metadata on FR-009a sweep).
- `cms.preview_tokens` — minted preview tokens: `token_hash` (sha256 of opaque token), `entity_kind`, `entity_id`, `version_id`, `minted_by_actor_id`, `minted_at_utc`, `expires_at_utc`, `revoked_at_utc?`, `actor_role_at_mint`. Daily-worker-deleted at ≥ 30 days past `expires_at_utc`.
- `cms.banner_campaign_bindings` — banner ↔ 007-b campaign links: `banner_id`, `version_id`, `campaign_id`, `bound_at_utc`, `released_at_utc?`, `binding_state` ∈ {`active`, `released_due_to_campaign_deactivation`, `released_by_editor`}.
- `cms.market_schemas` — per-market policy row: `market_code` (PK), `banner_max_live_per_slot`, `featured_section_max_references`, `preview_token_default_ttl_hours`, `draft_staleness_alert_days`, `asset_grace_period_days`. Edits restricted to `super_admin`.

State writes use EF Core optimistic concurrency via Postgres `xmin` mapped as `IsRowVersion()` (project pattern from specs 020 / 021 / 022 / 023 / 007-b) for concurrent draft edits, concurrent FAQ reorder, concurrent banner-slot capacity check, and concurrent legal page version publish. Append-only rows (`legal_page_versions` past `live`, `preview_tokens` post-mint, `assets` rows) are guarded by Postgres `BEFORE UPDATE OR DELETE` triggers with controlled exceptions for the FR-009a `storage_object_state=swept` transition + the FR-016 daily preview-token cleanup.

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers Postgres (per spec 003 contract — no SQLite shortcut). Contract tests assert HTTP shape parity between every `spec.md` Acceptance Scenario and the live handler. Property tests for state-machine invariants (no `archived → *`, no `superseded → *`, no hard-delete on post-draft rows). Concurrency tests for FR-021a (concurrent banner publishes hitting capacity cap), FR-019a tests for FAQ reorder race (xmin guard), legal-page version publish race (xmin guard). Storefront leak-detection tests for SC-003 (zero leakage of non-`live` content). Time-driven tests use `FakeTimeProvider` to advance the 4 workers (`CmsScheduledPublishWorker`, `CmsAssetGarbageCollectorWorker`, `CmsStaleDraftAlertWorker`, `CmsPreviewTokenCleanupWorker`). Cross-module catalog read contracts are stubbed via 3 `Fake*ReadContract` doubles so 024 tests run without spec 005 at DoD on `main`. Idempotency tests assert FR-033 envelope (every state-transitioning POST requires `Idempotency-Key`). Capacity-cap perf test seeds 1 000 live banners and validates SC-006's 200 ms p95 budget. Featured-section resolution perf test seeds 10 000 catalog products and validates SC-007's 300 ms p95 budget for a 24-reference section.

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — Phase 1C specs 014 / 015 deliver UI.

**Project Type**: .NET vertical-slice module under the modular monolith (ADR-023). Net-new top-level module: `Modules/Cms/`.

**Performance Goals**:

- **Storefront banner-slot read**: p95 ≤ 200 ms (50-row page; SC-006). With CDN cache `Cache-Control: public, max-age=60, stale-while-revalidate=300`, origin pressure is small.
- **Storefront featured-section read with refs**: p95 ≤ 300 ms (24 references resolved via catalog contracts; SC-007). Resolution is parallelised across references.
- **Storefront FAQ read**: p95 ≤ 150 ms.
- **Storefront blog-article read**: p95 ≤ 250 ms (markdown rendered server-side at storefront layer; CMS returns raw markdown + SEO block).
- **Storefront legal-page read**: p95 ≤ 200 ms (single-row substitution).
- **Admin draft create / save**: p95 ≤ 500 ms (validation + persist + audit).
- **Admin publish (publish-now)**: p95 ≤ 800 ms (locale-completeness check + capacity cap check + CTA validation contract calls + cache invalidation event emit).
- **Admin archive**: p95 ≤ 500 ms.
- **Preview-token mint**: p95 ≤ 200 ms.
- **Preview-token verified read**: p95 ≤ 250 ms (token verification + draft render).
- **Worker tick latency** (`CmsScheduledPublishWorker`): p95 ≤ 60 s from `scheduled_start_utc` (SC-004).
- **Featured-section bulk-reorder**: p95 ≤ 500 ms for ≤ 50 entries.
- **Concurrent draft-save resolution**: deterministic; one winner per xmin race (SC-010).

**Constraints**:

- **Idempotency** (FR-033): every state-transitioning POST endpoint requires `Idempotency-Key` (per spec 003 platform middleware); duplicates within 24 h return the original 200 response.
- **Concurrency guard**: every state-transitioning command uses an EF Core `RowVersion` (xmin) optimistic-concurrency check; the loser sees `409 cms.{entity_kind}.version_conflict` (or its FAQ-reorder analog `409 cms.faq.reorder_conflict` and the legal-page analog `409 cms.legal_page.version_conflict`).
- **Hard-delete prohibition** (FR-005a): the API layer MUST return `405 cms.{entity_kind}.delete_forbidden` for any `DELETE /v1/admin/cms/{kind}/{id}` route on non-draft rows. Append-only tables (`legal_page_versions` post-`live`, `preview_tokens` post-mint except daily cleanup, `assets` except FR-009a sweep state transition) MUST be guarded by Postgres `BEFORE UPDATE OR DELETE` triggers with controlled exceptions.
- **Storefront leak-prevention** (FR-005, SC-003): every storefront read endpoint enforces the filter `state=live AND (scheduled_start_utc IS NULL OR scheduled_start_utc <= now()) AND (scheduled_end_utc IS NULL OR scheduled_end_utc > now())` at the EF query level — never relies on application-side post-filter, which is leak-prone if the query is misused. Verified by SC-003 leak-detection tests.
- **Public storefront reads**: all five storefront read endpoints + the preview-token read endpoint are unauthenticated. Rate-limited per IP + per `entity_kind` (V1 default 600 req/min/IP, configurable) to mitigate enumeration / scraping. Admin authoring endpoints all require auth + RBAC.
- **PII at rest**: CMS entity bodies are editor-authored marketing copy and MUST NOT contain customer PII; the seed-pii-guard CI check applies. Asset uploads MUST NOT include customer-uploaded content (CMS assets are editor-uploaded only); customer-supplied content lives in spec 022 reviews / spec 023 support tickets, NOT in CMS.
- **Time source**: every state transition + every scheduled-window check + every TTL check + every rate-limit window reads `TimeProvider.System.GetUtcNow()`; tests inject `FakeTimeProvider`.
- **Worker idempotency**: `CmsScheduledPublishWorker` is idempotent on `(entity_kind, entity_id, target_state)`; `CmsAssetGarbageCollectorWorker` is idempotent on `asset_id`; `CmsStaleDraftAlertWorker` is idempotent on `(draft_id, alert_window_start_utc)`; `CmsPreviewTokenCleanupWorker` is idempotent on `token_hash`. Workers use the existing Postgres advisory-lock pattern from spec 020 to coordinate horizontally.
- **Bilingual editorial** (Principle 4): every system-generated operator-visible string (state labels, category labels, validation badges, broken-CTA flags, stale-draft alerts) MUST have both `ar` and `en` ICU keys; AR strings flagged in `AR_EDITORIAL_REVIEW.md`. Customer-facing entity content is editor-authored; 024 MUST NOT auto-translate.
- **Locale registry**: 024 reads supported locales from spec 003's market+localization registry at storefront read time; `?locale=fr` (or any locale outside the registry) returns `400 cms.storefront.locale_unsupported`.
- **Preview-token signing**: HMAC-SHA256 over `{entity_kind, entity_id, version_id, mint_timestamp, ttl_seconds, actor_role_at_mint}` using a server-side secret (256-bit). Secret rotated per Phase 1E E1 Key Vault policy; rotation MUST gracefully invalidate all in-flight tokens (the next preview-read call returns `403 cms.preview.token_signature_invalid`); secret is NEVER stored in `appsettings.json` per A1 layered-config rule.

**Scale/Scope**: ~25 HTTP endpoints (admin authoring across 5 kinds: ~15; preview tokens: 3; storefront read: 5; metrics: 1; lookups: 1 — every admin endpoint is authenticated). **39 functional requirements** (FR-001–FR-034 with FR-005a, FR-009a, FR-021a, FR-022a, FR-034a interleaved). 11 SCs. 8 key entities + `CmsMarketSchema` policy table. 1 four-state lifecycle (+ `superseded` terminal for legal). 9 net-new tables. 4 hosted workers. **21 lifecycle + operational domain events**. 5 entity kinds. 4 banner slot kinds, 4 featured-section kinds, 8 FAQ categories, 6 blog categories, 4 legal page kinds. Target capacity at V1 launch: ~50 banners live across both markets, ~30 featured sections, ~120 FAQ entries, ~50 blog articles, ~16 legal page versions in force; admin-side ~10 editors authoring concurrently; storefront ~5 000 reads/min peak (CDN-buffered).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Storefront reads are public unauth (consistent with "unauthenticated users MAY browse" — banners, FAQ, legal, blog are browse-eligible). Admin authoring requires authentication + RBAC. Preview tokens grant unauthenticated time-bounded read for stakeholder review without bypassing admin auth. | PASS |
| P4 Arabic / RTL editorial | Banner / featured / FAQ / legal page versions REQUIRE both `ar` and `en` bodies at publish (FR-007). Blog articles single-locale allowed (Principle 4 forbids machine-translation of long-form copy; explicit `available_locales[]` + `localization_unavailable_for_requested_locale` flag). System-generated strings ICU-keyed AR + EN. AR-locale admin authoring screen render verified in SC-008. | PASS |
| P5 Market Configuration | `market_schemas` rows hold every per-market knob (`banner_max_live_per_slot`, `asset_grace_period_days`, `draft_staleness_alert_days`, `preview_token_default_ttl_hours`, `featured_section_max_references`). Per-market `market_code` + `*` cross-market scope. No hardcoded EG/KSA branches. | PASS |
| P6 Multi-vendor-ready | `vendor_id` slot reserved on every CMS entity row. V1 always null in admin UI; indexed for future-vendor-scoped reads. | PASS |
| P7 Branding (palette) | N/A in backend module. Asset upload is provider-side; design tokens are owned by spec 003. | PASS (no scope) |
| P10 Pricing | N/A directly. Banner-slot ↔ campaign binding (FR-023) lets 007-b authors place a campaign hero in a banner slot — pricing logic remains in 007-b; 024 only owns the banner authoring surface and binding lifecycle. | PASS |
| P16 CMS | Direct constitutional spec — banners, featured sections, blog and educational content, FAQ, legal pages, SEO pages all delivered. Customer-facing screens consume dynamic CMS content via storefront reads. Constitutional core. | PASS |
| P17 Order & post-purchase | Legal pages owned here are the contracts in force at order time — refund / dispute traceability requires indefinite version retention (FR-005a). FAQ feeds the customer Help surface (spec 023). | PASS |
| P19 Notifications | 21 domain events declared; spec 025 subscribes (cache-invalidate, partial-broken, fully-broken, stale-alert, ownership-orphaned events) + spec 028 subscribes for analytics (publish / archive lifecycle). No in-line notification calls (FR-025). | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9, MediatR — no deviation. | PASS |
| P23 Architecture | New vertical-slice module `Modules/Cms/`; reuses existing seams (`IAuditEventPublisher`, RBAC, storage abstraction, advisory-lock worker pattern). No premature service extraction. | PASS |
| P24 State Machines | One explicit content lifecycle (`ContentLifecycleState`, 4 states + `superseded` terminal for legal page versions) documented in `data-model.md §3` with allowed states, transitions, triggers, actors, failure handling. Reopen path forbidden — `archived` is terminal soft state. | PASS |
| P25 Audit | Every state transition + every draft save + every publisher / legal-owner action + every preview-token mint / revoke + every reorder bulk-update + every banner-campaign binding / unbinding + every asset sweep emits an audit row (FR-026, FR-027). SC-002 verifies end-to-end. | PASS |
| P27 UX Quality | No UI here, but error payloads carry stable reason codes (`cms.publish.locale_completeness_missing`, `cms.banner.slot_capacity_exceeded`, `cms.banner.cta_target_unresolvable`, `cms.draft.version_conflict`, `cms.legal_page.cross_market_requires_super_admin`, etc.) for spec 014 / 015 to render. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request / response / errors / reason codes. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1D Milestone 7. WYSIWYG, A/B testing, personalisation, comments, customer-submitted content, auto-translation, AI copy, RSS, multi-language reply, vendor-scoped authoring all explicitly Out of Scope. | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/Cms/`. | PASS |
| ADR-003 Vertical slice | One folder per slice under `Cms/Editor/`, `Cms/Publisher/`, `Cms/LegalOwner/`, `Cms/SuperAdmin/`, `Cms/Storefront/`. | PASS |
| ADR-004 EF Core 9 | Code-first migrations under `Modules/Cms/Persistence/Migrations/`. `SaveChangesInterceptor` audit hook from spec 003 reused. `ManyServiceProvidersCreatedWarning` suppressed in `CmsModule.cs` (project-memory rule). | PASS |
| ADR-010 KSA residency | All tables in the KSA-region Postgres; no cross-region replication. | PASS |

**No violations**. Complexity Tracking below documents intentional non-obvious design choices.

### Post-design re-check (after Phase 1 artifacts)

Re-evaluated after `data-model.md`, `contracts/cms-contract.md`, `quickstart.md`, and `research.md` were authored. **No new violations introduced.**

- **P5 (re-emphasised)**: every market-tunable knob is sourced from `cms.market_schemas` rows; no inline market constants. ✅
- **P16**: 5 entity kinds × 4-state lifecycle with the `superseded` legal-page terminal modelled and audited end-to-end. ✅
- **P24**: state machine encoded in `CmsContentLifecycle.cs` with compile-time transition guards; legal-page version supersession is a single-transaction worker step. ✅
- **P25**: 19 audit-event kinds documented in `data-model.md §5`. ✅
- **P28**: contracts file enumerates 25 endpoints + 5 newly-declared cross-module interfaces (or reused if catalog has shipped) with full reason-code inventory (43 owned codes). ✅

## Project Structure

### Documentation (this feature)

```text
specs/phase-1D/024-cms/
├── plan.md                  # This file
├── research.md              # Phase 0 — bilingual mandatory rule, indefinite legal version retention rationale, preview-token signed-opaque pattern, live-resolve refs vs eager copy, two-tier sort rule, banner CTA validation strategy, asset GC reference-counting, stale-draft soft-alerting, capacity cap math, banner-campaign binding lifecycle
├── data-model.md            # Phase 1 — 9 tables, 1 unified content lifecycle (+ superseded for legal), ERD, 19 audit-event kinds, 21 domain events
├── contracts/
│   └── cms-contract.md      # Phase 1 — every editor + publisher + legal-owner + super-admin + storefront endpoint, every reason code, every domain event, every cross-module interface
├── quickstart.md            # Phase 1 — implementer walkthrough, first slice (author + publish a banner), legal-page version transition smoke, preview-token round-trip
├── checklists/
│   └── requirements.md      # quality gate (pass)
└── tasks.md                 # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   ├── Shared/                                              # EXTENDED
│   │   ├── ICatalogProductReadContract.cs                   # NEW (or reused if spec 005 has shipped) — spec 005 implements
│   │   ├── ICatalogCategoryReadContract.cs                  # NEW (same)
│   │   ├── ICatalogBundleReadContract.cs                    # NEW (same)
│   │   ├── ICmsCampaignBindingPublisher.cs                  # NEW — spec 007-b consumes via the in-process bus
│   │   ├── CmsContentDomainEvents.cs                        # NEW — 21 INotification records
│   │   └── (existing files unchanged)
│   ├── Cms/                                                 # NEW MODULE
│   │   ├── CmsModule.cs                                     # AddCmsModule(IServiceCollection); MediatR scan; AddDbContext suppressing ManyServiceProvidersCreatedWarning; register subscribers + workers + ICmsCampaignBindingPublisher
│   │   ├── Primitives/
│   │   │   ├── ContentLifecycleState.cs                     # enum: Draft, Scheduled, Live, Archived, Superseded (Superseded only valid for legal_page_version)
│   │   │   ├── CmsContentLifecycle.cs                       # transition rules + per-kind guards
│   │   │   ├── EntityKind.cs                                # enum: BannerSlot, FeaturedSection, FaqEntry, BlogArticle, LegalPageVersion
│   │   │   ├── BannerSlotKind.cs                            # enum: HeroTop, CategoryStrip, FooterStrip, HomeSecondary
│   │   │   ├── FeaturedSectionKind.cs                       # enum: HomeTop, HomeMid, CategoryLanding, B2bLanding
│   │   │   ├── FaqCategory.cs                               # enum: 8 fixed values + ICU mapper
│   │   │   ├── BlogCategory.cs                              # enum: 6 fixed values + ICU mapper
│   │   │   ├── LegalPageKind.cs                             # enum: Terms, Privacy, Returns, Cookies
│   │   │   ├── CtaKind.cs                                   # enum: Link, Category, Product, Bundle, ExternalUrl, None
│   │   │   ├── CtaHealth.cs                                 # enum: Verified, Broken, TransientUnverified, NotApplicable
│   │   │   ├── ReferenceKind.cs                             # enum: Product, Category, Bundle (for featured-section refs)
│   │   │   ├── CmsActorKind.cs                              # enum: Editor, Publisher, LegalOwner, SuperAdmin, FinanceViewer, B2bAccountManager, System
│   │   │   ├── CmsReasonCode.cs                             # enum + ICU-key mapper for all owned reason codes
│   │   │   ├── CmsTriggerKind.cs                            # enum: 11 trigger kinds
│   │   │   ├── PreviewTokenClaims.cs                        # value-object: entity_kind, entity_id, version_id, mint_timestamp_utc, ttl_seconds, actor_role_at_mint
│   │   │   ├── PreviewTokenSigner.cs                        # HMAC-SHA256 sign + verify
│   │   │   ├── CmsMarketPolicy.cs                           # value-object resolved from market_schemas row
│   │   │   ├── BannerCapacityCalculator.cs                  # FR-021a: count live banners per (slot_kind, market_code, locale) including '*'
│   │   │   └── LocaleCompletenessGate.cs                    # FR-007: per-kind locale-completeness check at publish
│   │   ├── Editor/
│   │   │   ├── SaveBannerDraft/                             # create + update (xmin guard)
│   │   │   ├── SaveFeaturedSectionDraft/
│   │   │   ├── SaveFaqEntryDraft/
│   │   │   ├── SaveBlogArticleDraft/
│   │   │   ├── DeleteUnpublishedDraft/                      # FR-005a draft-delete path; rate-limited 30/h
│   │   │   ├── DismissStaleDraftAlert/                      # FR-034a editor dismissal
│   │   │   ├── ListMyDrafts/
│   │   │   └── BulkReorderFaqEntries/                       # xmin-guarded; emits cms.faq.reorder
│   │   ├── Publisher/
│   │   │   ├── PublishNow/                                  # draft → live; locale-completeness + capacity cap + CTA validation
│   │   │   ├── SchedulePublish/                             # draft → scheduled
│   │   │   ├── ArchiveContent/                              # live → archived; reason_note ≥ 10 chars
│   │   │   ├── BindBannerToCampaign/                        # creates BannerCampaignBinding row
│   │   │   ├── UnbindBannerFromCampaign/                    # explicit editor unbind
│   │   │   └── ReassignDraftOwnership/                      # FR-034a ownership-orphaned handling
│   │   ├── LegalOwner/
│   │   │   ├── SaveLegalPageVersionDraft/                   # mandatory both bodies
│   │   │   ├── SchedulePublishLegalPageVersion/             # draft → scheduled
│   │   │   ├── PublishLegalPageVersionNow/                  # draft → live; transitions prior live → superseded in same txn
│   │   │   └── ListLegalPageVersionHistory/
│   │   ├── SuperAdmin/
│   │   │   ├── PublishCrossMarketLegalPageVersion/          # *-scoped legal page; super_admin only
│   │   │   ├── EditMarketSchema/                            # update CmsMarketSchema row
│   │   │   └── ListOrphanedAssets/                          # surfaces dereferenced assets in grace window
│   │   ├── Preview/
│   │   │   ├── MintPreviewToken/                            # signed HMAC-SHA256 token
│   │   │   ├── RevokePreviewToken/                          # immediate token-store revocation
│   │   │   └── ReadPreviewedDraft/                          # storefront-side endpoint; X-Robots-Tag noindex
│   │   ├── Storefront/                                      # PUBLIC UNAUTH endpoints
│   │   │   ├── ListBannerSlots/                             # FR-021 two-tier sort + capacity-aware
│   │   │   ├── ListFeaturedSections/                        # FR-019 live-resolve refs
│   │   │   ├── ListFaqEntries/                              # FR-021 sort by display_order then created_at
│   │   │   ├── ListBlogArticles/                            # FR-021 sort by published_at_utc DESC
│   │   │   ├── GetBlogArticle/
│   │   │   └── GetLegalPage/                                # FR-021 single-row substitution rule
│   │   ├── Subscribers/
│   │   │   ├── CampaignDeactivatedHandler.cs                # FR-023 — auto-release banner-campaign bindings on pricing.campaign.deactivated
│   │   │   └── EditorRoleRevokedHandler.cs                  # FR-034a — flag ownership_orphaned on subscribed customer.role_revoked
│   │   ├── Workers/
│   │   │   ├── CmsScheduledPublishWorker.cs                 # 60s cadence; promotes scheduled→live + live→archived; legal-page supersession in same txn
│   │   │   ├── CmsAssetGarbageCollectorWorker.cs            # daily cadence; FR-009a reference-counted sweep
│   │   │   ├── CmsStaleDraftAlertWorker.cs                  # daily cadence; FR-034a soft alerts
│   │   │   └── CmsPreviewTokenCleanupWorker.cs              # daily cadence; deletes expired+30d preview tokens
│   │   ├── Authorization/
│   │   │   └── CmsPermissions.cs                            # cms.editor, cms.publisher, cms.legal_owner
│   │   ├── Entities/
│   │   │   ├── BannerSlot.cs
│   │   │   ├── FeaturedSection.cs
│   │   │   ├── FaqEntry.cs
│   │   │   ├── BlogArticle.cs
│   │   │   ├── LegalPageVersion.cs
│   │   │   ├── CmsAsset.cs
│   │   │   ├── CmsPreviewToken.cs
│   │   │   ├── BannerCampaignBinding.cs
│   │   │   └── CmsMarketSchema.cs
│   │   ├── Persistence/
│   │   │   ├── CmsDbContext.cs
│   │   │   ├── Configurations/                              # IEntityTypeConfiguration<T> per entity
│   │   │   └── Migrations/                                  # net-new; creates `cms` schema + 9 tables + append-only triggers + FR-009a + FR-016 cleanup exceptions
│   │   ├── Messages/
│   │   │   ├── cms.en.icu                                   # system-generated EN keys
│   │   │   ├── cms.ar.icu                                   # system-generated AR keys (editorial-grade)
│   │   │   └── AR_EDITORIAL_REVIEW.md
│   │   └── Seeding/
│   │       ├── CmsReferenceDataSeeder.cs                    # KSA + EG market_schemas rows; idempotent across all envs
│   │       └── CmsV1DevSeeder.cs                            # synthetic content spanning 5 entity kinds × 4 lifecycle states + bilingual coverage + per-market + cross-market `*` (Dev+Staging only, SeedGuard)
└── tests/
    └── Cms.Tests/
        ├── Unit/                                            # state machine, locale-completeness gate, capacity calculator, preview-token signer/verifier, market-policy resolver, two-tier sort, reason-code mapper
        ├── Integration/                                     # WebApplicationFactory + Testcontainers Postgres; every editor + publisher + legal-owner + super-admin + storefront slice; concurrency guards; scheduled-publish worker; asset-GC worker; stale-draft worker; preview-token cleanup worker; subscriber tests; banner-CTA-validation
        └── Contract/                                        # asserts every Acceptance Scenario from spec.md against live handlers
```

**Structure Decision**: Net-new `Modules/Cms/` vertical-slice module under the modular monolith. Cross-module read contracts and event types live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule). The `Editor/`, `Publisher/`, `LegalOwner/`, `SuperAdmin/`, `Storefront/`, `Preview/` sibling layout enforces visibly that the six actor surfaces consume the same content lifecycle but expose disjoint endpoints with disjoint RBAC. The `Subscribers/` folder houses cross-module event consumers (007-b campaign deactivation, 004 role revocation); the `Workers/` folder houses the four reconciliation safety nets (scheduled publish + asset GC + stale-draft alert + preview-token cleanup). The `Storefront/` slice is the unauthenticated public-read surface; it lives inside the same module to keep the leak-prevention filter (FR-005, SC-003) co-located with the data-access layer.

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `ContentLifecycleState`, `CmsContentLifecycle`, `EntityKind`, `BannerSlotKind`, `FeaturedSectionKind`, `FaqCategory`, `BlogCategory`, `LegalPageKind`, `CtaKind`, `CtaHealth`, `ReferenceKind`, `CmsReasonCode`, `CmsTriggerKind`, `PreviewTokenClaims`, `PreviewTokenSigner`, `CmsMarketPolicy`, `BannerCapacityCalculator`, `LocaleCompletenessGate` | Foundation for all slices |
| B. Persistence + migrations | 9 entities + EF configurations + initial migration; `CmsDbContext` with warning suppression; append-only triggers on `legal_page_versions` (post-`live`), `preview_tokens`, `assets`; controlled exceptions for FR-009a sweep + FR-016 token cleanup | Unblocks all slices and workers |
| C. Reference seeder | `CmsReferenceDataSeeder` (KSA + EG + `*` market_schemas rows; idempotent across all envs) | Unblocks integration tests + Staging/Prod boot |
| D. Cross-module shared declarations | `ICatalogProductReadContract`, `ICatalogCategoryReadContract`, `ICatalogBundleReadContract` (or reuse if spec 005 has them), `ICmsCampaignBindingPublisher`, `CmsContentDomainEvents` | Unblocks specs 005 / 007-b / 014 / 025 / 028 to build against the contract |
| E. Storefront leak-safe read engine | A single `StorefrontContentResolver` that applies FR-017 / FR-021 filters at the EF query level (live + window + market+locale tier sort); used by all storefront endpoints | Unblocks storefront slices; SC-003 leak prevention is co-located here |
| F. Editor draft slices | SaveBannerDraft → SaveFeaturedSectionDraft → SaveFaqEntryDraft → SaveBlogArticleDraft → DeleteUnpublishedDraft → DismissStaleDraftAlert → ListMyDrafts | FR-001, FR-005a, FR-006, FR-008, FR-009, FR-022a publish-time validation, FR-033, FR-034a |
| G. FAQ bulk-reorder slice | BulkReorderFaqEntries (xmin-guarded; concurrent-reorder loser sees 409 cms.faq.reorder_conflict) | FR-021 ordering + concurrency |
| H. Publisher slices | PublishNow → SchedulePublish → ArchiveContent → BindBannerToCampaign → UnbindBannerFromCampaign → ReassignDraftOwnership | FR-001 lifecycle, FR-007 locale completeness, FR-013, FR-021a capacity, FR-022a CTA validation, FR-023 binding, FR-034a orphan reassignment |
| I. LegalOwner slices | SaveLegalPageVersionDraft → SchedulePublishLegalPageVersion → PublishLegalPageVersionNow (with prior-live → superseded in same txn) → ListLegalPageVersionHistory | FR-005a indefinite retention, FR-007 mandatory both bodies, FR-001 superseded transition |
| J. SuperAdmin slices | PublishCrossMarketLegalPageVersion (`*` scope) → EditMarketSchema → ListOrphanedAssets | FR-007 cross-market gate, P5 market-schema editing, FR-009a observability |
| K. Preview-token slices | MintPreviewToken → RevokePreviewToken → ReadPreviewedDraft (X-Robots-Tag noindex) | FR-014–FR-016 |
| L. Storefront read slices | ListBannerSlots → ListFeaturedSections → ListFaqEntries → ListBlogArticles → GetBlogArticle → GetLegalPage | FR-017–FR-021, FR-019 live-resolve refs, FR-022a CTA re-validation, FR-021 two-tier sort |
| M. Subscribers (cross-module event consumers) | CampaignDeactivatedHandler, EditorRoleRevokedHandler | FR-023 binding auto-release, FR-034a ownership orphan flag |
| N. Workers | CmsScheduledPublishWorker (60s), CmsAssetGarbageCollectorWorker (daily), CmsStaleDraftAlertWorker (daily), CmsPreviewTokenCleanupWorker (daily) | FR-010–FR-011 worker promotion, FR-009a sweep, FR-034a alert, FR-016 token cleanup |
| O. Authorization wiring | `CmsPermissions.cs` constants + `[RequirePermission]` attributes; spec 015 wires role bindings on its PR | Permission boundary |
| P. Domain events + 025 / 028 contract | Publish 21 events on each lifecycle / operational transition; subscribed by spec 025 (cache invalidate, broken-ref, stale-alert) and spec 028 (analytics); subscribers land on those specs' PRs | FR-024, FR-025 |
| Q. Contracts + OpenAPI | Regenerate `openapi.cms.json`; assert contract test suite green; document every reason code | Guardrail #2 |
| R. AR/EN editorial | All system-generated strings ICU-keyed; AR strings flagged in `AR_EDITORIAL_REVIEW.md` | P4 |
| S. `cms-v1` dev seeder | `CmsV1DevSeeder` — synthetic content spanning all 5 entity kinds × 4 lifecycle states; bilingual coverage; per-market + `*` examples; legal-page version-history with 2 versions per `(kind, market)` | SC-009, FR seeder requirement |
| T. Integration / DoD | Full Testcontainers run; capacity-cap concurrency test (SC-010); leak-detection test (SC-003); worker idempotency test (SC-005); featured-section resolution perf test (SC-007); banner-list perf test (SC-006); subscriber tests; fingerprint; DoD checklist; audit-coverage script | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Net-new `Modules/Cms/` module rather than co-locating with `Catalog` or `Marketing` (no marketing module exists yet anyway) | CMS carries its own state machine, RBAC, audit, 4 workers, 5 entity kinds, and the storefront leak-prevention filter — none of which belong in catalog. | Co-location with catalog would force catalog to take a hard dependency on CMS auth + lifecycle + scheduler and break the modular-monolith boundary. |
| Single 4-state lifecycle (`draft → scheduled → live → archived`) shared across 5 entity kinds, with `superseded` terminal extension only for legal page versions | Compile-time guarantee that no transition path is silently legal across any kind. Legal page supersession is the only domain semantic that demands a non-`archived` terminal — all other archival uses `archived`. | Per-kind state machines fragment authoring UX and force the publisher endpoints to branch on entity kind. |
| 9 net-new tables (vs. consolidating 4 of the 5 entity kinds into a single `cms_content` table with discriminator) | Per-kind columns differ enough (banner has assets + scheduling window + slot_kind; FAQ has display_order + category; blog has slug + SEO + cover_asset; legal has version_label + effective_at) that a discriminated table forces every column nullable, defeats indexing, and obscures field-level integrity. | A single discriminated table is leaky abstraction; per-kind tables make EF migrations cleaner and indexing per-kind precise. |
| Live-resolve featured-section references (vs. eager-copy resolved entries on publish) | Catalog data (price, name, image) goes stale within minutes; live-resolve at storefront-read time is the only correctness-preserving option. The 300 ms p95 budget for a 24-ref section is tight but achievable via parallel resolution. | Eager-copy on publish would require a re-resolve worker on every catalog change event — more complexity and still has a staleness window. |
| Two-tier sort (specific-market rows first, then `*`-scoped rows) at storefront read time | Editor's intent is "specific market wins"; a global `*` cookie banner outranking a market-specific Ramadan campaign would be wrong; equal interleaving by `priority_within_slot` ignores the most-specific-match rule. | Priority-only sort silently demotes critical market campaigns; market-only-with-`*`-fallback is too brittle (can't co-exist with `*` content). |
| Banner CTA validation at BOTH publish-time AND storefront-read-time (with fail-open `transient_unverified` flag) | Publish-time validation prevents shipping a broken-ref banner; read-time re-validation handles post-publish catalog churn (product archived). Fail-open on transient errors is the right call because suppressing customer-facing marketing during a catalog blip is worse than a possibly-stale CTA. | Publish-time-only fails to catch post-publish staleness; read-time-only allows broken refs at publish; fail-closed on transient causes thundering-banner-disappearance during catalog maintenance. |
| Capacity cap of 5 banners per `(slot_kind, market_code, locale)` with `*`-scoped banners counting against every per-market cap | Marketers operate banner rotations weekly with 2–4 concurrent banners; 5 is comfortable headroom. `*` counts everywhere because customers in EG see the `*` banner — capacity is delivered-banner-count, not authored-banner-count. | Unbounded leads to home-page chaos; per-market `*` accounting (1 slot for `*` total) creates surprising capacity exhaustion when EG editors don't realize KSA already has 5. |
| Reference-counted asset cleanup with 7-day grace (vs. immediate deletion or indefinite retention) | Storage cost for orphaned assets grows monotonically without GC; immediate deletion is unsafe (ops error → customer images gone). 7-day grace is the recoverability buffer; per-market configurable for privacy-incident response. | Indefinite is unbounded cost; immediate is unsafe; per-market policy lets privacy-ops ratchet down when needed. |
| Stale drafts soft-warning only (no auto-archive) | Auto-archive of a slow-moving legal page draft, an embargoed-launch banner, or a long-research blog article would be data loss without consent. Editors own content lifecycle (Principle 25). The dismiss-stale-alert endpoint gives explicit per-draft suppression. | Auto-archive after N days saves admin queue clutter but at the cost of editor trust and irrecoverable WIP. |
| Indefinite legal page version retention; `superseded` is terminal but queryable; `*`-scoped legal page publish requires `super_admin` | Regulatory + dispute-investigation requirement: the contract in force at any past order time MUST be re-derivable. Cross-market legal scope is inherently a legal-and-super-admin call (one wrong `*` legal page misroutes the contract for both markets simultaneously). | Pruning old versions defeats audit; `cms.legal_owner`-only `*` scope under-protects the cross-market boundary. |
| Polymorphic featured-section `references` jsonb (vs. typed FK columns) | Catalog reference kind (`product`, `category`, `bundle`) varies; a typed FK would require 3 nullable FK columns on every row; jsonb keeps the schema clean and is queryable via Postgres jsonb operators. | Typed FK columns proliferate nullable noise and break the cross-module pattern from specs 020 / 021 / 022 / 023 (which all use polymorphic links). |
| Preview tokens are signed opaque HMAC-SHA256 strings checked against a server-side store, NOT JWTs | A token store enables immediate revocation (a JWT would only expire by TTL — a 24 h leaked link is a 24 h leak). HMAC + store keeps signing simple and rotation cheap. | A JWT-only design loses revocation; a stateless token without a store loses replay protection. |
| Storefront read endpoints are public unauthenticated, rate-limited per IP + per-entity-kind | Customer-facing storefront content is browse-eligible per Principle 3 ("unauthenticated users MAY browse"); auth on storefront reads would block anonymous home views. Rate-limit prevents enumeration / scraping abuse. | Auth-gated storefront reads break the constitutional browse rule; no rate-limit invites scraping. |
| Banner ↔ campaign binding lives in `cms.banner_campaign_bindings`, NOT on the banner row | A banner may be bound and unbound multiple times across its lifecycle (campaign A → unbind → campaign B); a single FK on the banner row loses history. Append-only binding rows preserve the audit trail. | A single FK column conflates "currently bound" with "ever bound" and loses lifecycle audit. |
| `vendor_id` slot reserved on every CMS entity row but never populated in V1 | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2. Same pattern as specs 020 / 021 / 022 / 023 / 007-b. | Omitting forces a migration of every CMS row when vendor-scoped authoring lands in Phase 2. |
| Customer-supplied content is FORBIDDEN in CMS at V1 (no testimonials, no comments, no submissions) | Principle 4 (editorial-grade AR), Principle 25 (audit + traceability) and Principle 27 (UX quality) all push customer content into structured spec 022 (reviews) / spec 023 (support tickets) where moderation, ownership, and editorial quality are first-class. CMS is editor-authored marketing copy only. | Allowing customer-submitted content would force CMS to ship moderation, abuse handling, and bilingual editorial workflows that already exist in specs 022 / 023. |
| Storefront read endpoints emit cache-invalidate events to spec 025 / spec 014 edge cache subscribers (Phase 1E E1) but ship as no-op events in V1 | Edge-cache infrastructure lands in Phase 1E E1; emitting events from V1 unblocks the 1E subscribers without retrofitting CMS. The TTL-based `Cache-Control: public, max-age=60` is the V1 cache strategy; edge cache is a Phase 1E enhancement. | Retrofitting cache events post-1E means re-touching every CMS publisher slice; emitting them now is cheap and forward-compatible. |
