# Phase 1 Data Model: Admin Catalog

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-27

UI-only spec (FR-031). Every entity below is a **client-side view-model** consumed by Server / Client Components. No new server-side tables.

---

## Client view models

### `ProductViewModel`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `string` | spec 005 | |
| `sku` | `string` | spec 005 | Immutable post-creation. |
| `name` | `{ ar: string; en: string }` | spec 005 | Both required. |
| `description` | `{ ar: ProseMirrorJson; en: ProseMirrorJson }` | spec 005 | Tiptap output (R4). |
| `brandId` | `string \| null` | spec 005 | |
| `manufacturerId` | `string \| null` | spec 005 | |
| `categoryIds` | `string[]` | spec 005 | At least one required for publish. |
| `attributes` | `Record<string, unknown>` | spec 005 | Schema per category (spec 005). |
| `restricted` | `boolean` | spec 005 | |
| `restrictedRationale` | `{ ar: string; en: string } \| null` | spec 005 | Required when `restricted` is `true`. |
| `mediaIds` | `string[]` | spec 005 | Order matters for gallery. |
| `documentIds` | `string[]` | spec 005 | |
| `state` | `'draft' \| 'scheduled' \| 'published'` | spec 005 | See SM-1. |
| `scheduledPublishAt` | `Date \| null` | spec 005 | Required iff `state === 'scheduled'`. |
| `rowVersion` | `number` | spec 005 | Optimistic concurrency. |
| `pricingRefSummary` | `{ minorAmount: number; currency: string }` | spec 007 (read-only) | Editor surfaces but doesn't edit. |
| `inventoryRefSummary` | `{ totalAvailable: number }` | spec 008 (read-only) | Editor surfaces but doesn't edit. |

### `ProductDraftLocalState` (transient — survives unsaved-changes guard)

| Field | Type | Notes |
|---|---|---|
| `dirty` | `boolean` | Triggers FR-005 dirty-state dialog. |
| `tabActive` | `'ar' \| 'en'` | UI only; persists via session storage. |
| `inFlightUploads` | `Map<string, UploadStatus>` | IndexedDB-backed (Uppy GoldenRetriever). |
| `conflictDetected` | `boolean` | True after a 412 response (R6). |

### `CategoryNode`

| Field | Type |
|---|---|
| `id` | `string` |
| `parentId` | `string \| null` |
| `label` | `{ ar: string; en: string }` |
| `order` | `number` |
| `active` | `boolean` |
| `productCount` | `number` |
| `childIds` | `string[]` |

### `BrandViewModel` / `ManufacturerViewModel`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `name` | `{ ar: string; en: string }` | |
| `logoMediaId` | `string \| null` | |
| `manufacturerId` (brand only) | `string \| null` | Manufacturer linkage |
| `active` | `boolean` | |

### `MediaUpload` (transient)

| Field | Type | Notes |
|---|---|---|
| `localId` | `string` | UUID, used for optimistic UI. |
| `file` | `File` | Browser File handle. |
| `productDraftId` | `string` | |
| `progress` | `0–100` | |
| `status` | `'queued' \| 'uploading' \| 'processing' \| 'done' \| 'error'` | |
| `mediaId` | `string \| null` | Populated on `done`. |
| `variantUrls` | `{ thumb: string; mid: string; large: string } \| null` | Populated on `done`. |
| `error` | `{ reasonCode: string; message: string } \| null` | |

### `BulkImportSession`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `string` | spec 005 | |
| `status` | `'uploaded' \| 'validating' \| 'validated' \| 'committing' \| 'committed' \| 'failed'` | spec 005 | See SM-2. |
| `uploadedRowCount` | `number` | spec 005 | |
| `validatedRowCount` | `number` | spec 005 | Available after `validated`. |
| `erroredRowCount` | `number` | spec 005 | |
| `validationReportUrl` | `string \| null` | spec 005 | Signed URL (storage abstraction). |
| `submittedBy` | `string` | spec 005 | Admin id. |
| `createdAt` | `Date` | spec 005 | |

---

## Client state machines

### SM-1: `ProductPublishState`

States: `Draft`, `Scheduled`, `Published`, `Discarded` (terminal — orphans get swept).

| From | To | Trigger | Notes |
|---|---|---|---|
| `Draft` | `Scheduled` | Admin chooses **Schedule** with future timestamp | Persists `scheduledPublishAt`. |
| `Draft` | `Published` | Admin chooses **Publish now** | Atomic flip + draft-tag clear on media. |
| `Draft` | `Discarded` | Admin chooses **Discard draft** | Confirmation dialog; sweeper cleans media within 24 h. |
| `Scheduled` | `Published` | Server clock reaches `scheduledPublishAt` | Backend job; UI sees the new state on refresh. |
| `Scheduled` | `Draft` | Admin reverts the schedule | Editor re-opens. |
| `Published` | `Draft` | Admin edits any field | Copy-on-write — published version stays live; new draft revision created. |

Failure handling: a 412 (conflict, optimistic concurrency) on any save returns the editor to the "your version is stale, reload?" overlay; once reloaded, the state machine resumes from the server-truth state.

### SM-2: `BulkImportSessionState`

States: `Uploaded`, `Validating`, `Validated`, `Committing`, `Committed`, `Failed`.

| From | To | Trigger | Notes |
|---|---|---|---|
| `Uploaded` | `Validating` | spec 005 dry-run accepted | |
| `Validating` | `Validated` | spec 005 dry-run done | Report URL surfaces. |
| `Validating` | `Failed` | spec 005 dry-run rejects | Surfaces a single header / cap error. |
| `Validated` | `Committing` | Admin clicks **Commit** with explicit `expectedRowCount` | |
| `Committing` | `Committed` | spec 005 commit returns success | Audit emitted per row. |
| `Committing` | `Failed` | spec 005 commit aborts | All-or-nothing — no partial writes. |

### SM-3: `CategoryTreeMutation` (per-edit micro state)

Used to drive optimistic UI on the tree editor.

States: `Idle`, `OptimisticCommit`, `Confirmed`, `Reverted`.

| From | To | Trigger |
|---|---|---|
| `Idle` | `OptimisticCommit` | Drag drop / add / deactivate / label-edit |
| `OptimisticCommit` | `Confirmed` | spec 005 acknowledges |
| `OptimisticCommit` | `Reverted` | spec 005 rejects (e.g., 412 conflict) — UI rolls back + shows toast |

---

## Validation rules (client-side)

| Field | Rule |
|---|---|
| Product `sku` | required at create; immutable; non-whitespace; unique per spec 005. |
| `name.ar`, `name.en` | required; max 200 chars each. |
| `description.ar`, `description.en` | required; ProseMirror JSON; only allow-listed marks/blocks (R4). |
| `categoryIds` | required, at least one, before publish. |
| `restrictedRationale.ar`, `restrictedRationale.en` | required iff `restricted === true`. |
| `scheduledPublishAt` | required iff `state === 'scheduled'`; must be future-dated. |
| Category label `ar`, `en` | both required; max 100 chars. |
| Brand / manufacturer `name.ar`, `name.en` | both required; max 100 chars. |
| Bulk import upload | header schema match per spec 005; row count ≤ env cap (default 5000). |

---

## Forward-compat reservations

- `ProductViewModel.attributes` is `Record<string, unknown>` so a category's attribute schema can grow without view-model changes.
- `ProductViewModel.vendorId` (Phase 2) — when spec 005 ships it, the editor adds a vendor picker; current rendering ignores unknown server fields.
- `BulkImportSession` fields can grow (e.g., `progressPercent`) without breaking the wizard.
