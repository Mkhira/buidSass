# Audit-log Reader Redaction Policy

The audit-log reader (spec 015 FR-017 – FR-022a) renders the raw before/after JSON of every event spec 003 emits. Without redaction, an admin holding `audit.read` could read PII (customer emails, phones), restricted-product rationale, suspend reason notes — fields each module owns and gates separately.

This document is the **single registry** of JSON paths the reader MUST treat as redacted when the viewing admin lacks the corresponding permission. Each emitting spec appends its sensitive paths here.

Server-side enforcement (the audit-read endpoint pre-redacts the JSON) is the source of truth. The client `<MaskedField>` is defence-in-depth.

## Rules

1. A path listed below MUST be replaced with the standard `<MaskedField>` placeholder when the admin lacks the listed permission. The mask glyph (`••• @•••.com`, `+••• ••• ••• ••12`, or generic `•••` for non-email/phone) is the same one used elsewhere in the admin app — single component, single behaviour.
2. The redaction applies to **both** the `before` and `after` blobs.
3. JSON path syntax: dot-segmented, `*` matches any array index. E.g., `lines.*.customer.email`.
4. New audit emitters MUST add their sensitive paths here in the same PR that introduces the emission. CI rejects an emitter that lands without an entry.

## Registry

### Spec 004 (identity) — customer-related events

| Path | Required permission to see unredacted |
|---|---|
| `customer.email` | `customers.pii.read` OR `orders.pii.read` (whichever applies for the event's actor context) |
| `customer.phone` | same as above |
| `customer.displayName` | `customers.read` (always shown if the admin is allowed to see the customer at all) |
| `address.line1`, `address.line2`, `address.phone`, `address.postalCode`, `address.recipient` | `customers.pii.read` |
| `lockoutState.reasonNote` | `customers.read` (visible to support; redacted only for `audit.read`-only admins) |

### Spec 016 (catalog)

| Path | Required permission to see unredacted |
|---|---|
| `restrictedRationale.ar`, `restrictedRationale.en` | `catalog.product.read` (auditors with only `audit.read` see the placeholder) |

### Spec 018 (orders)

| Path | Required permission to see unredacted |
|---|---|
| `customer.email`, `customer.phone` | `orders.pii.read` |
| `refund.reasonNote` | `orders.refund.initiate` OR `orders.read` |
| `cancel.reasonNote` | `orders.cancel` OR `orders.read` |

### Spec 019 (customers)

| Path | Required permission to see unredacted |
|---|---|
| `accountAction.reasonNote` | `customers.suspend` OR `customers.unlock` OR `customers.password_reset.trigger` (any one suffices) |

## Resource-type registry

The `<AuditForResourceLink resourceType=…>` shell primitive (spec 015 FR-028f) and the audit-reader's `?resourceType=…` filter accept the following values. Adding a new resource type is an append here + the corresponding emission registration in spec 003.

| `resourceType` value | Owning spec | Used by |
|---|---|---|
| `Product` | 005 | 016 product editor |
| `Category` | 005 | 016 category editor |
| `Brand` | 005 | 016 brand editor |
| `Manufacturer` | 005 | 016 manufacturer editor |
| `Sku` | 008 | 017 SKU detail (synonym for product when SKU = product) |
| `Warehouse` | 008 | 017 (read-only this phase) |
| `Batch` | 008 | 017 batch detail |
| `Reservation` | 008 | 017 reservations table |
| `Order` | 011 | 018 order detail |
| `Refund` | 013 | 018 refund timeline entry |
| `Invoice` | 012 | 018 invoice section |
| `Customer` | 004 | 019 customer profile |
| `AdminAccount` | 004 | (future — admin-on-admin actions, e.g., role grants; not in Phase 1C) |

## Adding new emitters

When a new spec lands a new audit emission containing PII or sensitive free-text:

1. Append a row to the registry with the JSON path + the required permission.
2. Implement client-side `<MaskedField>` wrapping in the audit-detail JSON viewer for the new path.
3. File the redaction expectation against spec 003 / 004 if the server-side pre-redaction isn't already supported (`spec-XXX:gap:audit-redaction-<path>`).

## Drift CI

```bash
pnpm catalog:check-audit-redaction
# Walks every audit emission seeded by /e2e fixtures and asserts that each
# sensitive path either: (a) is in the registry above, or (b) has matching
# server-side redaction. Fails the build on a path that's neither.
```
