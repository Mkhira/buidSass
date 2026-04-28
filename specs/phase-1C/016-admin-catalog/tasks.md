---

description: "Tasks for Spec 016 — Admin Catalog"
---

# Tasks: Admin Catalog

**Input**: Design documents from `/specs/phase-1C/016-admin-catalog/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{consumed-apis.md,routes.md,client-events.md,csv-format.md}, quickstart.md

**Tests**: vitest + RTL unit/component, Playwright + Storybook for visual regression (SC-003), axe-playwright for a11y, Playwright e2e for Stories 1/2/4. Inherits spec 015's CI / lint hygiene unchanged.

**Organization**: Tasks grouped by user story. Stories run in priority order P1 → P4.

## Format

`[ID] [P?] [Story] Description (path)`

- `[P]` — parallelizable
- `[USn]` — user story (US1 = P1 MVP)

## Path conventions

- App code: `apps/admin_web/`
- Catalog feature lives under `apps/admin_web/app/(admin)/catalog/`, `apps/admin_web/components/catalog/`, `apps/admin_web/lib/catalog/`

---

## Phase 1: Setup (catalog-specific deps + scaffolding)

- [X] T001 Catalog deps already on `apps/admin_web/package.json` from spec 015 (dnd-kit, react-arborist, papaparse, idb, react-textarea-autosize). **Tiptap + Uppy deferred** — bundle-budget aware; replaced by `<Textarea>` and a lighter `UploadManager`.
- [X] T002 [P] `@types/papaparse` already in devDependencies.
- [X] T003 [P] `pnpm install` clean.
- [X] T004 [P] Feature dirs scaffolded.

---

## Phase 2: Foundational (catalog-specific shared infrastructure)

⚠️ Required before any user-story phase begins.

### Generated client + state machines

- [X] T005 `lib/api/clients/catalog.ts` covers products / categories / brands / manufacturers / bulk-import (from 015). Restore-revision + report-download + export-stream **deferred**.
- [X] T006 `lib/catalog/product-state.ts` — SM-1 with `allowedTransitions` / `canTransition` / `pillFor`. 6/6 tests.
- [X] T007 [P] `lib/catalog/bulk-import-session.ts` — SM-2 with `canCommit` / `progressStepFor` / `isTerminal`. 4/4 tests.
- [X] T008 [P] `lib/catalog/category-tree-mutation.ts` — `applyMovesLocally` + `diffMoves`. 3/3 tests.

### CSV + uploads

- [X] T009 `lib/catalog/csv.ts` — `parseHeader` / `parseRows` / `serializeReport` / `detectSchemaMismatch`. 6/6 tests.
- [X] T010 `lib/catalog/upload-manager.ts` — `fetch` + IndexedDB shim (Uppy deferred for bundle budget).
- [ ] T011 [P] upload-manager.test.ts — **deferred**.

### Catalog navigation contribution

- [X] T012 [P] `nav-manifest-static/catalog.json` registered. `pnpm catalog:check-nav-manifest` reports "2 module contributions, in sync".
- [X] T013 [P] Permission rules already wired in 015's `lib/auth/permissions.ts`.

### i18n keys

- [X] T014 [P] `messages/en.json` extended with `catalog.*` + `nav.entry.catalog_*`. i18n key-parity test passes.
- [X] T015 [P] `messages/ar.json` mirrored with `EN_PLACEHOLDER` markers.

### Catalog overview page

- [X] T016 `app/(admin)/catalog/layout.tsx` — `requireAnyPermission` over five catalog read keys.
- [X] T017 `app/(admin)/catalog/page.tsx` — five overview cards.

**Checkpoint**: foundation ready.

---

## Phase 3: User Story 1 — Create and publish a product end-to-end (Priority: P1) 🎯 MVP

**Goal**: Admin creates → uploads media → saves draft → publishes; product appears in customer app.

**Independent Test**: Playwright e2e walks the entire flow against the docker-compose backend.

### Product list

- [X] T018 [US1] Products list Server Component fetches first page; surfaces ErrorState on failure.
- [X] T019 [US1] [P] `<ProductListTable>` wraps `<DataTable>` with sku/name/state/restricted columns + URL-synced search.
- [X] T020 [US1] [P] `<ProductStatePill>` with tone mapping for draft/scheduled/published/discarded.
- [ ] T021 [US1] [P] Component tests + Storybook — **deferred**.
- [ ] T022 [US1] [P] Visual regression — **deferred**.

### Product editor

- [X] T023 [US1] `app/(admin)/catalog/products/{new,[productId]}/page.tsx` Server Components.
- [X] T024 [US1] `<ProductEditorForm>` — react-hook-form + zod, dirty-state guard, conflict reload dialog, AuditForResourceLink in header.
- [X] T025 [US1] [P] `<LocaleTabs>` — both panels mounted across switches.
- [ ] T026 [US1] [P] Tiptap editor — **deferred** (Tiptap dep not installed; stub via `<Textarea>`).
- [ ] T027 [US1] [P] Attribute-fields — **deferred** (depends on category-driven attribute schema endpoint).
- [X] T028 [US1] [P] `<RestrictedFlagSection>` — required AR/EN rationale enforced by zod superRefine.
- [X] T029 [US1] [P] `<PublishControls>` — gated by SM-1's `allowedTransitions(state)`.
- [X] T030 [US1] [P] Conflict overlay reuses 015's `<ConflictReloadDialog>` with preserved-fields slot.

### Media + documents

- [ ] T031 [US1] Media picker — **deferred** (Uppy not installed).
- [ ] T032 [US1] [P] Document uploader — **deferred**.
- [ ] T033 [US1] [P] Variant preview — **deferred**.
- [ ] T034 [US1] [P] media-picker tests — **deferred**.

### Server-side glue

- [X] T035 [US1] `/api/catalog/products/[id]/upload-url/route.ts` — returns 501 with gap reason until spec 003 storage signed-URL issuer ships.
- [X] T036 [US1] [P] `/api/catalog/products/[id]/publish/route.ts` — permission-gated proxy.
- [X] T037 [US1] [P] `/api/catalog/products/[id]/schedule/route.ts` — folds into /publish with scheduledAt body.

### Tests + e2e

- [X] T038 [US1] [P] product-state.test.ts (6) + bulk-import-session.test.ts (4) + csv.test.ts (6) + category-tree-mutation.test.ts (3) — 19/19 pass. Editor-form widget test deferred.
- [ ] T039 [US1] [P] Storybook stories — **deferred**.
- [ ] T040 [US1] [P] Visual regression — **deferred**.
- [ ] T041 [US1] [P] axe a11y — **deferred**.
- [ ] T042 [US1] Playwright e2e — **deferred** (needs docker-compose backend).

**Checkpoint**: US1 (MVP) ships independently.

---

## Phase 4: User Story 2 — Manage the category tree (Priority: P2)

**Goal**: drag-reorder / add / deactivate / label-edit on the category tree.

- [ ] T043 [US2] Create `apps/admin_web/app/(admin)/catalog/categories/page.tsx` (Server Component renders initial tree fetch).
- [ ] T044 [US2] Create `apps/admin_web/components/catalog/category/category-tree.tsx` composing `react-arborist` + `@dnd-kit/core` per R1. Per FR-007a, mount `<AuditForResourceLink resourceType="Category" resourceId={selectedCategoryId} />` in the page header when a category is selected.
- [ ] T045 [US2] [P] Create `apps/admin_web/components/catalog/category/category-row.tsx` (label edit inline, action menu).
- [ ] T046 [US2] [P] Create `apps/admin_web/components/catalog/category/deactivate-dialog.tsx` (count + impact warning).
- [ ] T047 [US2] [P] Create `apps/admin_web/components/catalog/category/add-category-dialog.tsx`.
- [ ] T048 [US2] [P] Create `apps/admin_web/lib/catalog/category-dnd-adapter.ts` glueing the two libraries.
- [ ] T049 [US2] [P] Create `tests/unit/catalog/category/category-tree.test.tsx` (mouse + keyboard reorder).
- [ ] T050 [US2] [P] Create `tests/a11y/catalog-tree.spec.ts` covering keyboard reorder + ARIA tree semantics.
- [ ] T051 [US2] [P] Create `tests/visual/catalog/categories.spec.ts` empty / 100-node states.
- [ ] T052 [US2] Create `e2e/catalog/story2_category_tree.spec.ts` exercising drag → save → deactivate → label-edit.

**Checkpoint**: US2 ships on top of US1.

---

## Phase 5: User Story 3 — Brand and manufacturer CRUD (Priority: P3)

**Goal**: brand + manufacturer CRUD with AR + EN names + logo + active flag.

- [ ] T053 [US3] Create `apps/admin_web/app/(admin)/catalog/brands/page.tsx` (DataTable list).
- [ ] T054 [US3] [P] Create `apps/admin_web/app/(admin)/catalog/brands/new/page.tsx` and `[brandId]/page.tsx`.
- [ ] T055 [US3] [P] Create `apps/admin_web/components/catalog/brand-form.tsx` using spec 015's `FormBuilder`. Mount `<AuditForResourceLink resourceType="Brand" resourceId={brandId} />` in the editor header per FR-007a.
- [ ] T056 [US3] [P] Create `apps/admin_web/app/(admin)/catalog/manufacturers/page.tsx`, `new/page.tsx`, `[mfgId]/page.tsx`.
- [ ] T057 [US3] [P] Create `apps/admin_web/components/catalog/manufacturer-form.tsx`. Per FR-012a, the form mirrors brand-form's shape with AR + EN name fields + optional logo + active flag; spec 005-specific manufacturer fields surface generically through the same `attributes` map renderer as the product editor (T027). Mount `<AuditForResourceLink resourceType="Manufacturer" resourceId={mfgId} />` in the editor header per FR-007a.
- [ ] T058 [US3] [P] Reuse `<MediaPicker>` for the brand / manufacturer logo (single-image variant).
- [ ] T059 [US3] [P] Create unit + visual + a11y tests for brand + manufacturer forms.
- [ ] T060 [US3] Create `e2e/catalog/story3_brand_manufacturer.spec.ts`.

**Checkpoint**: US3 ships independently on top of US1.

---

## Phase 6: User Story 4 — Bulk CSV import / export (Priority: P4)

**Goal**: export → modify → re-upload → dry-run report → commit (all-or-nothing).

- [ ] T061 [US4] Create `apps/admin_web/app/(admin)/catalog/bulk-import/page.tsx` — wizard step 1, upload form. Per FR-030b, when `contracts/csv-format.md` reports the spec 005 schema as not yet published (`spec-005:gap:csv-schema-publication` open + the upstream OpenAPI doc lacks the bulk-import endpoint), the wizard renders a localized "schema not yet published" notice and disables the upload control. The check runs at page render via a server-side health probe; result is cached for the request lifetime.
- [ ] T062 [US4] [P] Create `apps/admin_web/components/catalog/bulk-import/upload-step.tsx`.
- [ ] T063 [US4] Create `apps/admin_web/app/(admin)/catalog/bulk-import/[sessionId]/page.tsx` — wizard step 2, review report.
- [ ] T064 [US4] [P] Create `apps/admin_web/components/catalog/bulk-import/review-step.tsx` (renders validated / errored counts + commit button gated on explicit row-count confirmation per FR-025).
- [ ] T065 [US4] [P] Create `apps/admin_web/components/catalog/bulk-import/validation-report-table.tsx` rendering downloadable per-row errors (locale-aware columns).
- [ ] T066 [US4] [P] Create `apps/admin_web/app/api/catalog/bulk-import/route.ts` upload proxy (multipart).
- [ ] T067 [US4] [P] Create `apps/admin_web/app/api/catalog/bulk-import/[sessionId]/commit/route.ts` proxy.
- [ ] T068 [US4] [P] Create `apps/admin_web/app/api/catalog/bulk-import/[sessionId]/report/route.ts` (signed-URL fetch + stream-back).
- [ ] T069 [US4] [P] Create `apps/admin_web/app/(admin)/catalog/bulk-import/export/route.ts` — streamed CSV export proxying spec 005's stream.
- [ ] T070 [US4] [P] Create `tests/unit/catalog/bulk-import/{upload-step,review-step,validation-report-table}.test.tsx`.
- [ ] T071 [US4] [P] Create Storybook stories for the wizard's three steps + failed / committed states.
- [ ] T072 [US4] [P] Create `tests/visual/catalog/bulk-import.spec.ts`.
- [ ] T073 [US4] Create `e2e/catalog/story4_bulk_import.spec.ts` covering export → modify → re-upload → review → commit.

**Checkpoint**: US4 ships independently.

---

## Phase 7: AR/RTL editorial pass (cross-cutting)

- [ ] T074 [MANUAL] [P] Editorial-grade AR translations for every key seeded in T014/T015. **MUST NOT be executed by an autonomous agent.** Constitution Principle 4 forbids machine-translated AR. Workflow: agent commits the catalog-side keys to `messages/ar.json` with `"@@x-source": "EN_PLACEHOLDER"` markers; human translator replaces; CI fails the AR build if any marker remains. `/speckit-implement` MUST stop at this task.
- [ ] T075 [P] Run `pnpm lint:i18n` against the catalog feature; resolve any leak.
- [ ] T076 [P] Re-run all catalog visual snapshots in AR-RTL — fix layout bugs found.
- [ ] T077 [P] Verify Tiptap text-direction extension picks up the active locale on the description editor.

---

## Phase 8: Polish & cross-cutting concerns

- [ ] T078 [P] Run `pnpm test:a11y -- --grep catalog` and resolve every axe violation, especially keyboard reorder on the tree.
- [ ] T079 [P] Run `pnpm test --coverage -- catalog` and bring branch coverage on `lib/catalog/` and `components/catalog/` to ≥ 90 %.
- [ ] T080 [P] Verify the catalog feature folder adds < 200 KB gzipped to the initial JS bundle on the catalog routes (defer Tiptap + Uppy via `next/dynamic` if it spills).
- [ ] T081 [P] Verify the audit-log reader (spec 015) renders the new catalog audit kinds correctly: pick a few seeded events and confirm the JSON diff renders the AR + EN rationale fields legibly.
- [ ] T082 [P] Run the bulk-import contract test against a 5000-row staging fixture; record dry-run + commit timing in PR description (SC-005 / SC-006).
- [ ] T083 [P] Verify catalog-specific telemetry events pass the PII guard (`tests/unit/catalog/telemetry.pii-guard.test.ts`).
- [ ] T084 [P] Ensure no direct `fetch('http…')` calls bleed into `components/catalog/` (lint sweep).
- [ ] T084a [P] Append catalog-specific gap rows to `docs/admin_web-escalation-log.md` (file authored in spec 015's T098a). One row per gap: `(date, owning spec, gap title, GitHub issue link, in-app workaround)`. Empty additions on merge are acceptable; missing log file fails the PR.
- [ ] T084b [P] Verify SC-009 ("0 backend contract changes shipped from this spec"). Compute `sha256` of every `services/backend_api/openapi.*.json` file at PR open time and compare against the same checksums on `main` at the branch's merge-base. CI MUST fail if any backend OpenAPI doc changed. Output the comparison table to the PR description.
- [ ] T084c [P] Per FR-030a, append the catalog permission keys (`catalog.read`, `catalog.product.read`, `catalog.product.write`, `catalog.product.bulk_import`, `catalog.product.export`, `catalog.category.read`, `catalog.category.write`, `catalog.brand.read`, `catalog.brand.write`, `catalog.manufacturer.read`, `catalog.manufacturer.write`) to `specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md` if not already present, and ensure spec 015's `pnpm catalog:check-permissions` (T032c) passes against the catalog routes' `requiredPermissions` declarations.
- [ ] T084d [P] Per FR-007a, verify every catalog editor page (product, category, brand, manufacturer) renders `<AuditForResourceLink>` in the page header — coverage check via Storybook story sweep + a visual diff that asserts the component's presence on each editor's snapshot.
- [ ] T084e [P] Per FR-030b, verify the "schema not yet published" notice on the bulk-import wizard renders the localized copy correctly when the gap is open and disappears when the schema lands. Add a Storybook story for the gap state.
- [ ] T084f [P] Per spec 015 T032d, author `apps/admin_web/lib/auth/nav-manifest-static/catalog.json` declaring the Catalog group + sub-entries (Products, Categories, Brands, Manufacturers, Bulk import) per `contracts/nav-manifest.md` order range 200–299. Ensure spec 015's `pnpm catalog:check-nav-manifest` (T032e) passes.
- [ ] T085 Author DoD checklist evidence for SC-001 → SC-009 in the PR description.
- [ ] T086 Open the PR with: spec link, plan link, story-by-story demos (screen recordings or Storybook links), CI green, fingerprint marker.

---

## Dependencies

| Phase | Depends on |
|---|---|
| Phase 1 (Setup) | spec 015 merged + spec 005 contract merged |
| Phase 2 (Foundational) | Phase 1 |
| Phase 3 (US1) | Phase 2 |
| Phase 4 (US2) | Phase 2 (independent of US1) |
| Phase 5 (US3) | Phase 2 (independent of US1 / US2) |
| Phase 6 (US4) | Phase 2 + Phase 3 (uses product list view to verify post-commit) |
| Phase 7 (AR/RTL) | Phase 3 + Phase 4 + Phase 5 + Phase 6 |
| Phase 8 (Polish) | All prior phases |

## Parallel-execution opportunities

- **Phase 2**: T005–T015 are mostly file-disjoint — large parallel fan-out.
- **Within US1**: list / editor / media / publish-controls are largely independent file scopes.
- **US2 / US3 can run in parallel** with each other once Phase 2 is complete — different feature folders.
- **US4** depends on US1's product list (for post-commit verification) but its own file scope is independent.

## Suggested MVP scope

**MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — 42 tasks — ships product create-and-publish. US2 / US3 / US4 are post-MVP increments and can ship as separate PRs.

## Format check

All 86 tasks follow `- [ ] Tnnn [P?] [USn?] description (path)`. Tests interleaved per story so each is a vertical-slice PR.
