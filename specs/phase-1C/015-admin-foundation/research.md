# Phase 0 Research: Admin Foundation

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

Resolves every Technical-Context decision in `plan.md` to a concrete library / pattern with rationale and rejected alternatives.

---

## R1. Framework + render strategy — Next.js 14 App Router

- **Decision**: Next.js 14.2 App Router with Server Components by default (Q2). `output: 'standalone'` in `next.config.mjs` for the multi-stage `node:20-alpine` Dockerfile defined by Phase 1C-Infra.
- **Rationale**: ADR-006 lock + Q2 clarification. Server Components naturally read the httpOnly cookie at render time, kill the need for a client-side auth context, and keep the JS bundle small. App Router's parallel routes + intercepting routes also give us a clean way to render the audit-entry detail as a side-panel without losing the list page state.
- **Conventions**:
  - `app/(auth)` and `app/(admin)` are route groups. The latter has a `layout.tsx` that mounts the shell + checks auth via middleware.
  - Server Components fetch backend data directly via `lib/api/proxy.ts` (which reads the cookie from `cookies()`); Client Components fetch via `react-query` against the Next.js route handlers (which themselves proxy to the backend).
- **Alternatives rejected**: Pages Router (forfeits streaming + Server Components), Static export (incompatible with cookie auth), Remix (ADR-006 locked Next.js).

## R2. Auth proxy — `iron-session` + httpOnly cookies

- **Decision**: `iron-session` ^8 wraps the session payload (refresh token + access-token expiry hint) into a sealed, encrypted cookie set on `Set-Cookie` after a successful spec-004 sign-in. `httpOnly`, `Secure`, `SameSite=Strict`. Access tokens themselves are never persisted client-side — they're stored encrypted inside the iron-sealed cookie payload alongside the refresh token, and read only inside Server Components / Route Handlers.
- **Rationale**: Q1 chose httpOnly cookies. `iron-session` is the canonical Node.js sealed-cookie library, audited, well-maintained, and works seamlessly with Next.js App Router (`getIronSession(cookies(), …)`).
- **Refresh strategy**: every Server Component render checks `expiresAt`; if within 60 s of expiry, the route handler `/api/auth/refresh` is called server-side before the render, and the cookie is rotated. This is invisible to the user.
- **Logout**: `/api/auth/logout` calls spec 004 `/admin/sessions/{id}/revoke`, then clears the cookie via `Set-Cookie: …=; Max-Age=0`.
- **Alternatives rejected**: NextAuth.js (heavy-handed for this use; we already have a custom backend identity), JWT in localStorage (Q1 rejected), session-server (would add another stateful component to operate).

## R3. UI primitives — shadcn/ui + Tailwind + Radix

