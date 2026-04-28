# Phase 1 Data Model: Admin Customers

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-27

UI-only spec (FR-026). Every entity below is a **client-side view-model**. No new server-side tables.

---

## Client view models

### `CustomerListRow`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `string` | spec 004 | |
| `displayName` | `string` | spec 004 | Always renders. |
| `emailMasked`, `phoneMasked` | `string` | spec 004 | Server pre-redacts when admin lacks `customers.pii.read`. Client also wraps in `<MaskedField>` (R1 — defence-in-depth). |
| `marketCode` | `'ksa' \| 'eg'` | spec 004 | |
| `b2bFlag` | `boolean` | spec 004 | Convenience for filter rendering. |
| `verificationState` | `string` | spec 004 | Closed enum from spec 004 / 020 catalog. |
| `accountState` | `'active' \| 'suspended' \| 'closed'` | spec 004 | |
| `lastActiveAt` | `Date` | spec 004 | |
| `createdAt` | `Date` | spec 004 | |

### `CustomerProfile`

| Field | Type | Source | Notes |
|---|---|---|---|
| All `CustomerListRow` fields | as above | spec 004 | |
| `email`, `phone` | `string \| null` | spec 004 | Raw values; server null-redacts when admin lacks `customers.pii.read`. Always rendered through `<MaskedField>`. |
| `locale` | `'ar' \| 'en'` | spec 004 | The customer's preferred locale. |
| `roles` | `Array<{ key: string; labelKey: string; scope: 'customer' \| 'company' }>` | spec 004 | |
| `addressesPreview` | `Address[]` | spec 004 | Top 3 (with default-flag); "view all" expands inline. |
| `addressesCount` | `number` | spec 004 | |
| `ordersSummary` | `{ count: number; mostRecentOrderId?: string; mostRecentOrderNumber?: string }` | spec 011 | SWR-cached 60 s (R7). |
| `companyLinkage` | `CompanyLinkage \| null` | spec 004 | Null for B2C customers; populated for `customer.company_owner` / `customer.company_member`. |
| `rowVersion` | `number` | spec 004 | Optimistic concurrency for admin actions. |

### `Address`

| Field | Type | Notes |
|---|---|---|
| `id`, `label`, `recipient`, `line1`, `line2`, `city`, `region`, `country`, `postalCode`, `phone` | spec 004 | Phone passes through `<MaskedField phoneKind>`. |
| `marketCode` | `'ksa' \| 'eg'` | |
| `isDefault` | `boolean` | At most one default per customer. |

### `CompanyLinkage`

| Field | Type | Notes |
|---|---|---|
| `kind` | `'company_owner' \| 'company_member'` | Determines which sub-list renders. |
| `parentCompany` | `{ id, name, active }` | |
| `branches` | `Array<{ id, name, active }> \| null` | Populated when `kind === 'company_owner'`. |
| `members` | `Array<{ id, displayName, role, active }> \| null` | Populated when `kind === 'company_owner'`. |
| `approverPreview` | `{ requiresApproval: boolean; threshold?: number } \| null` | Read-only preview; full settings in spec 021. |

### `AccountActionDraft`

| Field | Type | Notes |
|---|---|---|
| `customerId` | `string` | |
| `action` | `'suspend' \| 'unlock' \| 'password_reset_trigger'` | |
| `reasonNote` | `string` | Required, ≥ 10 chars, ≤ 2000. |
| `idempotencyKey` | `string` | UUID v4; rotated only on 412 reload. |
| `stepUpAssertionId` | `string \| null` | Populated after step-up dialog success. |

### `HistoryPanelMode`

| Field | Type | Notes |
|---|---|---|
| `flagKey` | `'adminVerificationsShipped' \| 'adminQuotesShipped' \| 'adminSupportShipped'` | |
| `mode` | `'placeholder' \| 'shipped'` | Resolved at render time from the env flag. |

---

## Client state machines

### SM-1: `AccountActionSubmissionState`

States: `Idle`, `Confirming`, `StepUpRequired`, `StepUpInProgress`, `StepUpFailed`, `Submitting`, `Submitted`, `ConflictDetected` (412), `Failed` (recoverable), `FailedTerminal`.

| From | To | Trigger | Notes |
|---|---|---|---|
| `Idle` | `Confirming` | `ActionTapped` | Opens confirmation dialog. |
| `Confirming` | `Idle` | Cancel | |
| `Confirming` | `StepUpRequired` | Reason note ≥ 10 chars + Confirm tapped | Step-up always required for these actions per FR-013. |
| `StepUpRequired` | `StepUpInProgress` | Step-up dialog mounts | spec 004 challenge. |
| `StepUpInProgress` | `Submitting` | Step-up assertion succeeds | Assertion id attached. |
| `StepUpInProgress` | `StepUpFailed` | Step-up rejected / cancelled | Returns to `Idle` with toast. |
| `Submitting` | `Submitted` | spec 004 returns 2xx | Surgical cache invalidation per R6. |
| `Submitting` | `ConflictDetected` | 412 row-version | Overlay preserves draft. |
| `Submitting` | `Failed` | 5xx / network | Retry with same idempotency key. |
| `Submitting` | `FailedTerminal` | Server returns `customers.permission_revoked` | Routes to forbidden screen. |

### SM-2: `HistoryPanelMode`

A trivial two-state machine driven by env flags at module init:

| Flag value | Mode |
|---|---|
| `1` / true | `shipped` (renders the data list) |
| anything else | `placeholder` |

Flag flips require redeploy; no runtime transitions.

---

## Validation rules (client-side)

| Field | Rule |
|---|---|
| `reasonNote` (account actions) | required, 10 ≤ length ≤ 2000 |
| Free-text search query | trimmed, non-empty, ≤ 200 chars; debounced 300 ms before fire |
| Address postal code | rendered as-is; no edit in v1 (FR-019) |
| Self-action guard | client refuses any account action where `action.customerId === currentSession.adminId`; server is also authoritative |

---

## Forward-compat reservations

- `CustomerProfile.suspendedReason` (display-only, read from spec 004's lockout-state record) — admins can see why a customer is suspended without going to the audit-log reader. Surface lands when spec 004 publishes the field.
- `CompanyLinkage.vendorScope` (Phase 2) — when 021 ships vendor scope, the company card surfaces vendor info; current rendering ignores unknown server fields.
- `roles[].permissions` flattened list — useful for admins debugging "why can't this customer do X?"; not in v1 since `customers.pii.read` and friends apply to the **admin**, not the customer being viewed.
