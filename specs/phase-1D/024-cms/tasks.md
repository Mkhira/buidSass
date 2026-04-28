---
description: "Task list for Spec 024 — CMS (Phase 1D · Milestone 7)"
---

# Tasks: CMS

**Input**: Design documents from `/specs/phase-1D/024-cms/`
**Prerequisites**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/cms-contract.md](./contracts/cms-contract.md), [quickstart.md](./quickstart.md)

**Tests**: Included. Integration + contract tests are required per the spec's 11 SCs and the project DoD (Testcontainers Postgres; no SQLite shortcut). Unit tests included for primitives, content lifecycle, locale-completeness gate, banner capacity calculator, preview-token signer, two-tier sort, banner-CTA validator, market-policy resolver, and reason-code mapper.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story (US1 / US2 / US3 / US4 / US5 / US6 / US7). Foundational primitives, persistence, migrations, cross-module shared declarations, leak-safe storefront read engine, and reference seeder land in Phase 2 and unblock all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to a user story from spec.md (US1–US7); Setup, Foundational, and Polish phases have no story label
- Include exact file paths in descriptions

## Path Conventions (per [plan.md §Project Structure](./plan.md))

- Backend module: `services/backend_api/Modules/Cms/`
- Cross-module shared declarations: `services/backend_api/Modules/Shared/`
- Tests: `services/backend_api/tests/Cms.Tests/` (Unit/, Integration/, Contract/, Performance/)
- Fakes (cross-module test doubles): `services/backend_api/Modules/Shared/Testing/`
- Reference seeder mode: idempotent across Dev + Staging + Prod (via `SeedGuard`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization for the CMS module.

- [ ] T001 Create `services/backend_api/Modules/Cms/` directory tree per plan.md §Project Structure (Editor/, Publisher/, LegalOwner/, SuperAdmin/, Preview/, Storefront/, Subscribers/, Workers/, Authorization/, Entities/, Persistence/, Messages/, Seeding/, Primitives/) and add a placeholder `CmsModule.cs` registering an empty `AddCmsModule` extension method
- [ ] T002 [P] Create `services/backend_api/tests/Cms.Tests/` test project with xUnit + FluentAssertions + Microsoft.Extensions.TimeProvider.Testing references and Testcontainers.PostgreSql wiring (mirror `Support.Tests/Cms.Tests.csproj`); add `Unit/`, `Integration/`, `Contract/`, `Performance/` folders
- [ ] T003 [P] Add `cms` schema to the connection-string seeded migration generator (no schema content yet — empty migration just to confirm EF tooling works); verify `dotnet ef migrations add InitCmsSchema --project services/backend_api/Modules/Cms` produces a no-op migration
- [ ] T004 [P] Wire `AddCmsModule(builder.Configuration)` into `services/backend_api/Program.cs` after the existing `AddSupportModule(...)` call (suppression of `ManyServiceProvidersCreatedWarning` is enforced inside `CmsModule.cs` per project-memory rule)
- [ ] T005 Add a CMS-test `Cms.Tests.csproj` reference into `tests.sln` and verify `dotnet test --filter Category=Smoke` from repo root runs zero tests successfully (sanity of harness wiring)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core primitives, persistence, migrations, cross-module shared declarations, the leak-safe storefront read engine, and reference seeder. **No user story work can begin until this phase is complete.**

### Primitives (Phase A)

- [ ] T006 [P] Create `Modules/Cms/Primitives/ContentLifecycleState.cs` enum: `Draft`, `Scheduled`, `Live`, `Archived`, `Superseded`
- [ ] T007 [P] Create `Modules/Cms/Primitives/EntityKind.cs` enum: `BannerSlot`, `FeaturedSection`, `FaqEntry`, `BlogArticle`, `LegalPageVersion` + ICU-key mapper for friendly names
- [ ] T008 [P] Create `Modules/Cms/Primitives/BannerSlotKind.cs` enum: `HeroTop`, `CategoryStrip`, `FooterStrip`, `HomeSecondary`
- [ ] T009 [P] Create `Modules/Cms/Primitives/FeaturedSectionKind.cs` enum: `HomeTop`, `HomeMid`, `CategoryLanding`, `B2bLanding`
- [ ] T010 [P] Create `Modules/Cms/Primitives/FaqCategory.cs` enum with the 8 fixed values from FR-006 + ICU-key mapper (en + ar)
- [ ] T011 [P] Create `Modules/Cms/Primitives/BlogCategory.cs` enum with the 6 fixed values from FR-006 + ICU-key mapper
- [ ] T012 [P] Create `Modules/Cms/Primitives/LegalPageKind.cs` enum: `Terms`, `Privacy`, `Returns`, `Cookies` + ICU-key mapper
- [ ] T013 [P] Create `Modules/Cms/Primitives/CtaKind.cs` enum: `Link`, `Category`, `Product`, `Bundle`, `ExternalUrl`, `None`
- [ ] T014 [P] Create `Modules/Cms/Primitives/CtaHealth.cs` enum: `Verified`, `Broken`, `TransientUnverified`, `NotApplicable`
- [ ] T015 [P] Create `Modules/Cms/Primitives/ReferenceKind.cs` enum: `Product`, `Category`, `Bundle`
- [ ] T016 [P] Create `Modules/Cms/Primitives/CmsActorKind.cs` enum: `Editor`, `Publisher`, `LegalOwner`, `SuperAdmin`, `FinanceViewer`, `B2bAccountManager`, `System`
- [ ] T017 [P] Create `Modules/Cms/Primitives/CmsReasonCode.cs` enum + ICU-key mapper for all 43 owned reason codes from `contracts/cms-contract.md §10`
- [ ] T018 [P] Create `Modules/Cms/Primitives/CmsTriggerKind.cs` enum: 11 trigger kinds (`editor_save`, `publisher_schedule`, `publisher_publish_now`, `publisher_archive`, `publisher_schedule_past_start`, `publisher_schedule_past_effective`, `worker_promote_to_live`, `worker_promote_to_archived`, `worker_supersede_legal_version`, `super_admin_force`, `editor_delete_unpublished_draft`)
- [ ] T019 [P] Create `Modules/Cms/Primitives/CmsContentLifecycle.cs` with compile-time transition guards for all valid transitions per data-model.md §3 including the legal-page `Live → Superseded` edge; reject all other transitions with `InvalidContentTransitionException` carrying `cms.{kind}.illegal_transition` reason code
- [ ] T020 [P] Create `Modules/Cms/Primitives/PreviewTokenClaims.cs` value object: `EntityKind`, `EntityId`, `VersionId`, `MintTimestampUtc`, `TtlSeconds`, `ActorRoleAtMint`
- [ ] T021 [P] Create `Modules/Cms/Primitives/PreviewTokenSigner.cs` implementing HMAC-SHA256 sign + verify per research.md §R3 (constant-time compare; signing key sourced from layered config; throws `PreviewTokenSignatureInvalidException` on mismatch)
- [ ] T022 [P] Create `Modules/Cms/Primitives/CmsMarketPolicy.cs` value object that loads from `cms.market_schemas` row (banner_max_live_per_slot, featured_section_max_references, preview_token_default_ttl_hours, draft_staleness_alert_days, asset_grace_period_days)
- [ ] T023 [P] Create `Modules/Cms/Primitives/BannerCapacityCalculator.cs` implementing FR-021a — counts `live` banners per `(slot_kind, market_code, locale)` including `*`-scoped banners against every per-market cap; throws `BannerSlotCapacityExceededException` on overflow
- [ ] T024 [P] Create `Modules/Cms/Primitives/LocaleCompletenessGate.cs` implementing FR-007 per-kind locale-completeness check (banner / featured / FAQ / legal mandatory both bodies; blog single-locale allowed); returns `Allowed` or `Blocked(reason_code)`
- [ ] T025 [P] Create `Modules/Cms/Primitives/CmsRowVersion.cs` typed wrapper for the EF Core xmin row_version with helpers for the optimistic-concurrency check (mirrors `TicketRowVersion` from spec 023)
- [ ] T026 [P] Create `Modules/Cms/Authorization/CmsPermissions.cs` static class with constants `cms.editor`, `cms.publisher`, `cms.legal_owner` (used by `[RequirePermission(...)]` attributes from spec 004's RBAC)

### Persistence — entities (Phase B)

- [ ] T027 [P] Create `Modules/Cms/Entities/BannerSlot.cs` with all columns from data-model.md §2.1 (incl. `slot_kind`, bilingual headline/subhead, asset_id_ar/en, cta_kind/target/health, schedule window, market_code, priority_within_slot, lifecycle state, vendor_id, owner_actor_id, ownership flags, stale-alert columns, audit timestamps, xmin)
- [ ] T028 [P] Create `Modules/Cms/Entities/FeaturedSection.cs` with all columns from data-model.md §2.2 (incl. `section_kind`, bilingual title/subtitle, `references` jsonb, display_priority, market_code, lifecycle state, vendor_id, owner/orphan flags, last_partial_broken_alert_at_utc, audit timestamps, xmin)
- [ ] T029 [P] Create `Modules/Cms/Entities/FaqEntry.cs` with all columns from data-model.md §2.3 (incl. `category`, bilingual question/answer, display_order, market_code, lifecycle state, owner/orphan flags, audit timestamps, xmin)
- [ ] T030 [P] Create `Modules/Cms/Entities/BlogArticle.cs` with all columns from data-model.md §2.4 (incl. `category`, slug, authored_locale, title, summary, body, cover_asset_id, seo block columns, scheduled_publish_at_utc, market_code, lifecycle state, owner/orphan flags, audit timestamps, xmin)
- [ ] T031 [P] Create `Modules/Cms/Entities/LegalPageVersion.cs` with all columns from data-model.md §2.5 (incl. `legal_page_kind`, version_label, both bodies, effective_at_utc, market_code, lifecycle state including `Superseded`, superseded_at_utc, superseded_by_version_id, owner/orphan flags, audit timestamps, xmin)
- [ ] T032 [P] Create `Modules/Cms/Entities/CmsAsset.cs` with all columns from data-model.md §2.6 (incl. storage_object_id, mime, size_bytes, intended_locale, original_filename, storage_object_state, dereferenced_at_utc, swept_at_utc, uploader, upload timestamp, xmin)
- [ ] T033 [P] Create `Modules/Cms/Entities/CmsPreviewToken.cs` with all columns from data-model.md §2.7 (incl. token_hash, entity_kind, entity_id, version_id, actor_role_at_mint, minted_by_actor_id, minted_at_utc, expires_at_utc, revoked_at_utc, revoked_by_actor_id)
- [ ] T034 [P] Create `Modules/Cms/Entities/BannerCampaignBinding.cs` with all columns from data-model.md §2.8 (incl. banner_id, version_id, campaign_id, bound_at_utc, released_at_utc, binding_state, release_actor_id, release_reason_note, xmin)
- [ ] T035 [P] Create `Modules/Cms/Entities/CmsMarketSchema.cs` with all columns from data-model.md §2.9 (PK `market_code`; capacity / window default columns; last_edited_by_actor_id; last_edited_at_utc; xmin)

### Persistence — DbContext, configurations, migration (Phase B)

- [ ] T036 Create `Modules/Cms/Persistence/CmsDbContext.cs` deriving from `DbContext`; register `DbSet<T>` for all 9 entities; configure `cms` schema; suppress `ManyServiceProvidersCreatedWarning` per project-memory rule
- [ ] T037 [P] Create `Modules/Cms/Persistence/Configurations/BannerSlotConfiguration.cs` (and 8 sibling configurations — one per entity) implementing `IEntityTypeConfiguration<T>` with all CHECK constraints, indexes (per data-model.md §2 indexes paragraph), jsonb mapping for `references`, `xmin` mapped via `IsRowVersion()`
- [ ] T038 Create the EF Core migration `20260429_001_AddCmsSchema` via `dotnet ef migrations add AddCmsSchema --project services/backend_api/Modules/Cms`; review the generated SQL for the 9 tables + indexes
- [ ] T039 Edit migration `20260429_001_AddCmsSchema.cs` to add Postgres `BEFORE DELETE` trigger on `cms.legal_page_versions` rejecting all deletes with `cms.legal_page.version.delete_forbidden`; add `BEFORE UPDATE` trigger restricting non-`draft` row updates to the supersede transition only
- [ ] T040 Edit migration `20260429_001_AddCmsSchema.cs` to add `BEFORE DELETE` triggers on `cms.assets` (allowing only the GC worker's `state=swept` transition path), on `cms.preview_tokens` (allowing only the cleanup worker's `expires_at_utc + 30d < now()` deletion), and on `cms.banner_campaign_bindings` (rejecting all deletes; release is a state flip)
- [ ] T041 Edit migration `20260429_001_AddCmsSchema.cs` to add the partial unique index `(legal_page_kind, market_code) WHERE state = 'live'` on `cms.legal_page_versions` (enforces "exactly one live version per kind+market") + `UNIQUE (market_code, authored_locale, slug)` on `cms.blog_articles`
- [ ] T042 Run `dotnet ef database update --project services/backend_api/Modules/Cms` against a Testcontainers Postgres and assert all 9 tables + triggers + indexes are created via `\dt cms.*` + `\d cms.legal_page_versions`

### Cross-module shared declarations (Phase D)

- [ ] T043 [P] Create or reuse `Modules/Shared/ICatalogProductReadContract.cs` (declared by spec 005 if shipped; otherwise newly declared here) with `ReadAsync(productId, marketCode, ct) → CatalogProductRead` per data-model.md §7; create `Modules/Shared/Testing/FakeCatalogProductReadContract.cs` returning configurable availability + `LinkedEntityUnavailableReason` for tests
- [ ] T044 [P] Create or reuse `Modules/Shared/ICatalogCategoryReadContract.cs` (same pattern) + `Modules/Shared/Testing/FakeCatalogCategoryReadContract.cs`
- [ ] T045 [P] Create or reuse `Modules/Shared/ICatalogBundleReadContract.cs` (same pattern) + `Modules/Shared/Testing/FakeCatalogBundleReadContract.cs`
- [ ] T046 [P] Create `Modules/Shared/ICmsCampaignBindingPublisher.cs` exposing the binding events emitted by 024 (consumed by spec 007-b on the in-process MediatR bus)
- [ ] T047 [P] Create `Modules/Shared/CmsContentDomainEvents.cs` with all 21 `INotification` records per data-model.md §6 (10 lifecycle: `CmsBannerPublished`, `CmsBannerArchived`, `CmsFeaturedSectionPublished`, `CmsFeaturedSectionArchived`, `CmsFaqPublished`, `CmsFaqArchived`, `CmsBlogArticlePublished`, `CmsBlogArticleArchived`, `CmsLegalPageVersionPublished`, `CmsLegalPageVersionSuperseded`; 11 operational: `CmsFeaturedSectionPartialBroken`, `CmsFeaturedSectionFullyBroken`, `CmsBannerScheduledPublishBlockedCapacity`, `CmsBannerCtaTargetBroken`, `CmsCacheInvalidateBanner`, `CmsCacheInvalidateFeaturedSection`, `CmsCacheInvalidateFaq`, `CmsCacheInvalidateLegalPage`, `CmsCacheInvalidateBlogArticle`, `CmsDraftStaleAlert`, `CmsDraftOwnershipOrphaned`)
- [ ] T048 [P] Reuse `ICustomerRoleLifecycleSubscriber` from spec 004 if declared; otherwise create `Modules/Shared/ICustomerRoleLifecycleSubscriber.cs` with `OnRoleRevokedAsync(actor_id, role_code, ct)` + a fake double for tests where spec 004's role-revocation channel isn't on `main`

### Reference seeder + module wiring (Phase C)

- [ ] T049 Create `Modules/Cms/Seeding/CmsReferenceDataSeeder.cs` populating 3 `cms.market_schemas` rows for `EG`, `KSA`, `*` with V1 default values per data-model.md §8; idempotent across Dev + Staging + Prod via `SeedGuard` (project pattern from spec 020)
- [ ] T050 Wire `CmsReferenceDataSeeder` into `services/backend_api/Modules/Bootstrap/ReferenceDataSeederHost.cs` registry alongside the existing `SupportReferenceDataSeeder`
- [ ] T051 Verify module wiring: `services/backend_api/Modules/Cms/CmsModule.cs` registers `AddDbContext<CmsDbContext>(...)` (with warning suppression), MediatR scan for the `Cms` assembly, the four hosted-service workers (deferred to per-worker tasks but registered here), `[RequirePermission]` policies for `cms.editor` / `cms.publisher` / `cms.legal_owner`, and the catalog read contract bindings (real impl in production; fakes in tests)

### Storefront leak-safe read engine (Phase E)

- [ ] T052 [P] Create `Modules/Cms/Storefront/ICmsContentRow.cs` marker interface implemented by all 5 entity classes (`State`, `MarketCode`, `ScheduledStartUtc?`, `ScheduledEndUtc?`, `ScheduledPublishAtUtc?`)
- [ ] T053 Create `Modules/Cms/Storefront/StorefrontContentResolver.cs` exposing `IQueryable<T> ApplyStorefrontFilter<T>(IQueryable<T>, string marketCode, string locale, TimeProvider clock) where T : ICmsContentRow` — applies the FR-017 filter (`State == Live AND scheduling window open`) AND the FR-021 two-tier sort (specific market first, then `*`); MUST be the only path used by storefront slices
- [ ] T054 [P] Create `Modules/Cms/Tests/Unit/StorefrontContentResolverTests.cs` asserting that every non-`Live` state and every closed-window combination is filtered out, two-tier sort is correct, and stable secondary sort by `created_at_utc ASC` works (research.md §R8)

### Authorization + module bootstrap (Phase O)

- [ ] T055 Apply `[RequirePermission(CmsPermissions.Editor)]` / `[RequirePermission(CmsPermissions.Publisher)]` / `[RequirePermission(CmsPermissions.LegalOwner)]` attribute decorators on the slice-level controller bases (one base per actor surface) so admin endpoints fail-closed by default; spec 015 wires the actual role bindings on its PR

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 — `cms.editor` schedules a Ramadan banner; worker promotes to live (Priority: P1) 🎯 MVP

**Goal**: A `cms.editor` can author a bilingual banner with bilingual assets, target it to `KSA`, schedule it for a future window, hand it to a `cms.publisher` for `schedule-publish`; the `CmsScheduledPublishWorker` promotes it to `live` at `scheduled_start_utc` and to `archived` at `scheduled_end_utc`. Locale-completeness, banner capacity cap, and CTA validation gates run at publish.

**Independent Test**: Author a bilingual banner via the admin endpoint with a future schedule. Verify `state=draft`. `cms.publisher` schedule-publishes; verify `state=scheduled` and gate-pass audit. Advance the worker clock past `scheduled_start_utc`; verify `state=live` and `cms.banner.published` event. Advance past `scheduled_end_utc`; verify `state=archived`.

### Tests for User Story 1

- [ ] T056 [P] [US1] Contract test for `POST /v1/admin/cms/banner-slots/drafts` happy path + `cms.banner.schedule_window_invalid` + `cms.banner.cta_kind_target_mismatch` + `cms.asset.mime_forbidden` in `tests/Cms.Tests/Contract/Editor/SaveBannerDraftContractTests.cs`
- [ ] T057 [P] [US1] Contract test for `PATCH /v1/admin/cms/banner-slots/drafts/{id}` xmin-conflict path → `409 cms.draft.version_conflict` in `tests/Cms.Tests/Contract/Editor/PatchBannerDraftContractTests.cs`
- [ ] T058 [P] [US1] Contract test for `POST /v1/admin/cms/banner-slots/{id}/schedule-publish` happy path + `cms.publish.locale_completeness_missing` + `cms.banner.cta_target_unresolvable` + `cms.banner.slot_capacity_exceeded` in `tests/Cms.Tests/Contract/Publisher/ScheduleBannerPublishContractTests.cs`
- [ ] T059 [P] [US1] Integration test for end-to-end banner lifecycle (Editor authors → Publisher schedules → Worker promotes to live → Worker promotes to archived) using `FakeTimeProvider` to advance the clock in `tests/Cms.Tests/Integration/Workers/BannerLifecycleEndToEndTests.cs`
- [ ] T060 [P] [US1] Integration test asserting concurrent capacity-cap publishes — 100 concurrent attempts at the cap, exactly 1 winner, others see `409 cms.banner.slot_capacity_exceeded` (SC-010 banner-specific) in `tests/Cms.Tests/Integration/Publisher/BannerCapacityRaceTests.cs`

### Implementation for User Story 1

- [ ] T061 [P] [US1] Create `Modules/Cms/Editor/SaveBannerDraft/Command.cs` (record per quickstart.md §1) + `Modules/Cms/Editor/SaveBannerDraft/Handler.cs` resolving create vs update via xmin guard + asset-MIME validation via spec 015 storage-abstraction read + audit `cms.draft.created` / `cms.draft.updated`
- [ ] T062 [P] [US1] Create `Modules/Cms/Editor/SaveBannerDraft/Validator.cs` FluentValidation rules: schedule window strictness (`start < end`), CTA-kind/target shape coherence per `cta_kind`, external_url `https://` requirement, headline char caps (120/240)
- [ ] T063 [US1] Wire the SaveBannerDraft slice to `POST /v1/admin/cms/banner-slots/drafts` + `PATCH /v1/admin/cms/banner-slots/drafts/{id}` in `Modules/Cms/Editor/SaveBannerDraft/Endpoint.cs` with `[RequirePermission(CmsPermissions.Editor)]`, `[RequireIdempotencyKey]`, `[EnableRateLimiting("cms-admin")]`
- [ ] T064 [P] [US1] Create `Modules/Cms/Publisher/SchedulePublish/Command.cs` + `Handler.cs` invoking LocaleCompletenessGate → BannerCapacityCalculator → BannerCtaValidator → state machine transition to `Scheduled`; emits audit `cms.content.scheduled`
- [ ] T065 [P] [US1] Create `Modules/Cms/Publisher/PublishNow/Command.cs` + `Handler.cs` per quickstart.md §1 code skeleton (gates same as schedule-publish; transitions to `Live`; emits `cms.banner.published` + `cms.cache.invalidate.banner` after commit; audit `cms.content.published`)
- [ ] T066 [US1] Wire SchedulePublish + PublishNow slices to `POST /v1/admin/cms/banner-slots/{id}/schedule-publish` + `POST /v1/admin/cms/banner-slots/{id}/publish-now` with `[RequirePermission(CmsPermissions.Publisher)]`, `[RequireIdempotencyKey]`
- [ ] T067 [P] [US1] Create `Modules/Cms/Publisher/ArchiveContent/Handler.cs` for `POST /v1/admin/cms/banner-slots/{id}/archive` — checks `BannerCampaignBinding.binding_state='active'` and rejects with `409 cms.banner.archive_blocked_by_campaign_binding` if so; otherwise transitions to `Archived` with `archive_reason_note ≥ 10 chars`; emits `cms.banner.archived`
- [ ] T068 [P] [US1] Create `Modules/Cms/Services/BannerCtaValidator.cs` invoking `ICatalogProductReadContract` / `ICatalogCategoryReadContract` / `ICatalogBundleReadContract` per `cta_kind`; throws `BannerCtaTargetUnresolvableException` on hard failure (publish path); fail-open on transient errors with `cta_health=transient_unverified` flag (read path)
- [ ] T069 [US1] Create `Modules/Cms/Workers/CmsScheduledPublishWorker.cs` 60s-cadence BackgroundService with Postgres advisory-lock per quickstart.md §1 code skeleton; promotes `Scheduled → Live` (banners + featured + FAQ + blog + legal) and `Live → Archived` (banner only on `scheduled_end_utc`); idempotent per worker idempotency key `(entity_kind, entity_id, target_state)`; capacity-cap re-check before banner promotion (emits `cms.banner.scheduled_publish_blocked_capacity` rate-limited 1/banner/hour and leaves row in `Scheduled` if cap hit)
- [ ] T070 [US1] Wire `CmsScheduledPublishWorker` into `CmsModule.AddCmsModule(...)` as a singleton hosted service; register the advisory-lock helper from `Modules/Shared/Workers/`

**Checkpoint**: At this point, User Story 1 is fully functional and testable independently — banners can be authored, scheduled, published, and archived through their full lifecycle.

---

## Phase 4: User Story 2 — Customer in EG-AR opens storefront and sees live banners + featured section (Priority: P1)

**Goal**: The customer storefront calls the public `GET /v1/storefront/cms/banner-slots` and `GET /v1/storefront/cms/featured-sections` endpoints with `?market=EG&locale=ar`; receives only `live` content matching market-or-`*` and within the schedule window; featured-section refs are live-resolved via catalog read contracts; broken refs filtered silently.

**Independent Test**: Seed a `live` banner in EG-AR + EN, a `draft` banner in EG-AR, an `archived` banner in EG-AR, and a `live` banner in `*`. Call the storefront banner endpoint with `market=EG&locale=ar`; verify only the EG-AR `live` and the `*` `live` rows are returned. Same for featured sections with one stale ref; verify graceful filtering.

### Tests for User Story 2

- [ ] T071 [P] [US2] Contract test for `GET /v1/storefront/cms/banner-slots` happy path + market+locale filter + `cms.storefront.market_unsupported` + `cms.storefront.locale_unsupported` + cache-control headers + ETag stability in `tests/Cms.Tests/Contract/Storefront/ListBannerSlotsContractTests.cs`
- [ ] T072 [P] [US2] Contract test for `GET /v1/storefront/cms/featured-sections` including the live-resolved response shape `{section_id, references_resolved, total_references, total_resolved, total_unavailable}` and `omitted_due_to_unavailable_references=true` on fully-broken sections in `tests/Cms.Tests/Contract/Storefront/ListFeaturedSectionsContractTests.cs`
- [ ] T073 [P] [US2] Integration storefront leak-detection test (SC-003) — seed every non-`live` state across all 5 entity kinds; assert zero leakage on every storefront endpoint in `tests/Cms.Tests/Integration/Storefront/LeakDetectionTests.cs`
- [ ] T074 [P] [US2] Integration test asserting two-tier sort — specific market first, then `*` — across all five storefront endpoints in `tests/Cms.Tests/Integration/Storefront/TwoTierSortTests.cs`
- [ ] T075 [P] [US2] Performance test (SC-006) — 1 000 live banners; 50-row page in p95 ≤ 200 ms in `tests/Cms.Tests/Performance/BannerListPerfTests.cs`
- [ ] T076 [P] [US2] Performance test (SC-007) — 10 000 catalog products + 24-reference featured section; resolution p95 ≤ 300 ms in `tests/Cms.Tests/Performance/FeaturedSectionResolutionPerfTests.cs`

### Implementation for User Story 2

- [ ] T077 [P] [US2] Create `Modules/Cms/Storefront/ListBannerSlots/Query.cs` + `Handler.cs` — uses `StorefrontContentResolver.ApplyStorefrontFilter` to enforce live + window + market+locale tier sort; re-validates `cta_kind ∈ {product, category, bundle}` via `BannerCtaValidator` (read-mode, fail-open on transient); filters broken-CTA banners out and emits `cms.banner.cta_target_broken` (rate-limited 1/banner/hour); resolves `headline` / `subhead` / `asset_id` per requested `locale`
- [ ] T078 [US2] Wire ListBannerSlots slice to `GET /v1/storefront/cms/banner-slots` in `Endpoint.cs` with `[AllowAnonymous]`, `[EnableRateLimiting("cms-storefront")]` (V1 default 600 req/min/IP), `Cache-Control: public, max-age=60, stale-while-revalidate=300`, stable `ETag` header derived from response payload SHA256
- [ ] T079 [P] [US2] Create `Modules/Cms/Services/FeaturedSectionResolver.cs` invoking the three catalog read contracts in parallel (`Task.WhenAll`) for each reference; filters `IsAvailable == false` refs; returns the merged `references_resolved` list with `total_references` / `total_resolved` / `total_unavailable` counters
- [ ] T080 [P] [US2] Create `Modules/Cms/Storefront/ListFeaturedSections/Query.cs` + `Handler.cs` — uses `StorefrontContentResolver` then `FeaturedSectionResolver`; emits `cms.featured_section.partial_broken` (when 0 < unavailable < total) or `cms.featured_section.fully_broken` (when total_resolved == 0) rate-limited 1/section/hour via `last_partial_broken_alert_at_utc` row column update
- [ ] T081 [US2] Wire ListFeaturedSections to `GET /v1/storefront/cms/featured-sections` with `[AllowAnonymous]` + rate-limit + cache headers (mirrors T078)
- [ ] T082 [P] [US2] Create `Modules/Cms/Storefront/MarketLocaleValidator.cs` reading the spec 003 supported-market+locale registry; admin slices use the same validator at save-time (rejects unsupported `?market=fr` etc.); storefront returns `400 cms.storefront.market_unsupported` / `cms.storefront.locale_unsupported`
- [ ] T083 [US2] Configure rate-limit policy `cms-storefront` in `Modules/Cms/CmsModule.cs` — per IP + per `entity_kind` partitioning; V1 default 600 req/min/IP loaded from `appsettings.{env}.json` for environment-specific overrides
- [ ] T084 [P] [US2] Create `Modules/Cms/Storefront/EtagGenerator.cs` deriving stable ETag from response payload hash; integration test asserts ETag stability across two reads of the same content

**Checkpoint**: User Stories 1 + 2 both work independently — banners can be authored AND read by the customer storefront; featured sections render with graceful broken-ref handling.

---

## Phase 5: User Story 3 — `cms.legal_owner` publishes a new privacy-policy version (Priority: P1)

**Goal**: A `cms.legal_owner` authors a new privacy-policy version with both `ar` and `en` bodies, schedules it for the regulator-mandated effective date; at the worker tick the new version transitions to `live` AND the prior `live` version transitions to `superseded` in a single transaction. Indefinite retention; hard-delete forbidden.

**Independent Test**: Author a new privacy version in `draft`. Verify the prior `live` version is unaffected. Schedule-publish at a future time. Advance the worker clock; verify the prior version transitions to `superseded` with `superseded_at_utc` + `superseded_by_version_id` populated and the new version to `live`. Call the version-history endpoint; verify both versions ordered by `effective_at_utc DESC`. Attempt `DELETE` against the prior version; verify `405 cms.legal_page.version.delete_forbidden`.

### Tests for User Story 3

- [ ] T085 [P] [US3] Contract test for `POST /v1/admin/cms/legal-pages/{kind}/versions/drafts` mandatory both bodies + `cms.publish.effective_at_required` + `cms.publish.locale_completeness_missing` in `tests/Cms.Tests/Contract/LegalOwner/SaveLegalPageVersionDraftContractTests.cs`
- [ ] T086 [P] [US3] Integration test for the worker supersession transaction — assert prior `Live → Superseded` and new `Scheduled → Live` happen atomically (a forced rollback via injected fault leaves both rows untouched) in `tests/Cms.Tests/Integration/LegalOwner/LegalPageSupersessionTransactionTests.cs`
- [ ] T087 [P] [US3] Integration test asserting `DELETE /v1/admin/cms/legal-pages/{kind}/versions/{id}` returns `405 cms.legal_page.version.delete_forbidden` for every state (draft / scheduled / live / superseded) in `tests/Cms.Tests/Integration/LegalOwner/LegalPageHardDeleteForbiddenTests.cs`
- [ ] T088 [P] [US3] Contract test for `GET /v1/storefront/cms/legal-pages/{kind}` single-row substitution rule (specific-market `live` → `*` `live` → `404 cms.legal_page.not_found_for_market`) in `tests/Cms.Tests/Contract/Storefront/GetLegalPageContractTests.cs`

### Implementation for User Story 3

- [ ] T089 [P] [US3] Create `Modules/Cms/LegalOwner/SaveLegalPageVersionDraft/Command.cs` + `Handler.cs` — `[RequirePermission(CmsPermissions.LegalOwner)]`; FluentValidation enforces both bodies and effective_at_utc presence at save; xmin guard on update
- [ ] T090 [P] [US3] Create `Modules/Cms/LegalOwner/SchedulePublishLegalPageVersion/Handler.cs` + `Modules/Cms/LegalOwner/PublishLegalPageVersionNow/Handler.cs` — both run LocaleCompletenessGate; PublishNow does the prior-live → superseded transition in the SAME transaction with a `FOR UPDATE SKIP LOCKED` advisory lock per `(legal_page_kind, market_code)` to prevent concurrent publish double-supersede
- [ ] T091 [US3] Wire SaveLegalPageVersionDraft + Schedule + PublishNow + Archive (rare path) to the `POST /v1/admin/cms/legal-pages/{legal_page_kind}/versions/...` route shape per `contracts/cms-contract.md §5`
- [ ] T092 [P] [US3] Create `Modules/Cms/SuperAdmin/PublishCrossMarketLegalPageVersion/Handler.cs` — same publish flow but RBAC-gated to `super_admin` only; rejects `market_code != '*'` with `400 cms.legal_page.scope_not_cross_market`; rejects non-super-admin with `403 cms.legal_page.cross_market_requires_super_admin`
- [ ] T093 [P] [US3] Create `Modules/Cms/LegalOwner/ListLegalPageVersionHistory/Query.cs` + `Handler.cs` — returns ALL versions (live + scheduled + draft + superseded) per `(legal_page_kind, market_code)` ordered by `effective_at_utc DESC`; permits `cms.legal_owner`, `cms.viewer.finance`, `super_admin`
- [ ] T094 [US3] Wire ListLegalPageVersionHistory to `GET /v1/admin/cms/legal-pages/{legal_page_kind}/versions` with the read RBAC matrix
- [ ] T095 [US3] Extend `CmsScheduledPublishWorker` (T069) to handle legal-page supersession path: when a legal_page_version's `Scheduled → Live` transition fires, in the same transaction transition the prior `Live` version of the same `(legal_page_kind, market_code)` to `Superseded` with `superseded_at_utc = now()` + `superseded_by_version_id`; emit `cms.legal_page.version.published` + `cms.legal_page.version.superseded` after commit
- [ ] T096 [P] [US3] Create `Modules/Cms/Storefront/GetLegalPage/Query.cs` + `Handler.cs` — single-row substitution rule per data-model.md §2.5: prefer `(legal_page_kind, market_code=requested) AND state='live'`; fall back to `(legal_page_kind, market_code='*') AND state='live'`; otherwise `404 cms.legal_page.not_found_for_market`
- [ ] T097 [US3] Wire GetLegalPage to `GET /v1/storefront/cms/legal-pages/{kind}` with `[AllowAnonymous]` + cache headers + ETag

**Checkpoint**: User Story 3 is independent of US1/US2 and works on its own — legal page versions can be authored, scheduled, and the worker supersedes prior versions correctly; version history queryable forever.

---

## Phase 6: User Story 4 — `cms.editor` authors and schedules a blog article with SEO + previews via signed token (Priority: P2)

**Goal**: An editor authors a blog article with SEO metadata and (optionally) single-locale; mints a preview token; the storefront renders the draft via the preview URL with `X-Robots-Tag: noindex`; the editor schedules-publish; worker promotes; storefront blog index returns the article.

**Independent Test**: Author a draft article in `ar` only with SEO metadata. Mint a 24h preview token; verify the storefront preview endpoint returns the draft with `X-Robots-Tag: noindex, nofollow` and `preview_banner_marker=true`. Revoke the token; verify subsequent reads return `403`. Schedule-publish for the future. Advance worker; verify article appears in `GET /v1/storefront/cms/blog-articles`.

### Tests for User Story 4

- [ ] T098 [P] [US4] Contract test for `POST /v1/admin/cms/blog-articles/drafts` slug-uniqueness + slug-pattern + body-size limits + SEO field validation in `tests/Cms.Tests/Contract/Editor/SaveBlogArticleDraftContractTests.cs`
- [ ] T099 [P] [US4] Contract test for the preview-token round-trip (mint → read → revoke → read-403) including header assertion on `X-Robots-Tag: noindex, nofollow` in `tests/Cms.Tests/Contract/Preview/PreviewTokenLifecycleContractTests.cs`
- [ ] T100 [P] [US4] Integration test for single-locale blog article — `authored_locale=ar`, request `locale=en` returns `available_locales=['ar']` + `localization_unavailable_for_requested_locale=true` in `tests/Cms.Tests/Integration/Storefront/SingleLocaleBlogArticleTests.cs`
- [ ] T101 [P] [US4] Unit test for `PreviewTokenSigner` — sign + verify round-trip; tampered token rejected; expired token rejected; revoked token rejected; constant-time compare; clock-skew tolerance in `tests/Cms.Tests/Unit/PreviewTokenSignerTests.cs`

### Implementation for User Story 4

- [ ] T102 [P] [US4] Create `Modules/Cms/Editor/SaveBlogArticleDraft/Command.cs` + `Handler.cs` + `Validator.cs` — slug pattern `^[a-z0-9]+(-[a-z0-9]+)*$`, slug-uniqueness check per `(market_code, authored_locale)` returning `400 cms.blog.slug_collision`, body-length cap 60 000 chars, SEO meta_title ≤ 70 chars, meta_description ≤ 160 chars
- [ ] T103 [US4] Wire SaveBlogArticleDraft + PATCH to `POST /v1/admin/cms/blog-articles/drafts` + `PATCH /v1/admin/cms/blog-articles/drafts/{id}`
- [ ] T104 [US4] Extend the SchedulePublish + PublishNow handlers (T064/T065) with blog-article support: LocaleCompletenessGate.CheckPublishable for blog returns `Allowed` with single-locale present; emit `cms.blog_article.published` on `→ Live` transition
- [ ] T105 [P] [US4] Create `Modules/Cms/Storefront/ListBlogArticles/Query.cs` + `Handler.cs` — uses `StorefrontContentResolver`; sorts `published_at_utc DESC`; for single-locale articles when requested `locale != authored_locale`, populates `available_locales[]` + `localization_unavailable_for_requested_locale=true` and returns `body=null`
- [ ] T106 [P] [US4] Create `Modules/Cms/Storefront/GetBlogArticle/Query.cs` + `Handler.cs` (single-article read by slug); same locale-availability semantics
- [ ] T107 [US4] Wire ListBlogArticles + GetBlogArticle to `GET /v1/storefront/cms/blog-articles` + `GET /v1/storefront/cms/blog-articles/{slug}` with `[AllowAnonymous]` + rate-limit + cache headers
- [ ] T108 [P] [US4] Create `Modules/Cms/Preview/MintPreviewToken/Command.cs` + `Handler.cs` — generate cryptographically random 160-byte opaque token; HMAC-SHA256-sign per research.md §R3 with the layered-config secret; persist `cms.preview_tokens` row with `token_hash = sha256(token)`; return `{token, url, expires_at_utc, token_hash}`; rate-limited 30/h/actor; audit `cms.preview_token.minted`
- [ ] T109 [P] [US4] Create `Modules/Cms/Preview/RevokePreviewToken/Command.cs` + `Handler.cs` — finds row by `token_hash`; idempotent (re-call returns 200 with original `revoked_at_utc`); audit `cms.preview_token.revoked`
- [ ] T110 [US4] Wire MintPreviewToken + RevokePreviewToken to `POST /v1/admin/cms/{kind}/{id}/preview-token` + `DELETE /v1/admin/cms/preview-token/{token_hash}`
- [ ] T111 [P] [US4] Create `Modules/Cms/Preview/ReadPreviewedDraft/Query.cs` + `Handler.cs` — verifies HMAC signature (constant-time), looks up token-store row by `token_hash`, checks `expires_at_utc > now() AND revoked_at_utc IS NULL`, loads draft entity, sets `X-Robots-Tag: noindex, nofollow` response header, injects `preview_banner_marker=true` field into response
- [ ] T112 [US4] Wire ReadPreviewedDraft to `GET /v1/storefront/cms/preview/{kind}/{id}?token=...` with `[AllowAnonymous]` + tighter rate-limit policy `cms-preview` (60 req/min/IP)
- [ ] T113 [P] [US4] Create `Modules/Cms/Workers/CmsPreviewTokenCleanupWorker.cs` — daily cadence at 04:00 UTC; advisory lock `cms-preview-token-cleanup`; deletes `cms.preview_tokens` rows where `expires_at_utc + INTERVAL '30 days' < now()`; idempotent; this is the only hard-delete path for preview tokens (FR-016)
- [ ] T114 [US4] Wire `CmsPreviewTokenCleanupWorker` into `CmsModule.AddCmsModule(...)` as singleton hosted service

**Checkpoint**: User Story 4 ready independently — blog articles can be authored, previewed via signed token (with revocation), and published.

---

## Phase 7: User Story 5 — `cms.editor` curates a featured section that gracefully handles a stale catalog reference (Priority: P2)

**Goal**: An editor builds a featured section with 4 catalog references; the storefront live-resolves and silently filters out broken refs while the admin authoring UI shows a per-row "live preview" badge for each reference's current availability.

**Independent Test**: Seed a featured section with 4 product refs. Archive one product in catalog (or stub the contract to return `unavailable` for one id). Call the storefront featured-sections endpoint; verify 3 entries returned + `cms.featured_section.partial_broken` event emitted (rate-limited). Confirm the admin authoring read shows the broken-ref badge.

### Tests for User Story 5

- [ ] T115 [P] [US5] Contract test for `POST /v1/admin/cms/featured-sections/drafts` — `cms.featured_section.empty_references` + `cms.featured_section.too_many_references` + `cms.featured_section.reference_kind_unsupported` in `tests/Cms.Tests/Contract/Editor/SaveFeaturedSectionDraftContractTests.cs`
- [ ] T116 [P] [US5] Integration test asserting partial-broken-refs filter behavior + rate-limited `cms.featured_section.partial_broken` event emission (1/section/hour) in `tests/Cms.Tests/Integration/Storefront/FeaturedSectionPartialBrokenTests.cs`
- [ ] T117 [P] [US5] Integration test asserting fully-broken section returns `omitted_due_to_unavailable_references=true` and emits `cms.featured_section.fully_broken` in `tests/Cms.Tests/Integration/Storefront/FeaturedSectionFullyBrokenTests.cs`

### Implementation for User Story 5

- [ ] T118 [P] [US5] Create `Modules/Cms/Editor/SaveFeaturedSectionDraft/Command.cs` + `Handler.cs` + `Validator.cs` — `references[]` length validation (min 1, max `CmsMarketSchema.featured_section_max_references`); `kind ∈ {product, category, bundle}` enum validation; jsonb persist
- [ ] T119 [US5] Wire SaveFeaturedSectionDraft + PATCH to `POST /v1/admin/cms/featured-sections/drafts` + `PATCH /v1/admin/cms/featured-sections/drafts/{id}`
- [ ] T120 [US5] Extend SchedulePublish + PublishNow handlers (T064/T065) for featured sections: at publish, call `FeaturedSectionResolver` once to assert at least one reference resolves; reject `400 cms.featured_section.empty_references` if all unavailable
- [ ] T121 [P] [US5] Create `Modules/Cms/Editor/GetFeaturedSectionAdminDetail/Query.cs` + `Handler.cs` — admin-side read returning per-reference availability badge by calling the catalog read contracts; surfaces broken-ref count in the response for the authoring UI
- [ ] T122 [US5] Wire GetFeaturedSectionAdminDetail to `GET /v1/admin/cms/featured-sections/{id}` with `[RequirePermission(CmsPermissions.Editor)]`
- [ ] T123 [P] [US5] Add rate-limit guard for `cms.featured_section.partial_broken` events in `FeaturedSectionResolver.cs` — uses `last_partial_broken_alert_at_utc` row column updated atomically; SKIPS event emission when `now() < last_partial_broken_alert_at_utc + INTERVAL '1 hour'`

**Checkpoint**: User Story 5 ready independently — featured sections handle broken catalog refs gracefully both at storefront-read time and in the admin authoring UI.

---

## Phase 8: User Story 6 — `cms.editor` curates the FAQ across markets and locales (Priority: P2)

**Goal**: An editor authors FAQ entries with both `ar` + `en` Q&A, picks a category, sets display_order, scopes to a market; publishes; the storefront FAQ endpoint returns ordered live entries; bulk-reorder works with concurrency safety.

**Independent Test**: Seed 12 FAQ entries across 4 categories in EG-AR + EN. Call the storefront FAQ endpoint with various category filters; verify ordering and bilingual response. Reorder entries via the admin bulk-update; verify the new order persists; trigger a concurrent reorder and assert the loser sees `409 cms.faq.reorder_conflict`.

### Tests for User Story 6

- [ ] T124 [P] [US6] Contract test for `POST /v1/admin/cms/faq-entries/drafts` mandatory both Q&A locales at publish in `tests/Cms.Tests/Contract/Editor/SaveFaqEntryDraftContractTests.cs`
- [ ] T125 [P] [US6] Contract test for `POST /v1/admin/cms/faq-entries/reorder` happy path + concurrent reorder race producing `409 cms.faq.reorder_conflict` (xmin guard) in `tests/Cms.Tests/Contract/Editor/BulkReorderFaqEntriesContractTests.cs`
- [ ] T126 [P] [US6] Integration test for storefront FAQ ordering — `display_order ASC` then `created_at_utc ASC`; market+locale filtering; collisions allowed in `tests/Cms.Tests/Integration/Storefront/FaqOrderingTests.cs`

### Implementation for User Story 6

- [ ] T127 [P] [US6] Create `Modules/Cms/Editor/SaveFaqEntryDraft/Command.cs` + `Handler.cs` + `Validator.cs` — Q ≤ 250 chars, A ≤ 4000 chars markdown, category enum validation
- [ ] T128 [US6] Wire SaveFaqEntryDraft + PATCH to `POST /v1/admin/cms/faq-entries/drafts` + `PATCH /v1/admin/cms/faq-entries/drafts/{id}`
- [ ] T129 [P] [US6] Create `Modules/Cms/Editor/BulkReorderFaqEntries/Command.cs` + `Handler.cs` — accepts `entries[]` of `{id, display_order, xmin}`; runs in a single transaction; per-row xmin precondition; if ANY row fails the xmin check, rollback the entire batch and return `409 cms.faq.reorder_conflict` with the current state of all affected rows
- [ ] T130 [US6] Wire BulkReorderFaqEntries to `POST /v1/admin/cms/faq-entries/reorder`
- [ ] T131 [P] [US6] Create `Modules/Cms/Storefront/ListFaqEntries/Query.cs` + `Handler.cs` — uses `StorefrontContentResolver`; secondary sort `display_order ASC, created_at_utc ASC`; `category` query param filter
- [ ] T132 [US6] Wire ListFaqEntries to `GET /v1/storefront/cms/faq` with `[AllowAnonymous]` + rate-limit + cache headers
- [ ] T133 [US6] Extend SchedulePublish + PublishNow handlers (T064/T065) for FAQ entries: LocaleCompletenessGate enforces both Q&A locales

**Checkpoint**: User Story 6 ready independently — FAQ entries can be authored, reordered safely under concurrency, and read via the storefront.

---

## Phase 9: User Story 7 — `cms-v1` seeder for staging and local development (Priority: P3)

**Goal**: A developer or QA engineer runs `seed --dataset=cms-v1 --mode=apply`; the seeder creates synthetic content spanning all 5 entity kinds × 4 lifecycle states with bilingual coverage and per-market + cross-market `*` examples; idempotent across runs.

**Independent Test**: `seed --dataset=cms-v1 --mode=apply` against a fresh staging DB; verify per-state distribution; verify per-market + per-locale coverage; verify storefront reads against EG and KSA return non-empty responses; verify legal page version-history shows 2 versions per `(kind, market)`. Re-run the seeder; verify idempotency (no duplicates).

### Tests for User Story 7

- [ ] T134 [P] [US7] Integration test for `cms-v1` seeder distribution (SC-009) — per-state row counts ≥ 1 across all 5 entity kinds; bilingual coverage on banner / featured / FAQ / legal; per-market + `*` coverage; legal page version-history with 2 versions per `(kind, market)`; total runtime ≤ 20 s in `tests/Cms.Tests/Integration/Seeding/CmsV1DevSeederDistributionTests.cs`
- [ ] T135 [P] [US7] Integration test asserting seeder idempotency — running twice produces identical row counts (no duplicates) in `tests/Cms.Tests/Integration/Seeding/CmsV1DevSeederIdempotencyTests.cs`
- [ ] T136 [P] [US7] Integration test asserting `--mode=dry-run` exits 0 with planned-changes report and writes nothing to DB in `tests/Cms.Tests/Integration/Seeding/CmsV1DevSeederDryRunTests.cs`
- [ ] T137 [P] [US7] CI integration: `seed-pii-guard` smoke against the seeded dataset asserting no real-phone / real-email / national-ID patterns in `tests/Cms.Tests/Integration/Seeding/CmsV1DevSeederPiiGuardTests.cs`

### Implementation for User Story 7

- [ ] T138 [P] [US7] Create `Modules/Cms/Seeding/CmsV1DevSeeder.cs` skeleton with `SeedGuard` (refuses to run in Production); accepts `--mode=apply|dry-run`; orchestrates the per-kind seeders below
- [ ] T139 [P] [US7] Create `Modules/Cms/Seeding/Datasets/CmsV1Banners.cs` — 6 synthetic banners across `hero_top` / `category_strip` / `footer_strip` / `home_secondary` slot kinds in `live` + `scheduled` + `draft` + `archived` states across both EG + KSA + `*`; bilingual headlines + assets; idempotent on `(slot_kind, market_code, headline_en)` natural key
- [ ] T140 [P] [US7] Create `Modules/Cms/Seeding/Datasets/CmsV1FeaturedSections.cs` — 4 featured sections referencing seeded products + bundles (re-using product ids from `catalog-v1`); bilingual titles
- [ ] T141 [P] [US7] Create `Modules/Cms/Seeding/Datasets/CmsV1FaqEntries.cs` — 12 FAQ entries across all 8 categories in EG-AR/EN + KSA-AR/EN
- [ ] T142 [P] [US7] Create `Modules/Cms/Seeding/Datasets/CmsV1LegalPages.cs` — 4 legal page kinds (terms / privacy / returns / cookies) each with 2 versions (one prior `superseded`, one current `live`) for each market (EG + KSA + a `*` baseline)
- [ ] T143 [P] [US7] Create `Modules/Cms/Seeding/Datasets/CmsV1BlogArticles.cs` — 6 articles across categories `tips` / `news` / `guides` in mixed authored locales (some bilingual via dual rows, some single-locale); SEO blocks populated
- [ ] T144 [US7] Wire `CmsV1DevSeeder` into `services/backend_api/Modules/Bootstrap/DevSeederHost.cs` registry alongside the existing `SupportV1DevSeeder`; document the `seed --dataset=cms-v1` CLI invocation in `Modules/Cms/Seeding/README.md`

**Checkpoint**: All user stories should now be independently functional. CMS module ships with realistic seed data for staging and local development.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Subscribers, asset-GC + stale-draft workers, the admin governance endpoints (delete-draft / dismiss-stale / reassign-ownership / edit-market-schema / orphaned-assets), bilingual editorial pass, OpenAPI regeneration, audit-coverage script, and DoD verification.

### Cross-module subscribers (Phase M)

- [ ] T145 [P] Create `Modules/Cms/Subscribers/CampaignDeactivatedHandler.cs` — `INotificationHandler<CampaignDeactivated>` from spec 007-b's bus channel; finds active `BannerCampaignBinding` rows where `campaign_id = event.campaign_id`; stamps each with `released_at_utc = now()` + `binding_state = 'released_due_to_campaign_deactivation'`; emits `cms.banner.campaign_unbound` audit
- [ ] T146 [P] Create `Modules/Cms/Subscribers/EditorRoleRevokedHandler.cs` — `INotificationHandler<CustomerRoleRevoked>` from spec 004's role-lifecycle bus; sets `ownership_orphaned=true` on all drafts (across all 5 entity kinds) where `owner_actor_id = event.actor_id` AND state ∈ {Draft, Scheduled}; emits `cms.draft.ownership_orphaned`
- [ ] T147 Create `Modules/Cms/Publisher/BindBannerToCampaign/Handler.cs` and `Modules/Cms/Publisher/UnbindBannerFromCampaign/Handler.cs` — bind creates a `BannerCampaignBinding` row with `binding_state='active'`; rejects `400 cms.banner.campaign_already_bound` if banner has an active binding (1:1 in V1); unbind stamps `released_at_utc` + `binding_state='released_by_editor'`; both audited
- [ ] T148 Wire BindBannerToCampaign + UnbindBannerFromCampaign to `POST /v1/admin/cms/banner-slots/{id}/bind-campaign` + `POST /v1/admin/cms/banner-slots/{id}/unbind-campaign`

### Asset GC + stale-draft workers (Phase N)

- [ ] T149 [P] Create `Modules/Cms/Workers/CmsAssetGarbageCollectorWorker.cs` — daily 02:00 UTC cadence; advisory lock `cms-asset-gc`; for each `cms.assets` row with `storage_object_state='active'` AND `dereferenced_at_utc IS NOT NULL` AND `dereferenced_at_utc + INTERVAL '<grace-days>' < now()`, recount references across all 5 entity tables via a single union SQL; if count = 0, call spec 015 storage abstraction to delete the storage object, set `storage_object_state='swept'` + `swept_at_utc = now()`, audit `cms.asset.swept`
- [ ] T150 [P] Create `Modules/Cms/Workers/CmsStaleDraftAlertWorker.cs` — daily 03:00 UTC cadence; advisory lock `cms-stale-draft-alert`; flags drafts where `editor_save_at_utc + INTERVAL '<alert-days>' < now()` AND `last_stale_alert_dismissed_at_utc IS NULL OR last_stale_alert_dismissed_at_utc + INTERVAL '<alert-days>' < now()`; emits `cms.draft.stale_alert` (idempotent on `(draft_id, alert_window_start_utc)`); rate-limited 1/draft/week
- [ ] T151 [P] Create `Modules/Cms/Editor/DeleteUnpublishedDraft/Handler.cs` — FR-005a draft-delete path; rejects `405 cms.{kind}.delete_forbidden` for non-`Draft` rows; rejects `403 cms.draft.delete_not_owner` for actors who are neither the creator nor `super_admin`; rate-limited 30/h/actor; emits `cms.draft.deleted` + `cms.asset.dereferenced` for each `asset_id` on the deleted row
- [ ] T152 Wire DeleteUnpublishedDraft to `DELETE /v1/admin/cms/{kind}/drafts/{id}` (per `contracts/cms-contract.md §3.10`)
- [ ] T153 [P] Create `Modules/Cms/Editor/DismissStaleDraftAlert/Handler.cs` — `reason_note ≥ 10 chars`; stamps `last_stale_alert_dismissed_at_utc`; audit `cms.draft.stale_alert_dismissed`
- [ ] T154 Wire DismissStaleDraftAlert to `POST /v1/admin/cms/drafts/{id}/dismiss-stale-alert`
- [ ] T155 [P] Create `Modules/Cms/Publisher/ReassignDraftOwnership/Handler.cs` — `[RequirePermission(CmsPermissions.Publisher)]`; updates `owner_actor_id` + `ownership_orphaned=false`; audit `cms.draft.ownership_reassigned`; rejects `404 cms.draft.target_actor_not_a_cms_editor` if target lacks `cms.editor` role
- [ ] T156 Wire ReassignDraftOwnership to `POST /v1/admin/cms/drafts/{id}/reassign-ownership`
- [ ] T157 Wire `CmsAssetGarbageCollectorWorker` + `CmsStaleDraftAlertWorker` into `CmsModule.AddCmsModule(...)` as singleton hosted services
- [ ] T158 [P] Create `Modules/Cms/Editor/ListMyDrafts/Query.cs` + `Handler.cs` — lists drafts authored by the current `actor_id`; filters `entity_kind`, `market_code`, `stale=true`, `ownership_orphaned=true`; page 50 max 200
- [ ] T159 Wire ListMyDrafts to `GET /v1/admin/cms/drafts`

### Super-admin endpoints (Phase J)

- [ ] T160 [P] Create `Modules/Cms/SuperAdmin/EditMarketSchema/Handler.cs` — xmin-guarded; range checks per data-model.md §2.9 CHECK constraints; rejects `400 cms.market_schema.value_out_of_range` and `409 cms.market_schema.version_conflict`; audit
- [ ] T161 Wire EditMarketSchema to `PATCH /v1/admin/cms/market-schemas/{market_code}`
- [ ] T162 [P] Create `Modules/Cms/SuperAdmin/ListOrphanedAssets/Query.cs` + `Handler.cs` — returns dereferenced assets currently in grace window (FR-009a observability)
- [ ] T163 Wire ListOrphanedAssets to `GET /v1/admin/cms/orphaned-assets`

### Metrics + audit-coverage

- [ ] T164 [P] Create `Modules/Cms/Metrics/CmsMetricsHandler.cs` exposing per-kind active-content counts + broken-reference counts (featured + banner CTAs) + stale draft counts + ownership_orphaned counts + pending-scheduled counts + preview-token counts (active / expired pending cleanup) + asset counts (active / swept / in-grace-window) per `contracts/cms-contract.md §9`
- [ ] T165 Wire CmsMetricsHandler to `GET /v1/admin/cms/metrics` permitted by `super_admin` or `cms.viewer.finance`

### Audit + bilingual + OpenAPI

- [ ] T166 [P] Create `Modules/Cms/Messages/cms.en.icu` and `cms.ar.icu` covering every state label / category label / validation badge / broken-CTA flag / stale-draft alert / JSON-LD scaffolding key referenced by the slices; AR strings flagged in `Modules/Cms/Messages/AR_EDITORIAL_REVIEW.md` for editorial review per Principle 4 (SC-008 30-screen checklist)
- [ ] T167 [P] Regenerate `services/backend_api/openapi.cms.json` via the existing `dotnet swagger tofile` toolchain; assert congruence with `contracts/cms-contract.md` (every endpoint, every reason code, every cross-module event); commit the generated artifact
- [ ] T168 [P] Create `tests/Cms.Tests/Integration/AuditCoverageScriptTests.cs` — runs through every state-transitioning slice and asserts a matching audit row appears in `audit.audit_log_entries` (SC-002 verification across all 19 audit-event kinds)
- [ ] T169 [P] Create `tests/Cms.Tests/Integration/IdempotencyEnvelopeTests.cs` — replays every state-transitioning POST with the same `Idempotency-Key` and asserts the second call returns the original response (FR-033)
- [ ] T170 [P] Create `tests/Cms.Tests/Integration/HardDeleteProhibitionTests.cs` — for every entity kind in every non-`draft` state, assert `DELETE` returns `405 cms.{kind}.delete_forbidden` (SC-011)
- [ ] T171 [P] Create `tests/Cms.Tests/Integration/Workers/WorkerIdempotencyStressTest.cs` — 100-iteration repeat-worker stress test against a backdated row; asserts exactly 1 transition + 1 audit row + 1 emitted event per `(entity_kind, entity_id, target_state)` tuple (SC-005)

### Observability + final checks

- [ ] T172 Add OpenTelemetry traces for the four workers + the storefront resolver path + the banner-CTA validator (instrument so spec 028 analytics can cross-reference); reuse the existing `OtelExtensions` from spec 003
- [ ] T173 Verify `seed-pii-guard` CI check passes for `cms-v1` seeded dataset (no real phones / emails / national ID patterns)
- [ ] T174 Run quickstart.md walkthrough (banner US1 path + legal page US3 path + preview-token US4 path) end-to-end against a live Testcontainers Postgres + the WebApplicationFactory harness; document any drift between the quickstart prose and the implemented endpoint
- [ ] T175 Constitution + ADR fingerprint via `scripts/compute-fingerprint.sh`; attach fingerprint to PR description (Guardrail #3)
- [ ] T176 Run lint + format + contract-diff CI checks (Guardrails #1 + #2); resolve any drift
- [ ] T177 Run the full Cms.Tests Testcontainers integration suite; assert all SCs (SC-001 through SC-011) pass; capture timings for SC-006 and SC-007 perf targets in CI artifacts
- [ ] T178 Update `docs/dod.md` referenced by the project README to confirm 024 DoD checklist (per quickstart.md §5) is complete; checklist all items checked
- [ ] T179 Verify no existing test from specs 020 / 021 / 022 / 023 / 007-b regresses by running `dotnet test` at repo root and comparing the test count + pass-count to `main`'s baseline

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. T006–T055.
- **User Story 1 (Phase 3)** P1: Depends on Foundational. T056–T070.
- **User Story 2 (Phase 4)** P1: Depends on Foundational AND on US1's `BannerSlot` entity / `SaveBannerDraft` slice for seed-fixture data (storefront read tests need rows to read). T071–T084. Independent at the contract level — the leak-prevention resolver (Phase 2 Phase E) is the actual gate.
- **User Story 3 (Phase 5)** P1: Depends on Foundational. T085–T097. Independent of US1/US2/US4/US5/US6 — different entity kind.
- **User Story 4 (Phase 6)** P2: Depends on Foundational. T098–T114. Reuses the SchedulePublish / PublishNow handlers built in US1 (extend, not duplicate).
- **User Story 5 (Phase 7)** P2: Depends on Foundational AND on US2's `FeaturedSectionResolver` (storefront-side); the authoring slices in US5 are independent. T115–T123.
- **User Story 6 (Phase 8)** P2: Depends on Foundational AND on Phase 2 generic SchedulePublish / PublishNow extension paths. T124–T133.
- **User Story 7 (Phase 9)** P3: Depends on ALL US1–US6 deliverables (the seeder needs every kind's authoring slice to populate its dataset; an alternative is a direct-DB seed bypassing slices, but the chosen approach matches spec 023 pattern). T134–T144.
- **Polish (Phase 10)**: Depends on US1–US6 (US7 may run in parallel). Subscribers can run in parallel with user stories once their respective bus channels exist. T145–T179.

### User Story Dependencies

- **US1 (P1)**: First MVP slice. No dependencies on other stories.
- **US2 (P1)**: Independent at the contract level; benefits from US1 fixtures for tests.
- **US3 (P1)**: Independent — different entity kind; reuses Phase 2 primitives.
- **US4 (P2)**: Reuses US1's PublishNow / SchedulePublish path; independent for blog-specific surface.
- **US5 (P2)**: Reuses US2's FeaturedSectionResolver; independent for authoring surface.
- **US6 (P2)**: Independent.
- **US7 (P3)**: Depends on US1–US6 entity surfaces.

### Within Each User Story

- Tests SHOULD be written and SHOULD FAIL before implementation (TDD-friendly project posture; not strictly enforced).
- Entities + primitives before handlers (Phase 2 unblocks the rest).
- Handlers before endpoints (Endpoint files thin-wrap handlers).
- Slice-implementation before integration tests at the WebApplicationFactory level.
- Workers after their underlying state machine + entity is in place.

### Parallel Opportunities

- All Setup tasks marked [P] (T002, T003, T004) can run in parallel after T001.
- All Foundational primitives [P] (T006–T026) can run in parallel.
- All Foundational entities [P] (T027–T035) can run in parallel.
- All Foundational entity configurations (T037) can run in parallel as one task with sub-files.
- All Cross-module shared declarations [P] (T043–T048) can run in parallel.
- Most user-story tasks marked [P] within a phase can run in parallel.
- Different user stories (US1–US6) can be worked on in parallel by different team members once Foundational completes.
- Polish-phase subscribers + workers + super-admin endpoints + metrics + audit tasks (T145–T171) are highly parallelizable.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 in parallel (TDD posture):
Task: "Contract test for SaveBannerDraft in tests/Cms.Tests/Contract/Editor/SaveBannerDraftContractTests.cs"
Task: "Contract test for PatchBannerDraft in tests/Cms.Tests/Contract/Editor/PatchBannerDraftContractTests.cs"
Task: "Contract test for ScheduleBannerPublish in tests/Cms.Tests/Contract/Publisher/ScheduleBannerPublishContractTests.cs"
Task: "Integration test for banner lifecycle end-to-end"
Task: "Integration test for banner capacity race"

# After Phase 2 completes, launch all US1 implementation tasks marked [P] in parallel:
Task: "Modules/Cms/Editor/SaveBannerDraft/Command.cs + Handler.cs"
Task: "Modules/Cms/Editor/SaveBannerDraft/Validator.cs"
Task: "Modules/Cms/Publisher/SchedulePublish/Handler.cs"
Task: "Modules/Cms/Publisher/PublishNow/Handler.cs"
Task: "Modules/Cms/Publisher/ArchiveContent/Handler.cs"
Task: "Modules/Cms/Services/BannerCtaValidator.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T005).
2. Complete Phase 2: Foundational (T006–T055) — CRITICAL, blocks all stories.
3. Complete Phase 3: User Story 1 (T056–T070) — banner authoring + scheduled publish + worker promotion.
4. **STOP and VALIDATE**: Test US1 independently against the smoke quickstart.md §1.
5. Demo banner end-to-end lifecycle as the MVP.

### Incremental Delivery

1. Foundation (Phases 1 + 2) → backend skeleton ready.
2. US1 (banner schedule + publish) → MVP demo.
3. US2 (storefront read) → storefront integration ready for spec 014.
4. US3 (legal page versioning) → regulatory readiness.
5. US4 (blog + preview token) → editorial workflow ready.
6. US5 (featured-section curation) → home-page composition ready.
7. US6 (FAQ) → customer help-surface ready (feeds spec 023's Help panel + spec 014's more-screen).
8. US7 (seeder) → staging populated for QA.
9. Polish (Phase 10) → governance endpoints, GC + stale-draft workers, audit-coverage, OpenAPI, DoD.

### Parallel Team Strategy

With multiple developers and Foundational complete:
- Developer A: US1 (banner) + US2 (storefront banner) integration.
- Developer B: US3 (legal page versioning).
- Developer C: US4 (blog + preview token).
- Developer D: US5 (featured-section authoring) + US6 (FAQ).
- Developer A: US7 (seeder) once US1–US6 complete.
- Polish (Phase 10) split across team for subscribers, workers, super-admin, metrics, OpenAPI, audit-coverage.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- Each user story should be independently completable and testable.
- Verify tests fail before implementing (TDD-friendly posture).
- Commit after each task or logical group (per Guardrail discipline).
- Stop at any checkpoint to validate story independently.
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence.
- Project-memory rule applies: every new module's `AddDbContext(...)` MUST suppress `ManyServiceProvidersCreatedWarning` (spec 023 noted it; 024 carries it forward in T036).
- Workers MUST use Postgres advisory locks for horizontal coordination (project pattern from spec 020); the lock key is the worker class name.
- Storefront leak-detection is the SC-003 gate; the `StorefrontContentResolver` (T053) is the single chokepoint that makes leakage impossible by construction. Leak-detection tests (T073) confirm.
- Featured-section ref resolution is via `Modules/Shared/` catalog read contracts (loose-coupling pattern from specs 020/021/022/023). Stubs (`Fake*ReadContract`) cover the case where spec 005 isn't on `main`; see T043–T045.
- Hard-delete prohibition is enforced at three layers: API (T152 returns 405), DB triggers (T039–T040), and integration tests (T170).
- The four workers (T069 scheduled-publish, T113 preview-token cleanup, T149 asset GC, T150 stale-draft alert) are all idempotent on stable keys per research.md §R12; SC-005 stress test verifies idempotency under crash-recovery / horizontal pod scaling.
- Total tasks: 179.
