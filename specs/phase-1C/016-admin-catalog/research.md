# Phase 0 Research: Admin Catalog

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

Resolves every Technical-Context decision in `plan.md` to a concrete library / pattern. Inherits unchanged decisions from spec 015 (Next.js App Router, iron-session auth proxy, openapi-typescript, vitest + Playwright, Storybook, etc.); only the catalog-specific deltas are documented here.

---

## R1. Drag-and-drop tree — `@dnd-kit/core` + `@dnd-kit/sortable` + `react-arborist`

- **Decision**: `@dnd-kit/core` ^6 for the DnD primitives, `@dnd-kit/sortable` ^7 for sortable strategy, `react-arborist` ^3 for the virtualized tree. The category tree composes them: react-arborist owns virtualization + keyboard navigation, dnd-kit owns the drag interaction, both glued through a small adapter under `lib/catalog/category-dnd-adapter.ts`.
- **Rationale**: react-arborist's virtualization is what keeps the editor responsive on 10k-node trees (SC-004). dnd-kit is the modern accessible-by-default choice (proper ARIA `role="tree"` + arrow-key reorder). Combining them means one library handles big trees, another handles drag — neither library reinvents the other's concern.
- **Keyboard reorder fallback** (FR-008 + a11y): arrow-up/down moves selection; space lifts; arrow-up/down moves; space drops. Tested by `tests/a11y/catalog-tree.spec.ts`.
- **Alternatives rejected**: `react-dnd` (older API, weaker a11y story), `react-beautiful-dnd` (deprecated, not Next-13/14-friendly), in-house tree (loses virtualization, blows the perf budget).

## R2. Media + document upload — `@uppy/core` + storage abstraction