- **Decision**: `shadcn/ui` vendored components (copy-paste, not a runtime dep) + `tailwindcss` ^3.4 + `@radix-ui/react-*` primitives + `class-variance-authority` + `tailwind-merge` + `lucide-react` icons. Theming via `tailwind.config.ts` consuming `packages/design_system/tokens/`.
- **Rationale**: ADR-006 lock. shadcn's vendored model means we own the components — RTL adjustments and accessibility tweaks are local edits, not upstream PRs. Radix gives us accessible primitives (Dialog, Combobox, ContextMenu) without reinventing the wheel.
- **RTL handling**: Tailwind 3.4 supports logical properties (`ms-2`, `me-2`) that auto-flip with `dir`. We standardize on logical-property utilities everywhere; `tools/lint/no-physical-margins.ts` flags `mr-` / `ml-` / `pr-` / `pl-` outside vendored shadcn files.
- **Alternatives rejected**: MUI (heavy runtime, theming awkward), Chakra UI (less RTL-aware), Ant Design (visual mismatch with the customer app's design system).

## R4. i18n — `next-intl`

- **Decision**: `next-intl` ^3 with App Router. Message catalogs live in `messages/{en,ar}.json`. `dir` attribute on `<html>` driven by the active locale. Middleware extracts the locale from a cookie or `Accept-Language` header, sets it on the request, and prefixes routes with `[locale]` only if needed (per Assumptions, we keep URL paths locale-neutral and persist locale in a cookie).
- **Rationale**: First-class App Router support, proven RTL story (used by Vercel's own Arabic deployments), clean Server Component API (`useTranslations()` in Client Components, `getTranslations()` in Server Components).
- **Numerals + dates**: `Intl.NumberFormat` / `Intl.DateTimeFormat` with the active locale; KSA → `ar-SA` / `en-SA`, EG → `ar-EG` / `en-EG`. Western-Arabic numerals by default (consistent with the customer app's choice).
- **Lint**: `tools/lint/no-hardcoded-strings.ts` walks the AST under `app/`, `components/`, and `lib/` and rejects literal strings rendered as `<…>{'literal'}</…>`, `placeholder="literal"`, etc.
- **Alternatives rejected**: `react-i18next` (less Server-Component-friendly), `lingui` (similar capability but smaller community).

## R5. Data fetching + tables — `@tanstack/react-query` + `@tanstack/react-table`

- **Decision**:
  - **Server Components** fetch via `lib/api/proxy.ts` (no client cache needed).
  - **Client Components** fetch via `@tanstack/react-query` ^5 against the Next.js auth-proxy route handlers (`/api/audit`, `/api/notifications/...`). Stale-while-revalidate with sensible defaults; per-screen overrides in `lib/api/clients/<svc>.ts`.
  - **Tables**: `@tanstack/react-table` ^8 powers the shared `DataTable` (FR-023). Server-side pagination, column-driven filters, sortable columns, saved views.
- **Rationale**: `react-query` is the de-facto standard for Client-Component data; `react-table` is the de-facto standard for headless tables. Both are framework-agnostic and let us keep table state separate from UI.
- **Saved views**: Persisted via spec 004's user-preferences endpoint (Q3); the `DataTable` exposes a `viewKey` prop that maps to `admin_pref:dataTable:<viewKey>`.
- **Alternatives rejected**: SWR (works fine but smaller ecosystem for advanced cases like infinite scroll over cursor-paginated data), AG Grid (too heavy + commercial license).

## R6. Forms — `react-hook-form` + `zod`

- **Decision**: `react-hook-form` ^7 for form state, `zod` ^3 for schemas, with `@hookform/resolvers/zod` to bridge. Schemas double as TypeScript types via `z.infer`. Server-side validation errors map back to fields via the route-handler error envelope shape (matches spec 003's error model).
- **Rationale**: Best-in-class DX for typed forms in React. `FormBuilder` (FR-024) is a thin wrapper exposing `<FormField name=…/>` that handles dirty-state warning before navigation, optimistic disable, and ARIA wiring.
- **Alternatives rejected**: Formik (slower runtime, less typed), `react-final-form` (less active).

## R7. Notification SSE consumption — `eventsource-parser`

- **Decision**: `eventsource-parser` ^3 in a small wrapper under `lib/notifications/sse-client.ts` consuming `/api/notifications/sse` (the route handler proxies the spec 023 SSE upstream). Client Component `<BellMenu>` mounts the listener once per shell, stores deltas in `react-query` cache, and triggers re-renders.
- **Rationale**: Q5 chose SSE. Browser-native `EventSource` doesn't accept custom headers (no auth header needed since cookies travel automatically — that's the whole point of the cookie strategy). `eventsource-parser` lets us fall back to a `fetch` + reader-loop on platforms where `EventSource` connections through corporate proxies misbehave.
- **Reconnection**: client retries with exponential backoff up to 30 s, then hands control to a polling fallback (60 s) until SSE reconnects.
- **Alternatives rejected**: WebSocket (heavier, needs separate proto), polling everywhere (Q5 rejected).

## R8. Generated API clients — `openapi-typescript`

- **Decision**: `openapi-typescript` (build-time CLI) consumes each `services/backend_api/openapi.<svc>.json` and emits TypeScript type definitions to `lib/api/types/<svc>.ts`. Hand-curated client wrappers under `lib/api/clients/<svc>.ts` use `lib/api/proxy.ts` and import the generated types — that wrapper layer is what feature code calls.
- **Rationale**: `openapi-typescript` is the lightest-weight option that produces clean inferred types. Hand-curated wrappers keep a stable API surface even if the upstream OpenAPI doc reshuffles. CI fails on type drift.
- **Lint**: ESLint rule (`no-restricted-syntax` + custom AST check) blocks `fetch('http…')` / `axios.create({ baseURL: 'http…' })` outside `lib/api/`.
- **Alternatives rejected**: `openapi-fetch` (good but adds a runtime dep), full SDK generation via `openapi-generator-cli` (verbose; the TS variant is heavier than what we need).

## R9. Test stack — Vitest + Testing Library + Playwright + axe

- **Decision**:
  - **Unit / component**: `vitest` ^2 + `@testing-library/react` ^16. Setup file mounts `next-intl` and renders each component in EN-LTR + AR-RTL.
  - **Visual regression**: Storybook ^8 (Webpack 5) hosting every shell primitive in EN-LTR / AR-RTL × dark/light theme; Playwright snapshots with diff threshold tuned per the goldens we ship.
  - **End-to-end**: Playwright ^1.46 — Story 1 (sign-in incl. MFA → shell), Story 2 (audit-log reader). Runs on Chromium / Firefox / WebKit.
  - **Accessibility**: `@axe-core/playwright` runs across every shell page in EN-LTR + AR-RTL — fails on any new violation.
- **Rationale**: SC-003 + SC-008 are mechanically enforceable only via this stack. Storybook also serves as living documentation for downstream specs (016 – 019) that need to know what shared components exist.
- **Alternatives rejected**: Jest (slower than vitest, less ESM-native), Cypress (Playwright is now the broader-coverage choice), Chromatic (commercial; Playwright snapshots are good enough).

## R10. Telemetry adapter

- **Decision**: Same pattern as the customer app. `lib/observability/telemetry.ts` exposes a `TelemetryAdapter` interface; v1 ships `NoopAdapter` + `ConsoleAdapter` (Dev only). Real provider chosen by spec 023 / observability spec.
- **Events emitted in v1** are enumerated in `contracts/client-events.md`. PII guardrails enforced by `tests/unit/observability/pii-guard.test.ts`.
- **Alternatives rejected**: Pull a Mixpanel SDK now (premature provider commitment).

## R11. CI integration

- **Decision**: A new workflow `.github/workflows/admin_web-ci.yml` runs on PRs touching `apps/admin_web/**` or `packages/design_system/**`. Steps:
  1. `pnpm install --frozen-lockfile`
  2. `pnpm lint` (ESLint + custom no-hardcoded-strings + no-ad-hoc-fetch + no-physical-margins)
  3. `pnpm typecheck`
  4. `pnpm test` (vitest)
  5. `pnpm build-storybook` + `pnpm test:visual` (Playwright snapshots)
  6. `pnpm test:a11y` (axe-playwright across key pages)
  7. `pnpm build` (Next.js production build with `output: 'standalone'`)
- The advisory **`impeccable-scan` workflow** already wired in CLAUDE.md targets this app's PRs — Phase 1F spec 029 promotes it to merge-blocking.
- The **Phase 1C-Infra `admin-docker-build.yml`** consumes the standalone build artifacts.
- **Rationale**: Mirrors the backend CI's gate model. Visual + a11y regressions land before they reach `main`.

## R12. Storybook + visual-regression baseline

- **Decision**: Storybook ^8 hosts:
  - Every `components/shell/*` primitive (one story per state: default / loading / empty / error / restricted).
  - Every `components/data-table/*` and `components/form-builder/*` primitive.
  - Audit-reader composites (`AuditFilterPanel`, `AuditEntryDetail`).
  - Each story is rendered in EN-LTR / AR-RTL × light/dark theme.
- Visual baselines under `tests/visual/__snapshots__/` are committed; CI fails on diff. Diff threshold default 0.2 % per snapshot.
- **Rationale**: SC-003 (100% screens render correctly in both locales) is unenforceable by review alone at this scale.

## R13. Bootstrap / first-login UX

- **Decision**:
  - First admin signs in → if MFA required and not yet enrolled, the app routes to `/auth/mfa/enroll` (spec 004 owns the flow; this app renders it).
  - Subsequent sign-ins → straight to landing.
  - Locale resolved from cookie if set, else `Accept-Language`, else EN.
- **Alternatives rejected**: Forced first-login walk-through (admins are operators, not consumers — minimal friction wins).

---

## Open follow-ups for downstream specs

- **Spec 004**: confirm the navigation-manifest endpoint exists and returns role-filtered entries; if not, file `spec-004:gap:nav-manifest`. Same for the user-preferences endpoint backing saved views.
- **Spec 003 audit-read endpoint**: confirm cursor-paginated filter parameters cover actor / resource / market / timeframe / action. If any filter isn't supported server-side, file against spec 003.
- **Spec 023 (notifications)**: the SSE endpoint and the unread-feed endpoint live there. Until 023 ships, the bell consumes the stub feed per the Assumptions section.
- **Spec 029 (launch hardening)**: promotes `impeccable-scan` to merge-blocking on this app's PRs.
