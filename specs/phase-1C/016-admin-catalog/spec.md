# Feature Specification: Admin Catalog

**Feature Branch**: `phase-1C-specs`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Spec 016 admin-catalog (Phase 1C) — Next.js admin web feature (mounts inside spec 015's shell). Per docs/implementation-plan.md §Phase 1C item 016: depends on spec 005 contract merged to main + spec 015. Exit: CRUD for category/brand/product/media/docs; restriction metadata; bulk ops. Task groups: (1) Category tree editor (drag-reorder, activate/deactivate). (2) Brand CRUD. (3) Product CRUD with attribute editor; AR + EN content tabs. (4) Media + document upload via storage abstraction; variant previews. (5) Restriction flag editor + rationale field. (6) Bulk import/export (CSV) with validation report. (7) Draft + publish workflow; audit on publish."

## Clarifications

### Session 2026-04-27

- Q: How should the product editor handle dirty state when the admin navigates away with unsaved changes? → A: **Block navigation with a confirmation dialog** ("You have unsaved changes — discard / save / cancel"). Same dialog primitive as the rest of spec 015's `FormBuilder` dirty-warning. Auto-save is not in scope for v1 — admins explicitly save or discard.
- Q: Where do uploaded product media + documents land while a product is still in **draft**? → A: **Storage abstraction with a draft-scoped bucket prefix** owned by spec 003 storage seam. Files are written immediately on upload (so the admin sees real previews) but tagged `draft=true`. On publish, the draft tag is cleared atomically alongside the product transition; on draft-discard, a sweeper clears orphaned draft assets after 24 h.
- Q: How should bulk CSV import surface row-level validation errors? → A: **Server-side dry-run before commit, returning a structured validation report**. The admin uploads CSV → server validates every row, returns a downloadable report (CSV) listing row number + column + reason code per failure. Admin reviews, fixes, re-uploads. Commit step requires the admin to explicitly confirm the validated row count. No partial commits — all-or-nothing per upload, matching spec 005's bulk-import-idempotency table.
- Q: How granular is the publish-workflow state? → A: **Three states — `draft`, `scheduled`, `published`** plus a back-edge to `draft`. Draft is editable. Scheduled holds a future-dated publish time; until reached the product is invisible to customers. Published is live; editing creates a new draft revision (copy-on-write) and the admin chooses to overwrite-published or queue-as-scheduled. Audit emits on every transition. Matches spec 005's `product_state_transitions` table.
- Q: How should restricted-product rationale be enforced? → A: **Mandatory free-text rationale field whenever the restricted flag is true**. Rationale is captured server-side per spec 005 contract and surfaced in the audit-log reader (spec 015) entry's after-state JSON. Both AR and EN copy fields exist for the rationale (since restricted-product reasons surface to verification reviewers and may be quoted to customers in localized denial messages).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and publish a product end-to-end (Priority: P1)

A catalog admin opens the catalog module, navigates to **Products**, clicks **New product**, fills in the product form (SKU, name AR + EN, description AR + EN, brand, category, attributes, restricted flag with rationale if applicable, pricing reference, initial inventory pointer), uploads media + documents, saves as draft, previews the storefront detail rendering, then publishes — the product appears in the customer app's listing within a refresh.

**Why this priority**: This is the conversion path for the catalog module — without it the platform has no products to sell. Every other catalog module story is an enhancement on top of this slice.

**Independent Test**: Walk a new product through draft → upload media → save → preview → publish; verify the product appears in the customer app's listing (spec 014) and that an audit entry is emitted on publish.

**Acceptance Scenarios**:

1. **Given** an authenticated admin with `catalog.product.write`, **When** they create a new product with all required fields, **Then** the product saves as `draft` and appears in the products list with the draft badge.
2. **Given** a draft product, **When** the admin uploads a media file, **Then** the file is stored under the draft-scoped bucket prefix and a preview thumbnail appears in the editor within seconds.
3. **Given** a draft product with the restricted flag set, **When** the admin tries to save without filling the rationale field (AR + EN), **Then** the form blocks save with a localized error per language tab.
4. **Given** a draft product, **When** the admin clicks **Publish**, **Then** the product transitions to `published`, an audit event is emitted, the customer app's listing reflects the new product within one refresh, and any draft-scoped media tags are cleared.
5. **Given** a published product, **When** the admin edits any field, **Then** a new draft revision is created (copy-on-write); the published version remains live until the admin explicitly republishes or schedules.
6. **Given** a draft product with media uploaded, **When** the admin discards the draft, **Then** the orphan media is cleared by a sweeper within 24 hours.
7. **Given** the admin attempts to navigate away with unsaved changes, **When** they click a sidebar entry or press the browser back button, **Then** a confirmation dialog blocks the navigation with discard / save / cancel options.

---

### User Story 2 - Manage the category tree (Priority: P2)

A catalog admin opens **Categories**, sees the full tree, drags a sub-category to a new parent, adds a new leaf, deactivates a discontinued branch, edits the AR + EN labels, and saves — the customer app's category navigation reflects the new tree without breaking active products.

**Why this priority**: Categories are the navigation backbone of the customer app. Editing them is medium-frequency work but critical when wrong (a deactivated category hides every product mapped to it).

**Independent Test**: Walk an admin through drag-reorder → add → deactivate → label-edit; verify the customer app's listing tree refreshes; verify products mapped to a deactivated branch surface a warning.

**Acceptance Scenarios**:

1. **Given** the categories page is open, **When** the admin drags a sub-category to a new parent, **Then** the tree re-renders optimistically, the change is persisted, and an audit event is emitted.
2. **Given** a category with active products, **When** the admin attempts to deactivate it, **Then** the editor surfaces a non-blocking warning ("X active products are mapped here — they will be hidden from the storefront when this branch is inactive") with a confirmation dialog before commit.
3. **Given** a category, **When** the admin edits the AR or EN label, **Then** both labels are required and validated before save.
4. **Given** an admin without `catalog.category.write`, **When** they navigate to `/admin/catalog/categories`, **Then** they see a 403-style screen — never the editor.

---

### User Story 3 - Brand and manufacturer CRUD (Priority: P3)

A catalog admin manages the catalog's brands (and manufacturers, where the model distinguishes them per spec 005) — create, edit, set logo, link to manufacturer, deactivate.

**Why this priority**: Brands are referenced by products and surface on the storefront. Lower priority than products + categories but launch-needed for any branded catalog item.

**Independent Test**: Walk through brand create → edit → set logo → deactivate; verify the customer app's product detail surfaces the new brand correctly; deactivated brands are still rendered on the products linked to them (history-preserving).

**Acceptance Scenarios**:

1. **Given** the brands page is open, **When** the admin creates a new brand with AR + EN names, **Then** it appears in the list and is selectable in the product editor.
2. **Given** a brand with linked products, **When** the admin deactivates it, **Then** the brand is hidden from the **new product** picker but remains rendered on existing products (Constitution Principle 25 — history preserved).
3. **Given** any brand mutation, **When** it succeeds, **Then** an audit event is emitted with before / after state.

---

### User Story 4 - Bulk CSV import / export (Priority: P4)

A catalog admin exports the current product catalog as CSV, edits hundreds of rows offline (e.g., bulk price tweaks tied to a supplier negotiation), uploads the CSV, reviews the server-side validation report, fixes flagged rows, re-uploads, confirms the validated row count, and the changes commit atomically.

**Why this priority**: Reduces operator hours when onboarding a new supplier or running a price-update cycle. Lower priority because admins can edit one-by-one until this lands; high value once it does.

**Independent Test**: Export → modify N rows externally → re-upload → review validation report → confirm → verify changes committed and audit events emitted per row.

**Acceptance Scenarios**:

1. **Given** an authenticated admin with `catalog.product.bulk_import`, **When** they export the products CSV, **Then** the download contains every product their role scope permits.
2. **Given** a CSV upload, **When** the dry-run validation runs, **Then** the response is a downloadable report listing row number, column, and reason code for every failure — never a generic "validation failed".
3. **Given** an upload with N validated rows and M errored rows, **When** the admin clicks **Commit**, **Then** the action requires explicit confirmation of the row count, and either all N rows commit or none do (matches spec 005's `bulk_import_idempotency` semantics).
4. **Given** an in-flight bulk import, **When** the admin navigates away or closes the tab, **Then** the import either completes (if commit was already submitted) or is abandoned cleanly with no partial writes.

### Edge Cases

- Two admins edit the same product simultaneously → server-side optimistic concurrency (spec 005 row version) — the second save returns a conflict, the editor surfaces a "your version is stale, reload?" dialog with no data loss.
- Media upload fails mid-stream → editor shows a per-file retry chip; partial upload artefacts are cleared by the sweeper.
- Restricted flag toggled from true to false → rationale fields remain in the model (audit history) but become read-only in the editor; flipping back to true re-enables editing.
- Category deactivated while products are mapped to it → products remain published but the customer app filters them from the deactivated branch's listing automatically.
- Scheduled publish reaches its time while the admin is editing the same product → the scheduled publish creates a new revision; the admin's open editor session receives an inline notice "this product was published from a scheduled change; reload to see the live state".
- CSV import column missing → dry-run rejects the upload with a single header-mismatch error before scanning rows.
- Bulk import larger than the configured row cap (default: 5000 rows) → dry-run returns an early error pointing at the cap.
- Locale switched during product editing → the AR + EN tabs preserve their content; the surrounding shell flips direction; in-flight save still completes.
- Admin's role permission revoked mid-edit → next save returns 403; editor surfaces the same screen the shell uses for direct-403 navigation, with a route back to the products list.

## Requirements *(mandatory)*

### Functional Requirements

#### Product CRUD

- **FR-001**: The catalog module MUST mount inside spec 015's admin shell — sidebar entry "Catalog" with sub-entries Products, Categories, Brands, Manufacturers, Bulk import.
- **FR-002**: The product list MUST use spec 015's shared `DataTable` with server-side pagination, filters (status, category, brand, restricted flag, market), sortable columns, and saved views.
- **FR-003**: The product editor MUST present AR and EN content tabs for every localized field (name, description, restricted-rationale).
- **FR-004**: The product editor MUST validate required fields client-side and re-validate server-side via spec 005 contracts.
- **FR-005**: The product editor MUST block navigation away with unsaved changes via spec 015's `FormBuilder` dirty-state confirmation dialog (per Clarification — discard / save / cancel).
- **FR-006**: A published product MUST become a copy-on-write draft revision when edited; the published version stays live until the admin explicitly republishes.
- **FR-007**: Every product mutation MUST emit an audit event with before / after state via spec 003 — captured by the audit-log reader from spec 015.
- **FR-007a**: The product editor and the category / brand / manufacturer editors MUST surface a header `<AuditForResourceLink>` (spec 015 FR-028f) deep-linking to `/audit?resourceType=Product&resourceId=<id>` (etc.). The link is hidden for actors lacking `audit.read`.

#### Category tree

- **FR-008**: The category tree editor MUST support drag-reorder (within and across parents), add, edit, deactivate.
- **FR-009**: Category labels MUST be required in both AR and EN before save.
- **FR-010**: Deactivating a category with active products MUST surface a non-blocking warning (count + impact) with confirmation before commit.
- **FR-011**: Drag-reorder MUST be optimistic on the client and reconciled with server response on commit.

#### Brands + manufacturers

- **FR-012**: Brand CRUD MUST present AR + EN name fields, an optional logo media reference, and an optional `manufacturerId` linkage. List + editor live at `/catalog/brands` and `/catalog/brands/[brandId]`.
- **FR-012a**: Manufacturer CRUD MUST present AR + EN name fields and an optional logo media reference. Manufacturers are a separate entity from brands per spec 005's catalog model — a manufacturer can produce multiple brands (e.g., one manufacturer, several private-label lines). List + editor live at `/catalog/manufacturers` and `/catalog/manufacturers/[mfgId]`. Spec 005 owns the data model; this spec ships the CRUD UI mirroring brand CRUD's shape. Spec 005-specific manufacturer fields (regulatory contact, country of origin, etc.) surface as the editor's `attributes` map and are rendered generically — additions to that map don't require a UI change here.
- **FR-013**: Deactivated brands / manufacturers MUST remain rendered on the products linked to them but be hidden from the **new product** picker.

#### Media + documents

- **FR-014**: Media upload MUST go through spec 003's storage abstraction; the UI MUST never embed a direct provider URL.
- **FR-015**: Uploads MUST land under a draft-scoped bucket prefix while the parent product is in draft; the prefix is cleared atomically on publish.
- **FR-016**: Discarded drafts MUST trigger orphan-media sweep within 24 hours (sweeper owned by spec 005 backend).
- **FR-017**: The editor MUST show variant previews (thumbnail, mid, large) generated by spec 005's media-variant worker.
- **FR-018**: Document upload MUST accept the file types spec 005 declares (e.g., PDF datasheets) and surface them in a dedicated **Documents** sub-section.

#### Restricted-product metadata

- **FR-019**: The restricted-flag editor MUST include AR and EN rationale fields, both required when the flag is true.
- **FR-020**: Toggling the flag MUST emit an audit event with before / after state including the rationale.
- **FR-021**: Restricted products MUST remain visible (with prices) on the customer app per Constitution Principle 8 — this spec only manages the metadata.

#### Bulk import / export

- **FR-022**: The export action MUST stream a CSV containing every product within the admin's role scope.
- **FR-023**: The import action MUST require a server-side dry-run before commit and MUST reject any upload exceeding the configured row cap (default 5000).
- **FR-024**: The dry-run response MUST be a downloadable validation report (CSV) listing row number, column, and reason code per failure.
- **FR-025**: The commit action MUST require explicit confirmation of the validated row count and MUST be all-or-nothing per upload.
- **FR-026**: Each row in a successful import MUST emit an audit event captured by spec 015's reader.

#### Publish workflow

- **FR-027**: A product MUST move through the explicit states `draft`, `scheduled`, `published` — matching spec 005's `product_state_transitions` table.
- **FR-028**: Scheduled products MUST hold a future-dated publish time and MUST NOT be visible on the customer app until reached.
- **FR-029**: Editing a `published` product MUST create a new draft revision (copy-on-write); the admin chooses overwrite or queue-as-scheduled at next publish.
- **FR-030**: Every state transition MUST emit an audit event.

#### Architectural guardrails

- **FR-030a**: The full set of permission keys this spec consumes — `catalog.read`, `catalog.product.read`, `catalog.product.write`, `catalog.product.bulk_import`, `catalog.product.export`, `catalog.category.read`, `catalog.category.write`, `catalog.brand.read`, `catalog.brand.write`, `catalog.manufacturer.read`, `catalog.manufacturer.write` — MUST be registered in spec 015's `contracts/permission-catalog.md` (see spec 015 FR-028b).
- **FR-030b**: When spec 005's bulk-import CSV header schema document is missing on day 1 (`spec-005:gap:csv-schema-publication` per `contracts/csv-format.md`), the bulk-import wizard MUST render the upload step with a localized "schema not yet published" notice and disable the upload control. Admins fall back to one-by-one editing via the product editor until the schema lands. This is a degradation path, not a permanent state.
- **FR-031**: This spec MUST NOT modify any backend contract. Any gap escalates to spec 005 (catalog) per Phase 1C intent.
- **FR-032**: All API access MUST go through spec 015's auth-proxy + generated-client layer — no ad-hoc fetch.
- **FR-033**: All shell primitives (`AppShell`, `DataTable`, `FormBuilder`, state primitives) MUST come from spec 015. Catalog-specific primitives (CategoryTree, MediaPicker, RichEditor for AR/EN content) live under `apps/admin_web/components/catalog/`.

### Key Entities *(client-side state — no backend persistence introduced)*

- **ProductDraft**: id, sku, names (ar/en), descriptions (ar/en), brand id, category ids, attribute map, restricted flag + rationale (ar/en), media references, document references, state (`draft`/`scheduled`/`published`), scheduled-publish-at, row version.
- **CategoryNode**: id, parent id, label (ar/en), order, active, product count.
- **Brand / Manufacturer**: id, name (ar/en), logo media ref, active.
- **MediaUpload**: file, content type, draft scope, upload progress, variant urls when ready, error state on failure.
- **BulkImportSession**: id, uploaded csv path, dry-run report path, validated row count, errored row count, status (`uploaded` / `validated` / `committed` / `failed`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new product can be created and published end-to-end in under 4 minutes by an admin already familiar with the editor.
- **SC-002**: ≥ 99 % of save attempts on a fully-validated product form succeed without retry.
- **SC-003**: 100 % of catalog screens render correctly in both Arabic-RTL and English-LTR — measured by the launch-blocker visual-regression checklist (inherits spec 015's mechanism).
- **SC-004**: Drag-reorder on the category tree commits within 1 second of drop on the staging dataset (10k categories).
- **SC-005**: Bulk import dry-run on a 5000-row CSV returns the validation report in under 30 seconds.
- **SC-006**: Bulk import commit on a fully-validated 5000-row CSV completes in under 60 seconds.
- **SC-007**: A scheduled publish reaches the customer app within 60 seconds of its scheduled time on the staging environment.
- **SC-008**: 0 user-visible English strings on any catalog screen when the active locale is Arabic.
- **SC-009**: 0 backend contract changes shipped from this spec — escalations tracked as separate spec-005 issues.

## Assumptions

- **Spec 015 shell**: Available — this spec mounts inside it. The audit-log reader from 015 is the read surface for every audit event this spec emits server-side via spec 005.
- **Spec 005 contracts merged**: Required by the implementation plan. If any contract is missing (e.g., scheduled-publish endpoint), the gap is filed against spec 005 and the corresponding UI surfaces as a placeholder in this spec until the gap closes.
- **Storage abstraction**: Media + document uploads use spec 003's storage seam. The UI never references provider URLs directly — only the abstraction's signed-URL or proxy paths.
- **Sweeper for orphan drafts**: Owned by spec 005's backend worker. This spec assumes a 24-hour sweep cadence; the actual cadence is an operations decision documented in spec 005.
- **CSV format**: The bulk-import CSV header schema is defined by spec 005. This spec follows whatever schema 005 publishes; an issue is filed if the schema is incomplete.
- **Drag-and-drop library**: A small, accessible drag-and-drop primitive sourced from the existing shadcn ecosystem (`@dnd-kit/core` or similar) — research to nail the choice; not dictated here.

## Dependencies

- **Spec 003 (foundations)** — storage abstraction + audit emission
- **Spec 005 (catalog)** — every backend contract this spec consumes
- **Spec 015 (admin foundation)** — shell, auth proxy, `DataTable`, `FormBuilder`, state primitives, audit-log reader

## Out of Scope (this spec)

- Pricing CRUD (spec 007 admin surfaces are handled in a later spec).
- Inventory CRUD (spec 017).
- Search reindex controls (later admin spec).
- Storefront customer-facing rendering (spec 014).
- Verification approval workflow (spec 020).
- B2B-specific catalog views (spec 021).
