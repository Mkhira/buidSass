# Phase 1 Data Model: Admin Foundation

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-27

> **Scope reminder**: UI-only spec (FR-029). All entities below are **client-side view models** consumed by Server / Client Components. No new server-side tables.

---

## Client-side view models

### `AdminSession` (sealed cookie payload + decoded server-side)

| Field | Type | Source | Notes |
|---|---|---|---|
| `adminId` | `string` | spec 004 | Stable id. |
| `email` | `string` | spec 004 | Top-bar identity. |
| `displayName` | `string` | spec 004 | |
| `roleScope` | `'platform' \| 'ksa' \| 'eg'` | spec 004 | Drives the top-bar market badge (FR-015). |
| `roles` | `string[]` | spec 004 | E.g. `['admin.super', 'admin.finance']`. |
| `permissions` | `Set<string>` | spec 004 | Flat permission keys. Used by per-route middleware check. |
| `mfaEnrolled` | `boolean` | spec 004 | Surfaces enrolment CTA if false and required. |
| `accessToken` | `string` (sealed in cookie) | spec 004 | Never leaves the server. |
| `refreshToken` | `string` (sealed in cookie) | spec 004 | Never leaves the server. |
| `expiresAt` | `Date` | derived from JWT | Triggers proactive refresh when within 60 s. |

### `NavigationManifest`

| Field | Type | Source | Notes |
|---|---|---|---|
| `groups` | `NavigationGroup[]` | spec 004 nav-manifest endpoint | Sidebar groups (e.g. "Operations", "Catalog", "Finance"). |

#### `NavigationGroup`

| Field | Type |
|---|---|
| `id` | `string` |
| `labelKey` | `string` (i18n key) |
| `entries` | `NavigationEntry[]` |
| `order` | `number` |

#### `NavigationEntry`

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `labelKey` | `string` (i18n key) | |
| `iconKey` | `string` | Maps to a `lucide-react` icon |
| `route` | `string` | Next.js route, locale-neutral |
| `requiredPermissions` | `string[]` | Server-asserted in middleware too |
| `order` | `number` | |
| `badgeCountKey?` | `string` | E.g. `pending_returns_count` — wired in 1D specs |

### `AuditFilter`

| Field | Type | Notes |
|---|---|---|
| `actor` | `string?` | Admin email or id |
| `resourceType` | `string?` | E.g. `Product`, `Order` |
| `resourceId` | `string?` | |
| `actionKey` | `string?` | |
| `marketScope` | `'platform' \| 'ksa' \| 'eg' \| undefined` | |
| `timeframe` | `{ from: Date; to: Date }` | Default last 7 d |

### `AuditEntry`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `string` | spec 003 | |
| `actor` | `{ id: string; email: string; role: string }` | spec 003 | |
| `actionKey` | `string` | spec 003 | E.g. `catalog.product.updated` |
| `resourceType` | `string` | spec 003 | |
| `resourceId` | `string` | spec 003 | |
| `marketScope` | `'platform' \| 'ksa' \| 'eg'` | spec 003 | |
| `correlationId` | `string` | spec 003 | |
| `before` | `unknown` (JSON) | spec 003 | Renders in the diff viewer |
| `after` | `unknown` (JSON) | spec 003 | |
| `occurredAt` | `Date` | spec 003 | |

### `Notification`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `string` | spec 023 | |
| `kindKey` | `string` | spec 023 | E.g. `returns.pending_approval` |
| `titleKey`, `bodyKey` | `string` | spec 023 | i18n keys |
| `deepLink` | `string` | spec 023 | Resolved client-side against the route table |
| `occurredAt` | `Date` | spec 023 | |
| `read` | `boolean` | spec 023 | |

### `SavedView`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `string` | spec 004 user-preferences | |
| `screenKey` | `string` | client | E.g. `audit.list` |
| `name` | `string` | client | User-supplied |
| `filterBlob` | `unknown` (JSON) | client | Round-tripped opaque |
| `sortBlob` | `unknown` (JSON) | client | |

---

## Client-side state machines

### SM-1: `AdminSession`

States: `Anonymous`, `Authenticating`, `MfaRequired`, `Authenticated`, `Refreshing`, `RefreshFailed`, `LoggingOut`.

| From | To | Trigger | Notes |
|---|---|---|---|
| `Anonymous` | `Authenticating` | `LoginSubmitted` | Server route handler calls spec 004 sign-in. |
| `Authenticating` | `MfaRequired` | spec 004 returns `mfa_required` | UI routes to MFA entry. |
| `MfaRequired` | `Authenticating` | `MfaCodeSubmitted` | |
| `Authenticating` | `Authenticated` | spec 004 returns access + refresh | Cookie set + server-side hydrated. |
| `Authenticating` / `MfaRequired` | `Anonymous` | spec 004 4xx | Editorial-grade error state per FR-016. |
| `Authenticated` | `Refreshing` | within 60 s of `expiresAt` | Server-side; transparent to user. |
| `Refreshing` | `Authenticated` | spec 004 refresh OK | New cookie issued. |
| `Refreshing` | `RefreshFailed` | spec 004 refresh 401 | Cookie cleared; UI prompts re-auth with `?continueTo=…`. |
| `Authenticated` | `LoggingOut` | `LogoutSubmitted` | |
| `LoggingOut` | `Anonymous` | spec 004 revoke OK + cookie cleared | |

### SM-2: `NavigationManifest`

States: `NotLoaded`, `Loading`, `Loaded`, `Stale` (manifest changed server-side), `Error`.

Triggers: session change (re-fetch), 403 from a route the admin previously had permission for (re-fetch + transition to `Stale` then `Loaded`).

### SM-3: `NotificationFeed`

States: `Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `PollingFallback`.

| From | To | Trigger |
|---|---|---|
| `Disconnected` | `Connecting` | shell mounted |
| `Connecting` | `Connected` | SSE first message received |
| `Connected` | `Reconnecting` | SSE error / timeout |
| `Reconnecting` | `Connected` | SSE re-established |
| `Reconnecting` | `PollingFallback` | exponential backoff exceeds 30 s |
| `PollingFallback` | `Reconnecting` | next 60 s tick; retry SSE |
| any | `Disconnected` | shell unmounted (logout / nav away) |

---

## Validation rules (client-side echo)

| Field | Rule | Owner |
|---|---|---|
| Sign-in email | RFC 5322-lite | spec 004 |
| Password | ≥ 12 chars (policy enforced server-side) | spec 004 |
| TOTP code | exactly 6 digits | spec 004 |
| Audit timeframe `to ≥ from` | client | this spec |
| Audit timeframe span ≤ 90 days | client (soft guard) | this spec — server may allow more |

---

## Forward-compat reservations

- `NavigationGroup.entries` items can carry a future `vendorScope` field for Phase 2 marketplace; the shell renders whatever the server sends and ignores unknown fields.
- `Notification.kindKey` is a string — new kinds added by future specs (014/020/etc.) need only register an i18n entry and a deep-link route.
