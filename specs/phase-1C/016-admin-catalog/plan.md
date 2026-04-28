# Implementation Plan: Admin Catalog

**Branch**: `phase-1C-specs` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1C/016-admin-catalog/spec.md`

## Summary

Mount the **catalog management module** inside spec 015's admin shell — Products / Categories / Brands / Manufacturers / Bulk import. Lane B: UI only — every backend gap escalates to spec 005. Inherits the shell, auth proxy, `DataTable`, `FormBuilder`, audit-log read surface, AR-RTL plumbing, and CI hygiene from spec 015.

The product editor implements a **draft → scheduled → published** state model with copy-on-write revisions matching spec 005's `product_state_transitions` table. Media + documents land under a **draft-scoped bucket prefix** owned by spec 003's storage abstraction; orphan drafts are swept by a 24-hour worker. Bulk CSV import follows a **dry-run → validation report → all-or-nothing commit** flow matching spec 005's `bulk_import_idempotency` table. Restricted-flag toggling requires AR + EN rationale; every state transition emits an audit event the operator reads through spec 015.

Drag-and-drop on the category tree is powered by **`@dnd-kit/core`**. The product editor's AR + EN content tabs use a tabbed `FormBuilder` layout — no separate locale routes — so a single dirty-state guard covers both languages. CSV streaming on export uses Web Streams (no full-buffer hold) to handle 10k-row catalogs without memory spikes.

## Technical Context

**Language/Version**: TypeScript 5.5, Node.js 20 LTS (inherits spec 015's runtime).

**Primary Dependencies** (additions on top of spec 015's stack):

- `@dnd-kit/core` ^6 + `@dnd-kit/sortable` ^7 — accessible drag-and-drop for the category tree.
- `react-arborist` ^3 — virtualized tree component (drives the category editor's render performance on 10k-node trees).
- `@uppy/core` ^3 + `@uppy/aws-s3` (or signed-URL adapter) ^3 — media + document upload with retry, chunking, progress UI. Spec 003 storage abstraction issues the signed URL.
- `papaparse` ^5 — CSV parse / serialize for the bulk-import flow.
- `react-textarea-autosize` ^8 — content tab textareas (AR + EN tabs of the product editor).
- `@tiptap/react` ^2 + `@tiptap/extension-text-direction` — minimal rich-text editor for product descriptions (paragraph, bold, italic, list, link only — no media). Direction extension picks up the active locale.
- All other deps inherited from spec 015 (Next.js, react-query, react-table, react-hook-form, zod, next-intl, iron-session, shadcn/ui, etc.).

**Storage**: No new server-side storage in this spec. Client-side: react-query cache for list / detail / draft state; transient `IndexedDB` (via `idb` ^8) for in-progress upload metadata so a tab refresh doesn't lose upload progress on multi-megabyte uploads. No tokens, no PII in IndexedDB.

**Testing**:

- Unit + component (vitest + RTL) — every catalog Bloc-equivalent (here, react-query mutation + `react-hook-form` controller) has a unit test.
- Visual regression (Playwright + Storybook snapshots) — every catalog screen × {EN-LTR, AR-RTL} × {light, dark}.
- A11y (axe-playwright) — every catalog screen, with explicit checks for the drag-and-drop tree (keyboard reorder via arrow keys + space).
- E2E (Playwright) — Story 1 (create + publish), Story 2 (category tree), Story 4 (bulk import dry-run + commit) on Chromium / Firefox / WebKit.
- A bulk-import contract test — uploads a CSV with seeded valid + invalid rows, asserts the dry-run validation report shape.

**Target Platform**: Same as spec 015 — modern desktop browsers ≥ 1280 px wide. Drag-and-drop falls back to keyboard reorder for accessibility.

**Project Type**: Next.js admin web feature folder under `apps/admin_web/app/(admin)/catalog/` and `apps/admin_web/components/catalog/`. No new app or package.

**Performance Goals**:

- Drag-reorder commit ≤ 1 s on 10k-category staging dataset (SC-004).
- Bulk-import dry-run on 5000 rows ≤ 30 s (SC-005).
- Bulk-import commit on 5000 rows ≤ 60 s (SC-006).
- Product editor first interactive render ≤ 1.5 s on broadband.

**Constraints**:

- **No backend code in this PR** (FR-031). Gaps escalate to spec 005.
- **No direct provider URLs** for media (FR-014). All upload paths go through the storage abstraction's signed URL.
- **No client-side fetch outside `lib/api/`** (inherits spec 015's lint).
- **No hard-coded user-facing strings** (inherits spec 015's i18n lint).
- **Strict CSP** for the catalog route group — `connect-src` only includes the backend + storage abstraction; `script-src` is `self` (no inline JS). Helps mitigate any rich-text-editor XSS surface.
- **Bulk-import row cap**: 5000 rows per upload (configurable via env, hard cap 50000 to avoid memory blow-up).

**Scale/Scope**: ~10 catalog pages (products list / editor, categories tree, brands list / editor, manufacturers list / editor, bulk-import wizard, draft-revisions panel). 4 prioritized user stories, 33 functional requirements, 9 success criteria, 5 clarifications integrated. Storybook target: ~30 stories on top of spec 015's baseline.

## Constitution Check

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Customer browse / view price unaffected — this is the admin side. | PASS (n/a) |
| P4 Arabic / RTL editorial | Every catalog screen ships AR + EN with RTL via spec 015's i18n stack. AR + EN content tabs in the product editor; both required where the spec says so. | PASS |
| P5 Market Configuration | Catalog is global with per-product market visibility (per spec 005). The list filter exposes market scope; no hard-coded market literals in UI logic. | PASS |
| P6 Multi-vendor-ready | Forward-compatible. When spec 005 gains vendor scope, the editor adds a vendor field; current views render whatever the server sends and ignore unknown fields. | PASS |
| P7 Branding | Tokens consumed from `packages/design_system`. No inline hex literals. | PASS |
| P8 Restricted Products | Restricted-flag editor with mandatory AR + EN rationale (FR-019). Audit emission on every toggle (FR-020). Customer-side visibility is owned by spec 014 and stays compliant with Principle 8. | PASS |
| P10 Pricing | Catalog editor is purely metadata — pricing CRUD lives in a later spec. The editor surfaces a read-only pricing reference for context. | PASS (deferral) |
| P11 Inventory | Inventory CRUD lives in spec 017. Editor surfaces a read-only inventory pointer. | PASS (deferral) |
| P15 Reviews | Out of scope. | PASS |
| P22 Fixed Tech | Next.js + shadcn/ui per ADR-006 (inherited from spec 015). | PASS |
| P23 Architecture | Spec 015's modular shell + this feature folder. No new service. | PASS |
| P24 State Machines | Product publish state (`draft` / `scheduled` / `published` + `discarded`), bulk-import session (`uploaded` / `validated` / `committed` / `failed`) — both documented in `data-model.md`. | PASS |
| P25 Data & Audit | Every product / category / brand / manufacturer mutation emits an audit event server-side via spec 005, surfaced through spec 015's reader. | PASS |
| P27 UX Quality | Every screen ships loading / empty / error / restricted / draft-conflict / scheduled-conflict states (FR-005 + extended). Drag-and-drop has explicit keyboard reorder fallback for accessibility. | PASS |
| P28 AI-Build Standard | Spec ships explicit FRs, scenarios, edge cases, success criteria, 5 resolved clarifications. | PASS |
| P29 Required Spec Output | All 12 sections present. | PASS |
| P30 Phasing | Phase 1C Milestone 5/6. Depends on spec 005 contract merged + spec 015 shipped. | PASS |
| P31 Constitution Supremacy | No conflicts. | PASS |
| ADR-001 Monorepo | Code lives under `apps/admin_web/` only. | PASS |
| ADR-006 Next.js + shadcn/ui | Locked. | PASS |
| ADR-010 KSA residency | All API calls hit the backend in Azure Saudi Arabia Central. Storage abstraction also lives in the same region. | PASS |

**No violations.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1C/016-admin-catalog/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── consumed-apis.md
│   ├── routes.md
│   ├── client-events.md
│   └── csv-format.md           # Reference to spec 005's CSV header schema
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
apps/admin_web/
├── app/(admin)/catalog/
│   ├── layout.tsx                       # Catalog sub-shell (sidebar group highlighting)
│   ├── page.tsx                         # Catalog overview (links to sub-modules)
│   ├── products/
│   │   ├── page.tsx                     # List
│   │   ├── new/page.tsx                 # New product editor
│   │   └── [productId]/
│   │       ├── page.tsx                 # Editor (with draft + revision tabs)
│   │       └── revisions/page.tsx       # Revision history
│   ├── categories/page.tsx              # Tree editor
│   ├── brands/{page,new,[brandId]/page}.tsx
│   ├── manufacturers/{page,new,[mfgId]/page}.tsx
│   └── bulk-import/
│       ├── page.tsx                     # Wizard step 1 — upload
│       ├── [sessionId]/page.tsx         # Wizard step 2 — review report + commit
│       └── export/route.ts              # Streamed CSV export
├── components/catalog/
│   ├── product/
│   │   ├── product-editor-form.tsx      # Tabbed AR/EN content + attributes
│   │   ├── product-state-pill.tsx       # draft / scheduled / published
│   │   ├── publish-controls.tsx         # publish / schedule / discard
│   │   ├── revision-history.tsx
│   │   └── restricted-flag-section.tsx
│   ├── category/
│   │   ├── category-tree.tsx            # @dnd-kit + react-arborist
│   │   ├── category-row.tsx
│   │   └── deactivate-dialog.tsx
│   ├── brand-form.tsx
│   ├── manufacturer-form.tsx
│   ├── media/
│   │   ├── media-picker.tsx             # @uppy + previews
│   │   ├── document-uploader.tsx
│   │   └── variant-preview.tsx
│   └── bulk-import/
│       ├── upload-step.tsx
│       ├── review-step.tsx
│       └── validation-report-table.tsx
├── lib/catalog/
│   ├── product-state.ts                 # SM-1 client model
│   ├── bulk-import-session.ts           # SM-2 client model
│   ├── csv.ts                           # Papaparse wrappers + header validation
│   ├── upload-manager.ts                # Uppy + IndexedDB resume metadata
│   └── api.ts                           # react-query hooks wrapping the spec 005 client
└── tests/
    ├── unit/catalog/...
    ├── visual/catalog.spec.ts
    └── a11y/catalog-tree.spec.ts        # Keyboard reorder coverage
```

