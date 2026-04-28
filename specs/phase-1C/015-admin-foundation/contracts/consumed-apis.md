# Consumed APIs

UI-only spec (FR-029). Every contract below is **owned by another spec**; this document tabulates which OpenAPI document each surface consumes and where the generated TypeScript types land.

| Surface | Owning spec | OpenAPI source | Generated types path | Wrapper |
|---|---|---|---|---|
| `app/(auth)/login/`, `app/(auth)/mfa/`, `app/(auth)/reset/` | 004 identity | `services/backend_api/openapi.identity.json` | `lib/api/types/identity.ts` | `lib/api/clients/identity.ts` |
| `app/(admin)/audit/` | 003 foundations (audit-read) | `services/backend_api/openapi.json` (audit endpoints under `/v1/admin/audit-log`) | `lib/api/types/audit.ts` | `lib/api/clients/audit.ts` |
| `app/api/nav-manifest/` | 004 identity (nav-manifest endpoint) | `openapi.identity.json` | `lib/api/types/identity.ts` | `lib/api/clients/identity.ts` |
| `app/api/notifications/` (SSE + unread + mark-read) | 023 notifications (stub until 023 ships) | future `openapi.notifications.json` | `lib/api/types/notifications.ts` | `lib/api/clients/notifications.ts` |
| Saved views (FR-023) — `DataTable` reads/writes | 004 identity (user-preferences) | `openapi.identity.json` | `lib/api/types/identity.ts` | `lib/api/clients/identity.ts` |

## Generation strategy

- **Tool**: `openapi-typescript` CLI run by `pnpm gen:api`.
- **Output**: `lib/api/types/<svc>.ts`. Generated dir is gitignored.
- **Wrappers**: `lib/api/clients/<svc>.ts` are **hand-curated** thin wrappers that import the generated types and use `lib/api/proxy.ts` to add cookies + correlation id.
- **Drift guard**: CI compares the generated types' hash against a committed checksum and fails on drift between the OpenAPI doc in `services/backend_api/` and the generated types.

## Auth-proxy boundary

Every browser-issued request goes through a Next.js Route Handler under `app/api/`:

```
Browser → /api/<surface> (Next.js, has cookie) → backend.dental-commerce-api/<endpoint> (with Authorization: Bearer …)
```

The browser never sees the access token. The proxy route handler:

1. Reads the sealed iron-session cookie via `getIronSession(cookies(), …)`.
2. If `expiresAt` is within 60 s, refreshes the access token (server-side) and rotates the cookie.
3. Forwards the request to the backend with `Authorization: Bearer <access>` and an `X-Correlation-Id` UUID v4.
4. Maps backend error envelopes to the shared `lib/api/error.ts` shape so the UI sees a consistent error model.

## Headers attached to every backend call

| Header | Source | Purpose |
|---|---|---|
| `Authorization: Bearer <access>` | sealed cookie | Spec 004 auth |
| `X-Correlation-Id: <uuid>` | per-request UUID | Spec 003 audit + observability |
| `Accept-Language: ar-SA \| en-SA \| ar-EG \| en-EG` | locale cookie + role scope | Localized server responses |
| `X-Market-Code: ksa \| eg \| platform` | session role-scope | Per-market server behaviour |

## Escalation policy

A backend gap discovered during build is **never patched here** (FR-029). File an issue against the owning spec with the prefix `spec-XXX:gap:<short-description>` and cross-link from the relevant `apps/admin_web/` TODO comment. Block on the 1B fix only if it affects a P1 acceptance scenario.