- **Decision**: `@uppy/core` ^3 with the `@uppy/aws-s3` companion plugin (or a generic signed-URL plugin if spec 003's abstraction issues non-S3-shaped URLs). Spec 003 issues a presigned upload URL through its abstraction; the browser uploads directly to the storage provider via that URL — never via the Next.js server.
- **Rationale**: Uppy is the leanest production-grade upload library — chunked, resumable, retry on flaky network, progress UI, image previews. Direct-to-storage uploads keep the Next.js server out of the multi-megabyte upload data path. Spec 003 controls the abstraction so vendor lock-in stays at one layer.
- **Draft scope**: Uppy's `meta` field carries `{ productId, draft: true }` so the storage abstraction tags the object accordingly (FR-015). On publish, a server-side action clears the draft tag atomically. On discard, the object becomes orphaned and the spec 005 sweeper deletes it after 24 h (FR-016).
- **Resume on refresh**: Uppy persists in-flight upload metadata to IndexedDB via `@uppy/golden-retriever`. On a tab refresh, in-progress uploads resume; metadata holds only file id + chunk progress, never tokens.
- **Alternatives rejected**: `react-dropzone` (no chunking / resume), browser FormData multipart (no retry / progress / chunking), `tus-js-client` (TUS protocol not what the storage abstraction speaks).

## R3. CSV parse / serialize — `papaparse` + Web Streams

- **Decision**: `papaparse` ^5 for parsing the dry-run validation report download client-side (fixing-up + re-uploading) and for streaming export. Export goes through a Next.js Route Handler that pipes the spec 005 export endpoint's response stream straight to the client — no full buffer hold (SC-005 / SC-006 friendly).
- **Rationale**: Papaparse is the de-facto JS CSV library; the Web Streams pipeline keeps memory flat regardless of catalog size.
- **Header validation**: `lib/catalog/csv.ts` exports a typed schema validator that compares the uploaded header to the schema spec 005 publishes. Mismatch fails fast with a single localized error (Edge case in `spec.md`).
- **Alternatives rejected**: hand-rolled CSV parser (RFC 4180 edge cases are nasty), `csv-parse` (heavier, less browser-friendly).

## R4. Rich text — `@tiptap/react` minimal config

- **Decision**: `@tiptap/react` ^2 with `StarterKit` minus the disallowed extensions; explicit allow-list: paragraph, bold, italic, list (ordered + unordered), link, code-block. Plus `@tiptap/extension-text-direction` so the editor respects the active locale's direction.
- **Rationale**: Constitution Principle 28 demands explicit / structured content — a full WYSIWYG would invite content drift and a CSP carve-out. Tiptap's allow-list approach keeps the surface small and predictable. Pasted content is sanitized through Tiptap's schema (no embedded HTML).
- **Output**: Tiptap emits ProseMirror JSON, which spec 005 already accepts on the catalog product description payload (research note: confirm with spec 005 schema).
- **Alternatives rejected**: Lexical (Meta's editor — strong but heavier integration), Slate.js (less batteries-included), raw textarea (loses bold / italic / lists).

## R5. Form layout for AR + EN tabs — tabbed `FormBuilder`

- **Decision**: A single `FormBuilder` instance with a `LocaleTabs` wrapper that renders the same field schema once per locale. Field state is keyed by `name + locale` (e.g., `nameAr`, `nameEn`); zod schema requires both locales for required fields. Dirty-state covers both tabs at once — switching tabs never resets the form.
- **Rationale**: One editor → one save action → one publish action. Separate locale routes would force two dirty-state guards and two publish flows; tabs collapse this complexity.
- **i18n**: The tab labels themselves are localized (so an Arabic-locale admin sees "العربية / الإنجليزية" tabs); the tab content remains in the locale of the tab regardless of the shell locale.
- **Alternatives rejected**: Separate forms per locale (double the code), language-suffix keys (`name_ar`/`name_en`) flat in one tab (poor UX, hides the "did I fill both languages?" question).

## R6. Optimistic concurrency — spec 005's row-version pattern

- **Decision**: Editor reads `rowVersion` from spec 005's product response; mutations send the version back. Server-side conflict (412) maps to a "your version is stale, reload?" dialog in the UI. Reload preserves any unsaved local edits in a side panel so the admin can copy them across.
- **Rationale**: Spec 005 already implements row-version optimistic concurrency (per the project's existing pattern documented in CLAUDE.md / data-model.md of spec 005). This spec just exposes it cleanly to the UI.
- **Alternatives rejected**: Pessimistic locking (would require a backend "currently editing" record — out of scope for 016), last-write-wins (silent data loss).

## R7. Bulk-import wizard

- **Decision**: Three-step wizard rendered as URL-keyed steps:
  1. `/admin/catalog/bulk-import` — upload step. POSTs the CSV to a Next.js Route Handler that proxies to spec 005's dry-run endpoint with `multipart/form-data`. Returns a `sessionId`.
  2. `/admin/catalog/bulk-import/[sessionId]` — review step. Server Component fetches the session status; if `validated`, the step renders the row count + a download link to the validation report.
  3. Commit action on the review step posts a confirm to spec 005's commit endpoint with the explicit `expectedRowCount` (matches FR-025).
- **Rationale**: URL-keyed steps survive refresh, support back-button, and let the admin send a permalink to a colleague mid-import (the validation report is recoverable). Matches spec 005's `bulk_import_idempotency` table.
- **Alternatives rejected**: Single-page modal wizard (loses refresh resilience), polling-based "import in progress" UI on the products page (poor information architecture).

## R8. Publish-state UX — three-state pill + scheduling popover

- **Decision**: `<ProductStatePill>` renders one of `draft` / `scheduled@<time>` / `published`. The publish controls (`<PublishControls>`) expose:
  - **Publish now** (terminal action, triggers backend transition)
  - **Schedule** (opens a date-time picker; disabled if `state === 'scheduled'` to avoid stacking)
  - **Discard draft** (only enabled in `draft` state; confirmation dialog)
  - **Revert to draft** (in `published` state, opens the copy-on-write revision)
- **Rationale**: Three states + four actions is the smallest set that covers FR-027 + FR-028 + FR-029 without ambiguity.
- **Alternatives rejected**: A toggle button (loses the scheduled state UX), separate "scheduled" page (over-fragmented).

## R9. Restricted-flag section

- **Decision**: A dedicated `<RestrictedFlagSection>` collapsed by default. When the toggle is `on`, the AR + EN rationale fields become required and the form's zod schema enforces both. Toggling the flag emits a client telemetry event (`catalog.product.restricted.toggled`) per `contracts/client-events.md`. The committed rationale renders read-only in the audit-log reader's after-state JSON via spec 015.
- **Alternatives rejected**: Free-form rationale across the form (poor discoverability — admins miss it), single-locale rationale (Constitution Principle 4 forbids).

## R10. Visual-regression coverage extensions

- **Decision**: Add to spec 015's Storybook visual-regression suite the following stories — each in EN-LTR + AR-RTL × {light, dark}:
  - `<ProductEditorForm>` in `draft` / `scheduled` / `published` / `restricted-with-rationale-missing` / `conflict-detected` states.
  - `<CategoryTree>` in empty / 100-node / 10000-node states (for visual regression at scale, the 10000-node story is keyed off a fixture and skipped on snapshot diff if the fixture is out of date).
  - `<MediaPicker>` in idle / uploading / error / done states.
  - `<BulkImportWizard>` for each of the three steps + `failed` and `committed` terminal states.
- **Rationale**: SC-003 + SC-008 carry forward from spec 015. New screens land in the same enforcement.

## R11. CI integration

- **Decision**: No new workflow file. The existing `apps/admin_web-ci.yml` (from spec 015) runs against this branch unchanged — the catalog pages live inside `apps/admin_web/`. The advisory `impeccable-scan` workflow already targets this app's PRs (CLAUDE.md). Phase 1F spec 029 promotes it to merge-blocking.
- **Rationale**: Inheriting CI is exactly the point of building inside the shell from spec 015.

---

## Open follow-ups for downstream specs

- **Spec 005**: confirm the bulk-import-export endpoints expose all role-scoped products via streamed CSV; if not, file `spec-005:gap:streamed-export`. Confirm the CSV header schema is published in `spec-005/contracts/`.
- **Spec 005**: confirm media variants generated by the variant worker include the three sizes the editor surfaces (thumbnail, mid, large); if not, file the gap.
- **Spec 003**: confirm the storage abstraction's signed-URL response includes a `meta.draft` flag and that the abstraction's sweeper respects it. Already documented as a 24-hour cadence in this spec's Assumptions; if the cadence is configurable, surface that as an env in the operations runbook.
- **Spec 015**: confirm `FormBuilder` exposes the "dirty-state guard with discard / save / cancel" dialog primitive — if not, the dialog is added to spec 015 as a follow-up.
- **Spec 020 (verification)**: when 020 ships, the restricted-rationale field becomes a referenced source in the verification reviewer's screen; the data shape doesn't change here.