**Structure Decision**: One feature folder per noun (`products`, `categories`, `brands`, `manufacturers`, `bulk-import`) under the existing `app/(admin)/catalog/` route group. Components live under `components/catalog/<noun>/` mirroring the route structure. `lib/catalog/` holds catalog-specific shared logic (state machines, CSV helpers, upload manager) but never mounts UI. The shell + DataTable + FormBuilder come from spec 015 — this spec does not reimplement primitives.

## Complexity Tracking

| Choice | Why | Simpler alternative rejected because |
|---|---|---|
| `@dnd-kit/core` + `react-arborist` | Accessible keyboard reorder + virtualized tree at 10k nodes (SC-004). | A naive HTML tree would either freeze on large trees or fail accessibility checks. |
| `@uppy/core` + storage abstraction signed URLs | Resumable, chunked uploads survive flaky LANs and large files; spec 003's signed-URL handshake stays unchanged. | Direct browser-side multipart uploads to a provider would couple the UI to the provider (FR-014 violation). |
| `IndexedDB` for in-progress upload metadata | A tab refresh on a multi-MB upload should not orphan the file. IndexedDB persists for the upload session only, never tokens or PII. | localStorage is too small (~5 MB cap) and synchronous; in-memory state loses on refresh. |
| Tabbed AR + EN content (single editor, not separate routes) | One dirty-state guard covers both languages; admins switch tabs without losing context. | Separate `/edit/ar` and `/edit/en` routes would duplicate the navigation guard and the publish controls. |
| Server-side dry-run + downloadable validation report (CSV) | Matches spec 005's `bulk_import_idempotency` semantics; admins can fix CSVs in their existing tooling. | Streaming row-by-row validation in the browser would replicate server logic and break on header drift. |
| Tiptap minimal rich-text (no media) | Product descriptions need bold / italic / lists / links but should not embed media (Constitution Principle 28 — explicit, structured content). | A full WYSIWYG would invite content drift, security surface (XSS via pasted HTML), and a CSP carve-out. |
