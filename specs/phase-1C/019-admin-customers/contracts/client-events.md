# Client-emitted events (customers module)

Adds to spec 015's telemetry vocabulary. Same `TelemetryAdapter`, same PII guardrails — extra strict here because customer data is the highest-PII surface in the admin app.

| Event | Trigger | Properties |
|---|---|---|
| `customers.list.opened` | `/customers` rendered | `filter_keys` (sorted, no values), `row_count_bucket` |
| `customers.list.filter.applied` | Filter changed | `filter_kind` (closed enum) |
| `customers.list.search.initiated` | Search submitted (after debounce) | — (NO query string, NO result count beyond bucket) |
| `customers.profile.opened` | `/customers/[id]` rendered | `b2b` (bool), `account_state` (closed enum) |
| `customers.profile.addresses.expanded` | "View all" tapped | `address_count_bucket` |
| `customers.profile.orders_summary.cache_hit` | SWR served from cache | — |
| `customers.profile.orders_summary.cache_miss` | SWR triggered fetch | — |
| `customers.profile.company_card.opened` | `<CompanyCard>` rendered | `kind` ('company_owner' / 'company_member'), `branch_count_bucket` |
| `customers.profile.company_chip.tapped` | Branch / member chip clicked | `target_kind` (closed enum) |
| `customers.action.tapped` | Suspend / unlock / password-reset tapped | `action` (closed enum) |
| `customers.action.confirmed` | Confirmation dialog accepted | `action` |
| `customers.action.step_up.required` | Threshold check (always true for these actions) | `action` |
| `customers.action.step_up.succeeded` | Step-up dialog completed | `action` |
| `customers.action.step_up.cancelled` | Admin cancelled step-up | `action` |
| `customers.action.submitted` | Submit success | `action` |
| `customers.action.conflict_detected` | 412 returned | `action` |
| `customers.action.failed` | Other error | `action`, `reason_code` |
| `customers.history_panel.opened` | Verification / quote / support panel rendered | `panel_kind`, `mode` ('placeholder' / 'shipped') |
| `customers.history_panel.copy_id` | Copy-id affordance on a placeholder used | `panel_kind` |

## PII guard rails

- **No customer id, customer email, customer phone, display name, search query, reason-note text, address fields, company name, branch name, role values** are ever emitted. The client telemetry stream is intentionally stripped.
- `*_bucket` properties collapse counts into closed-enum power-of-10 ranges.
- `reason_code` values come from spec 004's closed catalog.
- `account_state` is one of three closed values (`active` / `suspended` / `closed`).
- The PII-leak unit test `tests/unit/customers/telemetry.pii-guard.test.ts` asserts every event's property set against the allow-list. The test is critical — accidental PII leak in telemetry is a compliance issue.

## Why no PII-view audit

Per Q4 — server-side data-access in spec 004 is the audit-bearing surface. Auditing every redacted page render in the client would (a) flood the audit log with non-actionable events, (b) introduce an audit-emission code path on the client that only matters for paranoia, not accountability. Real audit happens where the data is fetched.
