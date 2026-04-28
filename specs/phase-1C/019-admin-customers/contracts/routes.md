# Customers Routes

All routes inside spec 015's `(admin)` group; same auth-proxy + middleware order.

| Path | Permission required | Notes |
|---|---|---|
| `/customers` | `customers.read` | Customer list. |
| `/customers?roleScope=company` | `customers.read` + `customers.b2b.read` | The **Companies** sub-entry (spec FR-001). Same `/customers` page rendered with a preset `roleScope=company_owner` filter chip; the chip cannot be removed without leaving the entry. Hidden in the sidebar when `customers.b2b.read` is missing. |
| `/customers?accountState=suspended` | `customers.read` | The **Suspended** sub-entry (spec FR-001). Same `/customers` page rendered with a preset `accountState=suspended` filter chip; the chip cannot be removed without leaving the entry. |
| `/customers/[customerId]` | `customers.read` | Profile detail. |
| `/customers/[customerId]/addresses` | `customers.read` | Address book expanded. |
| `/customers/[customerId]/company` | `customers.b2b.read` | B2B company drill (only when the profile is B2B + permission held). |

## Action endpoints (proxied by Next.js)

| Action | Method + path | Permission required | Step-up |
|---|---|---|---|
| Suspend | `POST /api/customers/[customerId]/suspend` | `customers.suspend` | Required |
| Unlock | `POST /api/customers/[customerId]/unlock` | `customers.unlock` | Required |
| Password-reset trigger | `POST /api/customers/[customerId]/password-reset` | `customers.password_reset.trigger` | Required |

All three actions:

- Carry `Idempotency-Key`.
- Require `X-StepUp-Assertion`.
- Carry the reason note as JSON body (`{ "reasonNote": "..." }`).
- Emit an audit event server-side; the audit-log reader (spec 015) is the read surface.

## Permission matrix (initial)

```
customers.read
customers.pii.read                  # email / phone visibility (FR-007)
customers.b2b.read                  # Company card + branches drill (FR-021)
customers.suspend
customers.unlock
customers.password_reset.trigger
```

The shell already enforces middleware permission checks. New keys above are added to spec 004's permission catalog via the standard escalation channel (file `spec-004:gap:customers-permissions` if any are missing on day 1).

## Sidebar group registration

When the admin's nav-manifest includes any `customers.*` read key, spec 015's sidebar surfaces a "Customers" group with sub-entries:

- **Customers** (always visible when `customers.read` is held)
- **Companies** (visible when `customers.b2b.read` is held — links to a market-level B2B view; per-customer drill happens through individual profiles)
- **Suspended** (always visible when `customers.read` is held — pre-filtered list for `accountState = suspended`)

Entries are contributed via the manifest, never hard-coded.
