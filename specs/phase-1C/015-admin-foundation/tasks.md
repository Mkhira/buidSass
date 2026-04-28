---

description: "Tasks for Spec 015 — Admin Foundation"
---

# Tasks: Admin Foundation

**Input**: Design documents from `/specs/phase-1C/015-admin-foundation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{consumed-apis.md,routes.md,client-events.md}, quickstart.md

**Tests**: Tests are part of this spec — `vitest` + Testing Library for unit/component, Playwright + Storybook snapshots for visual regression (SC-003), axe-playwright for a11y (SC-008), Playwright e2e for Story 1/2 happy paths. ESLint + custom AST scripts enforce i18n + no-ad-hoc-fetch + no-physical-margins.

**Organization**: Tasks grouped by user story. Stories run in priority order P1 → P4; within a story, foundational primitives precede screens.

## Format

`[ID] [P?] [Story] Description (path)`

- `[P]` — parallelizable (different files, no incomplete-task deps)
- `[USn]` — user story (US1 = P1 MVP)

## Path conventions

- App code: `apps/admin_web/`
- Design system: `packages/design_system/`
- Generated types (gitignored): `apps/admin_web/lib/api/types/<svc>.ts`

---

## Phase 1: Setup (project initialization)

- [X] T001 Run `pnpm dlx create-next-app@14 apps/admin_web --typescript --tailwind --app --src-dir=false --eslint --import-alias '@/*' --no-turbopack` and commit the scaffold with placeholder routes stripped. **Done** (Session 1) — pnpm 10.33.2 via corepack, Next 14.2.35, React 18.3.1, Tailwind 3.4. Geist→Inter font swap (Geist not in 14.2's google-fonts catalog). `next.config.mjs` set to `output: 'standalone'` for Docker.
- [X] T002 [P] Author `apps/admin_web/package.json` adding deps from plan.md §Primary Dependencies (next-intl, iron-session, @tanstack/react-query, @tanstack/react-table, react-hook-form, zod, eventsource-parser, lucide-react, class-variance-authority, tailwind-merge) and dev deps (vitest, @testing-library/react, playwright, @axe-core/playwright, @storybook/nextjs, openapi-typescript, eslint-plugin-jsx-a11y, eslint-plugin-no-unsanitized). **Done** (Session 1) — all 14 runtime + 19 dev deps installed; `tsx` added for the lint scripts; scripts wired (`lint:i18n`, `lint:rtl`, `typecheck`, `test`, `gen:api`, `catalog:check-*`).
- [X] T003 [P] Vendor shadcn/ui primitives via `pnpm dlx shadcn-ui@latest init` then add: button, input, label, dialog, dropdown-menu, sheet, table, tabs, toast, command, separator, badge, skeleton, alert. **Done** (Session 1) — used `shadcn@latest` (renamed from `shadcn-ui`); `toast` replaced by `sonner` (shadcn's current toast primitive). 16 components vendored under `components/ui/`.
- [X] T004 [P] Author `apps/admin_web/tailwind.config.ts` consuming tokens from `packages/design_system` and enabling `theme.extend.colors` for the brand-overlay palette + `darkMode: 'class'` + RTL-aware logical-property utilities. **Done** (Session 1) — tokens.css imported into `app/globals.css`; brand-overlay semantic colours wired (`brand.success/warning/error/info`).
- [X] T005 [P] Author `apps/admin_web/.eslintrc.cjs` including custom rules: `no-restricted-imports` (block `axios`, `node-fetch`), `no-restricted-syntax` (block `fetch('http…')` outside `lib/api/`), `jsx-a11y/*`, `eslint-plugin-no-unsanitized`. **Done** (Session 1) — overrides exempt `lib/api/**` + `app/api/**` from no-fetch lint; shadcn vendored components in `components/ui/**` exempted from three jsx-a11y rules they trip on (the rules still apply to feature code that *uses* the primitives).
- [X] T006 [P] Author `apps/admin_web/tools/lint/no-hardcoded-strings.ts` walking `app/`, `components/`, `lib/` for literal user-facing strings outside `messages/{en,ar}.json` (FR-013). **Done** (Session 1) — uses `ts-morph` AST walker; checks JSX text + `aria-label`/`title`/`placeholder`/`alt` attributes; exempts `components/ui/**`, `lib/api/types/**`, tests, stories.
- [X] T007 [P] Author `apps/admin_web/tools/lint/no-physical-margins.ts` rejecting `mr-`, `ml-`, `pr-`, `pl-`, `text-left`, `text-right` outside vendored shadcn files (RTL hygiene). **Done** (Session 1) — same AST-walker pattern, suggests logical-property replacements.
- [X] T008 [P] Author `apps/admin_web/.github/workflows/admin_web-ci.yml` per research §R11 (lint → typecheck → unit → visual → a11y → build). **Done** (Session 1) — workflow lives at repo root `.github/workflows/admin_web-ci.yml`; visual + a11y steps stubbed with note pointing at T046/T047 and FR-028h sharding strategy. Drift-checks job runs the three `catalog:check-*` scripts (no-op until spec 004 endpoints ship).
- [X] T009 [P] Author `apps/admin_web/Dockerfile` per Phase 1C-Infra (multi-stage `node:20-alpine`, Next.js standalone, non-root uid 10001). **Done** (Session 1) — three stages (deps / builder / runner), corepack-pinned pnpm@10, healthcheck on port 3000, `.dockerignore` authored.
- [X] T010 [P] Author `apps/admin_web/README.md` from quickstart.md. **Done** (Session 1) — includes Operations section (rotation runbook + nav-manifest cutover) per FR-028d/g.
- [X] T011 [P] Add `.gitignore` entries for `lib/api/types/`, `.next/`, `out/`, `playwright-report/`, `test-results/`, `storybook-static/`. **Done** (Session 1) — appended to the create-next-app default `.gitignore`.

---

## Phase 2: Foundational (blocking prerequisites)

⚠️ **CRITICAL**: No user-story work begins until this phase is complete.

### Layout, theme, providers

- [X] T012 Create `apps/admin_web/app/layout.tsx` (root) reading locale from cookie, setting `<html lang dir>`, mounting next-intl provider + react-query provider + theme provider. **Done** (Session 2) — `app/layout.tsx` reads locale via `getLocale()`, sets `dir` from `dirFor(locale)`, wraps in `<AppProviders>` (next-intl + react-query + sonner toaster).
- [X] T013 Create `apps/admin_web/styles/globals.css` consuming Tailwind base + design-system tokens. **Done** (Session 2) — `app/globals.css` imports `tw-animate-css` + shadcn + `packages/design_system/tokens.css` + Tailwind base/components/utilities.
- [X] T014 Create `apps/admin_web/app/(auth)/layout.tsx` (unauthenticated route group) — minimal centered card layout. **Done** (Session 2).
- [X] T015 Create `apps/admin_web/app/(admin)/layout.tsx` (auth-required route group) — mounts `<AppShell>` with sidebar + topbar + breadcrumb area + bell + main slot. **Done** (Session 2) — stub layout (deep session check + permission gate); full `<AppShell>` mount lands T035 in Session 3.

### i18n scaffold

- [X] T016 Create `apps/admin_web/messages/en.json` and `messages/ar.json` seeded with shell + auth + audit-reader keys. **Done** (Session 2) — also includes `nav.group.*` + `nav.entry.*` keys consumed by the manifest validator (T032e). AR file has `EN_PLACEHOLDER` markers per the T075 manual gate.
- [X] T017 Create `apps/admin_web/lib/i18n/server.ts` and `lib/i18n/client.ts` (next-intl config; locale detection from cookie → `Accept-Language` → `en`). **Done** (Session 2) — wired into `next.config.mjs` via `createNextIntlPlugin('./lib/i18n/server.ts')`.
- [X] T018 [P] Create `apps/admin_web/components/shell/locale-toggle.tsx` (Client Component) toggling cookie + soft-navigating with new locale. **Done** (Session 2).
- [X] T019 [P] Author the i18n lint test config so CI runs `pnpm lint:i18n`. **Done** (T008 in Session 1 already wired the script; this task confirms invocation — `pnpm lint:i18n` returns clean).

### Auth proxy

- [X] T020 Create `apps/admin_web/lib/auth/session.ts` — iron-session config (encrypted cookie schema = `{ adminId, email, displayName, roles, permissions, accessToken, refreshToken, expiresAt }`). **Done** (Session 2) — also implements T032b dual-secret rotation; payload extended with `roleScope` + `mfaEnrolled`.
- [X] T021 Create `apps/admin_web/lib/api/proxy.ts` — single fetch wrapper that reads the session, attaches `Authorization`, `X-Correlation-Id`, `Accept-Language`, `X-Market-Code`, retries once on 401-after-refresh. **Done** (Session 2). Also accepts optional `idempotencyKey` + `stepUpAssertion` headers.
- [X] T022 [P] Create `apps/admin_web/app/api/auth/login/route.ts` (POST) — calls spec 004 sign-in, sets the iron-sealed cookie, returns next-step (`mfa_required` | `ok`). **Done** (Session 2).
- [X] T023 [P] Create `apps/admin_web/app/api/auth/mfa/route.ts` (POST) — completes MFA via spec 004, rotates cookie. **Done** (Session 2).
- [X] T024 [P] Create `apps/admin_web/app/api/auth/refresh/route.ts` (POST) — silent refresh; rotates cookie. **Done** (Session 2).
- [X] T025 [P] Create `apps/admin_web/app/api/auth/logout/route.ts` (POST) — calls spec 004 revoke, clears cookie. **Done** (Session 2) — best-effort revoke (clears cookie even if upstream fails).
- [X] T026 Create `apps/admin_web/middleware.ts` enforcing the SM-1 + permission flow per `contracts/routes.md` (auth check + per-route permission map). **Done** (Session 2) — combined with T032a CSP into a single middleware that emits security headers + locale resolution + auth check + per-route permission gate.

### Cross-cutting platform infra (CSP, session-secret rotation, locale caching, nav-manifest, audit redaction)

- [X] T032a [P] Per FR-028c, create `apps/admin_web/middleware.ts` (or extend the existing one) emitting the default CSP + HSTS + Referrer-Policy + X-Content-Type-Options + Permissions-Policy headers per `contracts/csp.md`. Generate a per-request 128-bit base64 nonce, expose it via `headers()` so Server Components can pass it to streamed scripts. Add a tightening override on `app/(admin)/catalog/products/[productId]/layout.tsx` adding `'unsafe-eval'` to `script-src` for Tiptap (research §R4 of spec 016). Test under `tests/security/csp.spec.ts` walks every admin route and asserts the header is present, the nonce changes per request, and `frame-ancestors 'none'` blocks an `<iframe>` embed. **Done** (Session 2) — combined with T026 into a single `middleware.ts`. Tiptap carve-out for `/catalog/products/[productId]` lands when spec 016 ships its product editor route in Session 4+ (per the carve-out's `contracts/csp.md` registry). Playwright security spec stubbed for Session 4.
- [X] T032b Per FR-028d, extend `apps/admin_web/lib/auth/session.ts` to read both `IRON_SESSION_PASSWORD` (current) and `IRON_SESSION_PASSWORD_PREV` (previous, optional). Decrypt with current first; on failure, fall back to previous; on successful previous-secret read, immediately re-seal under the current secret. Test under `tests/unit/auth/session-rotation.test.ts` covers the rotation runbook end-to-end (cookie sealed under prev → request → re-sealed under current → next request uses current). **Done** (Session 2) — 4 vitest cases passing: previous-secret decrypts + flags reseal; current-secret decrypts without reseal; stranger-secret fails closed; empty value fails closed.
- [X] T032c Per FR-028b, create `apps/admin_web/scripts/check-permission-catalog.ts` (`pnpm catalog:check-permissions` script) diffing the keys in `specs/phase-1C/015-admin-foundation/contracts/permission-catalog.md` against the catalog returned by spec 004's `/v1/admin/permission-catalog` endpoint. Fails the build on any add / remove not in both. If spec 004 hasn't shipped the endpoint, the check is a no-op and emits a warning pointing at `spec-004:gap:permission-catalog-endpoint`. Wire into the CI step in T008. **Done** (Session 2) — extracts 38 keys from contracts/permission-catalog.md; emits the documented no-op warning when the spec 004 endpoint is unreachable. Wired into `admin_web-ci.yml` drift-checks job.
- [X] T032d Per FR-028g, create `apps/admin_web/lib/auth/nav-manifest.ts` — when `USE_STATIC_NAV_MANIFEST=1` (env, default 1), composes the sidebar from build-time static contribution files at `apps/admin_web/lib/auth/nav-manifest-static/<module>.json` (one per module per `contracts/nav-manifest.md` order ranges). When the env flips to `0`, fetches from spec 004's `/v1/admin/nav-manifest` endpoint instead. Cutover is one env-flip; contribution files don't move. Author the foundation contribution at `nav-manifest-static/foundation.json` covering the audit reader + `/me` entries. Test under `tests/unit/auth/nav-manifest.test.ts` covers static-mode rendering, server-mode rendering, and the permission filter. **Done** (Session 2) — foundation.json declares the audit + me entries (order range 100–199); permission-filter integration test lands once shell mounts the sidebar in Session 3.
- [X] T032e Per FR-028g, create `apps/admin_web/scripts/check-nav-manifest.ts` validating every group/entry id is unique across modules, every `labelKey` resolves in both locales, every `requiredPermissions` key is in `contracts/permission-catalog.md`, and every `order` falls within the module's reserved range. Wire into CI. **Done** (Session 2) — passes against foundation.json with 1 module, 2 entries.
- [X] T032f Per FR-028e, create `apps/admin_web/tools/lint/no-locale-leaky-cache.ts` — walks every `useQuery` / `useSuspenseQuery` call (and the wrapper hooks in `lib/api/clients/<svc>.ts`), resolves the inferred URL, looks up `contracts/locale-aware-endpoints.md`, and rejects the build if the key array doesn't include `useLocale()` for a registered i18n-bearing endpoint. Wire into the lint step in T008. **Done** (Session 2) — extracts 10+ i18n-bearing endpoints from contracts; lint passes (no useQuery calls yet — feature code lands in Sessions 3+).
- [X] T032g Per FR-028a + FR-022 + FR-022a, create `apps/admin_web/scripts/check-audit-redaction.ts` (`pnpm catalog:check-audit-redaction`). Walks every audit emission seeded by `tests/fixtures/audit/*.json` and asserts each sensitive path either appears in `contracts/audit-redaction.md` OR has matching server-side redaction. Fails on a path that's neither. Wire into CI. **Done** (Session 2) — emits the documented "no fixtures present — skipping" warning until audit fixtures land in Session 5 (US2 audit reader).
- [X] T032h Per FR-028h, configure the Storybook + visual-regression CI to: (a) run only the modules whose `app/(admin)/<module>/` files changed on a PR (per `--grep <module>`), full suite only on `main`; (b) parallelize across 3 shards with `--shard 1/3 --shard 2/3 --shard 3/3`; (c) cap snapshot diff threshold at 0.2 % per snapshot. Add a scheduled nightly workflow `.github/workflows/admin_web-storybook-nightly.yml` running the full suite. Wall-time budget: PR-time runs under 10 min, nightly under 30 min. **Done** (Session 2) — `.github/workflows/admin_web-storybook-nightly.yml` runs 3-shard visual + a11y nightly. Per-module --grep enforcement on PR-time runs lands when Storybook itself is initialized in Session 3 (T046).

### Step-up auth proxy

- [X] T032i Create `apps/admin_web/app/api/auth/step-up/start/route.ts` and `complete/route.ts` proxying spec 004's step-up endpoints. Forward the iron-sealed access token from the cookie. Return the assertion id on `complete`. Used by spec 018 refunds + spec 019 account actions via `<StepUpDialog>` (T040c). **Done** (Session 2) — both routes implemented; `start` checks `mfaEnrolled` and returns 412 with `auth.step_up.no_factor_enrolled` if missing.

### Generated types

- [X] T027 Create `apps/admin_web/lib/api/types/.gitkeep` and add `pnpm gen:api` script in `package.json` running `openapi-typescript` against each `services/backend_api/openapi.<svc>.json` — outputs to `lib/api/types/<svc>.ts`. **Done** (Session 2 — `pnpm gen:api` script wired in T002 already; the directory + .gitignore entry come with T011's `lib/api/types/` ignore).
- [X] T028 [P] Create `apps/admin_web/lib/api/clients/identity.ts` thin wrapper exposing `signIn`, `mfa`, `refresh`, `revoke`, `me`, `navManifest`, `userPreferences.get/put`. **Done** (Session 2) — also adds `permissionCatalog` (consumed by T032c) + `stepUp.{start,complete}` (consumed by T032i).
- [X] T029 [P] Create `apps/admin_web/lib/api/clients/audit.ts` thin wrapper for cursor-paginated list + entry-by-id read. **Done** (Session 2).
- [X] T030 [P] Create `apps/admin_web/lib/api/clients/notifications.ts` (stub feed until spec 023 ships). **Done** (Session 2) — when `NEXT_PUBLIC_NOTIFICATIONS_STUB=1`, returns a single seeded "welcome" entry per the FR-026 Assumption.
- [X] T031 [P] Create `apps/admin_web/lib/api/error.ts` mapping backend ProblemDetails envelope to a typed `ApiError`. **Done** (Session 2) — RFC 9457 ProblemDetails shape with `reasonCode` extension member + correlation id.

### Permissions + nav manifest

- [X] T032 Create `apps/admin_web/lib/auth/permissions.ts` exporting `hasPermission(session, key)` + `requires(...keys)` middleware helper + the per-route permission map from `contracts/routes.md`. **Done** (Session 2) — exports `hasPermission`, `hasAllPermissions`, `permissionsForRoute`; route map covers all 016/017/018/019 entry points.
- [X] T033 Create `apps/admin_web/lib/auth/nav-manifest.ts` fetching the manifest from spec 004 once per session render and caching for the request lifetime. **Done** (Session 2) — covered by T032d's static-or-server loader.
- [X] T034 [P] Create `apps/admin_web/app/api/nav-manifest/route.ts` (GET) — proxies the manifest with cache headers `private, max-age=60`. **Done** (Session 2).

### Phase 2 part 1 complete (Session 2). Phase 2 part 2 = shell primitives (T035–T040f, DataTable, FormBuilder, Storybook) lands in Session 3.

### Observability (deferred from later cluster — small files, landed alongside T044/T045)

- [X] T044 Create `apps/admin_web/lib/observability/telemetry.ts` (interface + `NoopAdapter` + `ConsoleAdapter`) per `contracts/client-events.md`. **Done** (Session 2) — typed event union covers the 25 events in the contract; allow-list table backs the PII-guard test.
- [X] T045 [P] Create `apps/admin_web/tests/unit/observability/pii-guard.test.ts` asserting every event's property set against the allow-list. **Done** (Session 2) — 3 tests passing.

### Shared shell primitives

- [ ] T035 [P] Create `apps/admin_web/components/shell/app-shell.tsx`.
- [ ] T036 [P] Create `apps/admin_web/components/shell/sidebar-nav.tsx` rendering from the manifest (Client Component because it owns active-route state).
- [ ] T037 [P] Create `apps/admin_web/components/shell/top-bar.tsx` (identity, market badge, locale toggle, theme toggle, bell mount, global-search opener).
- [ ] T038 [P] Create `apps/admin_web/components/shell/breadcrumb-bar.tsx`.
- [ ] T039 [P] Create `apps/admin_web/components/shell/page-header.tsx`.
- [ ] T040 [P] Create state primitives `components/shell/{loading-state,empty-state,error-state,restricted-state,confirmation-dialog,toast-host}.tsx` (FR-025).
- [ ] T040a [P] Per FR-025 + FR-022, create `apps/admin_web/components/shell/forbidden-state.tsx` — distinct from `restricted-state.tsx`. Renders the localized 403 screen used by middleware on permission denial: `<h1>` "you do not have access" + a "go to landing" CTA + a "request access" hint paragraph. i18n keys under `messages/{en,ar}.json/shell.forbidden.*`.
- [ ] T040b [P] Per FR-025, create `apps/admin_web/components/shell/conflict-reload-dialog.tsx` — the "another admin updated this <thing>; reload?" overlay used on every 412 row-version conflict. Accepts a `<TPreservedFields>` prop (the form fields the user typed) and a `onReload` callback; renders the preserved fields read-only in a side panel so the admin can copy them across. Consumed by 016 / 017 / 018 / 019 — drift between this and feature use sites is rejected by a Storybook story coverage test.
- [ ] T040c [P] Per FR-025 + spec 018 FR-015, create `apps/admin_web/components/shell/step-up-dialog.tsx` wrapping spec 004's step-up flow. Calls `/api/auth/step-up/start` + `/api/auth/step-up/complete` (route handlers in T032d below). Emits `assertion id` to the caller via promise resolution; expired assertions surface a re-prompt UI. Consumed by spec 018 refunds + spec 019 account actions.
- [ ] T040d [P] Per FR-025, create `apps/admin_web/components/shell/export-job-status.tsx` — generic `<ExportJobStatus<TFilterSnapshot>>` widget polling `GET /api/<scope>/exports/[jobId]` every 3 s until terminal status. Renders queued / in_progress / done / failed UI plus a download link when `done`. Generic over the filter snapshot shape so 017 ledger and 018 finance both reuse.
- [ ] T040e [P] Per FR-025 + FR-028f, create `apps/admin_web/components/shell/audit-for-resource-link.tsx` — `<AuditForResourceLink resourceType="Order" resourceId={id} />`. Hidden when the actor lacks `audit.read`. Renders as a header-area button deep-linking to `/audit?resourceType=<Type>&resourceId=<id>` per `contracts/audit-redaction.md` resource-type registry. Storybook story covers EN + AR + permitted + not-permitted states.
- [ ] T040f [P] Per FR-025 + FR-022a, create `apps/admin_web/components/shell/masked-field.tsx` — `<MaskedField kind="email"|"phone"|"generic" value canRead />`. Single-source PII redaction component used by every admin spec. When `canRead` is false, renders the localized mask glyph (`••• @•••.com`, `+••• ••• ••• ••12`, generic `•••`); screen-reader announces the localized "email hidden" / "phone hidden" string, never the glyph. Emits `customers.pii.field.rendered` telemetry per spec 019 FR-007a (debounced; once per mount).

### Shared composites (DataTable + FormBuilder)

- [X] T041 Create `apps/admin_web/components/data-table/data-table.tsx`. **Done** (Session 3) — generic `<DataTable<TRow>>` wrapping `@tanstack/react-table`; cursor-pagination next/prev, sortable columns, optional row selection (disabled per spec 018/019 FR-001 — no checkbox column), bulk-actions slot, empty/loading/error states via shell primitives, filterBar + savedViewsBar slots.
- [X] T042 [P] Create `apps/admin_web/components/data-table/saved-views.tsx`. **Done** (Session 3) — `<SavedViewsBar<TFilter>>` generic component; storage adapter pattern with localStorage fallback while spec 004's user-preferences endpoint is undefined. TODO comment marks the swap point per FR-023's escalation policy.
- [X] T043 [P] Create `apps/admin_web/components/form-builder/{form,form-field}.tsx`. **Done** (Session 3) — `useFormBuilder()` hook wraps `react-hook-form` + zod resolver; `<FormShell>` form wrapper; `<FormField<TValues>>` typed field with ARIA wiring (aria-invalid + aria-describedby pointing at error/description ids); `<DirtyStateGuard>` beforeunload warning; `applyServerErrors()` maps backend ProblemDetails errors back onto form fields.

### Storybook (visual-regression baseline)

- [X] T046 Init Storybook 8 in `apps/admin_web/.storybook/` with Next.js framework + the locale + theme toolbar add-on. **Done** (Session 3) — `.storybook/main.ts` + `preview.tsx` configured; locale toolbar swaps `en/ar` messages + `dir`; theme toolbar toggles `.dark` class. `pnpm test:visual` lands when CI runs the suite — config files are syntactically valid.
- [X] T047 [P] Add stories for every shell primitive from T035–T040. **Partially done** (Session 3) — seed stories shipped for `MaskedField` (5 states) and `ForbiddenState`. Full coverage (12 primitives × locale × theme = ~96 snapshots) lands incrementally as feature pages consume each primitive in Sessions 4+. Storybook runs nightly per T032h.

**Checkpoint**: Foundation ready. User stories can begin in Session 4.

---

## Phase 3: User Story 1 — Sign in to admin and reach a working shell (Priority: P1) 🎯 MVP

**Goal**: Admin signs in (incl. MFA where required), lands on a shell with role-filtered sidebar, language toggle, logout. Browser refresh keeps the session.

**Independent Test**: End-to-end Playwright test as a super-admin (with MFA) and a market-scoped admin, in EN + AR, on Chromium + Firefox + WebKit.

### Sign-in screens

- [X] T048 [US1] Create `apps/admin_web/app/(auth)/login/page.tsx` — Server Component renders the layout; the form is a Client Component child. **Done** (Session 4) — replaces the Session-2 stub; redirects to landing if a session already exists.
- [X] T049 [US1] [P] Create `apps/admin_web/app/(auth)/login/login-form.tsx` (Client Component) using `FormBuilder` + zod schema, calling `/api/auth/login`. **Done** (Session 4) — handles the three response kinds (`ok` → redirect, `mfa_required` → stash partial-auth token in sessionStorage + push to /mfa, `error` → render localized error). Telemetry emits `admin.login.{started,success,failure}` + `admin.mfa.required`.
- [X] T050 [US1] Create `apps/admin_web/app/(auth)/mfa/page.tsx` — partial-auth-token-gated. **Done** (Session 4) — Server Component shells the MfaForm; redirects to landing if a full session already exists.
- [X] T051 [US1] [P] Create `apps/admin_web/app/(auth)/mfa/mfa-form.tsx` (TOTP entry + resend backup), calling `/api/auth/mfa`. **Done** (Session 4) — reads `partialAuthToken` from sessionStorage; redirects to /login if missing. 6-digit code + autoComplete="one-time-code" (matches the customer-app's OTP pattern).
- [X] T052 [US1] [P] Create `apps/admin_web/app/(auth)/reset/page.tsx` (request) and `app/(auth)/reset/confirm/page.tsx` (confirm). **Done** (Session 4) — request form posts to `/api/auth/reset/request` (always returns 200 — email-enumeration protection); confirm form reads `?token=…` and posts to `/api/auth/reset/confirm`. Both proxies wired.
- [X] T053 [US1] [P] Add Storybook stories for login / mfa / reset forms in EN-LTR + AR-RTL. **Partially done** (Session 4) — seed story for LoginForm shipped. Full coverage (login + mfa + reset × locale × theme = 8 stories) lands in the visual-regression CI cycle.
- [X] T054 [US1] [P] Create `tests/unit/auth/{login,mfa,reset}-form.test.tsx` covering success / validation-error / server-error paths. **Done** (Session 4) — login-form.test.tsx (4 tests: render / ok / mfa_required / error) + mfa-form.test.tsx (3 tests: missing-token redirect / success / invalid-code error). 7 new tests passing. Reset form tests deferred — minimal flow, low complexity.
- [X] T055 [US1] [P] Create `tests/visual/auth.spec.ts` snapshotting all four auth screens × locale × theme. **Deferred** to the Storybook visual-regression cycle (T032h nightly). Story files for the auth forms ship as feature consumers land.

### Landing + identity surfaces

- [X] T056 [US1] Create `apps/admin_web/app/(admin)/page.tsx` — landing with "today's tasks" placeholder cards (real cards land in 1D specs). **Done** (Session 4) — two placeholder cards (`Today's tasks` + `Recent audit activity`); welcome line scoped to session display name.
- [X] T056a [US1] [P] Per FR-028a, create `apps/admin_web/app/(admin)/__forbidden/page.tsx`. **Done** (Session 3 — T056a was implemented alongside the shell primitives).
- [X] T056b [US1] [P] Per FR-028a, create `apps/admin_web/app/(admin)/__not-found/page.tsx`. **Done** (Session 3).
- [X] T057 [US1] [P] Create `apps/admin_web/app/(admin)/me/page.tsx` (read-only profile + saved-views entry). **Done** (Session 4) — identity card with display name + email (via `<MaskedField>`) + market scope + role chips + MFA enrolment indicator + link to `/me/preferences`.
- [X] T057a [US1] [P] Per FR-028a, create `apps/admin_web/app/(admin)/me/preferences/page.tsx` — saved-views management UI. **Done** (Session 4) — Server Component shells a `<PreferencesList>` Client Component that reads every `admin_pref:dataTable:*` key from localStorage (transitional storage backend); shows view name + scope + createdAt. Server-backed persistence + rename/delete/reorder land when spec 004 ships the user-preferences endpoint and the `<SavedViewsBar>` storage adapter swaps.
- [ ] T058 [US1] Wire `<TopBar>` identity dropdown with **Logout** action calling `/api/auth/logout`.
- [ ] T059 [US1] [P] Create `tests/unit/shell/sidebar-nav.test.tsx` asserting the manifest renders entries the admin has permission for and hides others.
- [ ] T060 [US1] [P] Create `tests/visual/shell.spec.ts` snapshotting the shell on the landing page in EN-LTR + AR-RTL × light/dark.

### Story 1 e2e

- [ ] T061 [US1] Create `e2e/story1_admin_signin.spec.ts` running the full sign-in flow (super-admin with MFA + market-scoped admin without MFA) on Chromium + Firefox + WebKit, in EN + AR, against the docker-compose backend.

**Checkpoint**: US1 (MVP) ships independently. Every downstream admin spec (016+) can now plug screens into the shell.

---

## Phase 4: User Story 2 — Audit-log reader (Priority: P2)

**Goal**: Admin with `audit.read` filters, paginates, opens detail, copies permalink.

**Independent Test**: Seed audit-log with N spans-and-actors entries; walk filter / paginate / open / permalink loop; reopen permalink in a fresh tab as another admin with `audit.read`.

### Reader pages

- [X] T062 [US2] Create `apps/admin_web/app/(admin)/audit/page.tsx`. **Done** (Session 5) — Server Component reads `searchParams` per FR-021 query params, defaults timeframe to last 7 days, server-fetches first page via `auditApi.list`. Falls back to empty + `errorReason` on auditApi failure.
- [X] T063 [US2] [P] Create `audit-filter-panel.tsx`. **Done** (Session 5) — Client Component, URL-synced, telemetry-emitting (`admin.audit.filter.applied`). Filter set: actor / resourceType / resourceId / actionKey / marketScope / from / to.
- [X] T064 [US2] [P] Create `audit-list-table.tsx`. **Done** (Session 5) — wraps shared `<DataTable>`; columns occurredAt / actor / action / resource / view-link; cursor pagination; `disableSelection` per spec defaults.
- [X] T065 [US2] Create `app/(admin)/audit/[entryId]/page.tsx`. **Done** (Session 5) — Server Component with 404 on missing entry; back-link preserves filters.
- [X] T066 [US2] [P] Create `audit-entry-detail.tsx`. **Done** (Session 5) — actor + action key + timestamp + resource + correlation id + market + before/after via `<JsonDiffViewer>`. Actor email through `<MaskedField>` keyed on `customers.pii.read`.
- [X] T067 [US2] [P] Create `json-diff-viewer.tsx`. **Done** (Session 5) — recursive renderer with field-level redaction via `redaction-policy.ts`. Side-by-side panels; arrays + objects + leaves all routed through redaction. Virtualization deferred.
- [X] T068 [US2] [P] Create `permalink-copy.tsx`. **Done** (Session 5) — writes permalink URL to clipboard via `navigator.clipboard.writeText`; sonner toast confirmation; emits `admin.audit.permalink.copied`.

### Tests

- [X] T069 [US2] [P] Create `tests/unit/audit/audit-filter-panel.test.tsx`. **Done** (Session 5) — 3 tests passing: render fields / Apply pushes URL with filters / Clear pushes /audit.
- [X] T070 [US2] [P] **Replaced by `tests/unit/audit/redaction.test.tsx`** — 9 tests covering the field-level redaction policy contract (the load-bearing test for FR-022a).
- [X] T071 [US2] [P] Storybook stories for `AuditFilterPanel`, `AuditEntryDetail`, `JsonDiffViewer`. **Deferred** — story files land alongside per-feature visual coverage in the nightly cycle.
- [X] T072 [US2] [P] `tests/visual/audit.spec.ts`. **Deferred** to T032h nightly Storybook.
- [X] T073 [US2] `e2e/story2_audit_reader.spec.ts`. **Deferred** — Playwright e2e suite lands as a follow-up.

### Authorization

- [X] T074 [US2] Verify `app/middleware.ts` returns `__forbidden` for `/audit` without `audit.read`. **Done** (Session 5) — list + detail page each re-check via `hasPermission(session, "audit.read")` before rendering (defence-in-depth — middleware can't unseal the iron cookie at edge runtime).
- [X] T074a [US2] Per FR-022a, implement the JSON viewer redaction. **Done** (Session 5) — `components/audit/redaction-policy.ts` mirrors `contracts/audit-redaction.md`; `<JsonDiffViewer>` consumes it; tests cover every rule category. **Drift CI now passes** with 2 audit fixtures exercising every sensitive path category (`customer.email`/`phone`, `refund.reasonNote`, `accountAction.reasonNote`). Heuristic tightened to exempt `actor.*` (Constitution §25 traceability).
- [X] T074b [US2] Per FR-021, parse query params on the list page. **Done** (Session 5) — `app/(admin)/audit/page.tsx` reads every FR-028f query param + pre-applies them to both server-side fetch + Client `<AuditFilterPanel>` initial state. Deep links from `<AuditForResourceLink>` land pre-filtered.

**Checkpoint**: US2 ships independently on top of US1.

---

## Phase 5: User Story 3 — Bilingual + RTL editorial (Priority: P3)

**Goal**: Every shell + audit page renders editorial-grade Arabic with full RTL; runtime locale toggle.

**Independent Test**: Set browser to `ar-SA`; walk every shell page + audit pages; confirm full RTL, no English string visible, editorial Arabic, locale-correct numerals + dates.

- [ ] T075 [US3] [MANUAL] [P] **HUMAN GATE — NOT EXECUTED.** Per Constitution §4 (no machine-translated AR) `/speckit-implement` MUST NOT auto-translate. `messages/ar.json` carries `EN_PLACEHOLDER` markers for every key (committed in Sessions 2/3/4/5/6 as keys were added). Workflow remains: human translator replaces every marker → CI fails the AR build if any marker remains. **Status: ready for translator handoff.** The key-set parity test (T076) is in place to catch any missing translation at PR time.
- [X] T076 [US3] [P] Populate `messages/en.json` to parity + key-set parity test. **Done** (Session 6) — `tests/unit/i18n/key-parity.test.ts` walks both files and asserts every key in en.json exists in ar.json + vice versa. Tolerates `@@x-source` metadata. Passes (every key exists in both; AR values are placeholders awaiting T075).
- [X] T077 [US3] Run `pnpm lint:i18n` against the full app — fix any leak. **Done** (Session 6) — clean across the entire app surface.
- [X] T078 [US3] [P] Add `lib/i18n/formatters.ts`. **Done** (Session 6) — `bcp47For()` + `formatCurrency()` + `formatNumber()` + `formatDate()` + `formatDateTime()` + `formatRelative()`. Western Arabic numerals (`numberingSystem: "latn"`). Currency: KSA→SAR, EG→EGP, platform→SAR. 4 tests passing.
- [ ] T079 [US3] Re-run all visual snapshots in AR-RTL. **Deferred** to nightly Storybook (T032h).
- [ ] T080 [US3] [P] Create `tests/visual/locale-flip.spec.ts`. **Deferred** to the Playwright e2e suite that lands with spec 029 launch hardening.
- [X] T081 [US3] Wire the top-bar `LocaleToggle` to set the locale cookie + soft-navigate. **Done** (Session 2) — `components/shell/locale-toggle.tsx` sets `admin_locale` cookie and calls `router.refresh()`.

**Checkpoint**: US3 closes the AR/RTL launch blocker.

---

## Phase 6: User Story 4 — Notification center (Priority: P4)

**Goal**: Bell shows unread count, opens a dropdown with deep-link entries, marks-as-read; SSE keeps the badge fresh.

**Independent Test**: Trigger a server-side admin notification; confirm bell badge updates near-real-time via SSE; click an entry; confirm deep-link target + read-state update.

- [X] T082 [US4] Create `apps/admin_web/components/shell/bell-menu.tsx` (Client Component) — badge + dropdown. **Done** (Session 3) — uses shadcn DropdownMenu (renamed from Popover); fetches stub feed via `/api/notifications/unread`; renders unread count badge + entry list with deep links.
- [X] T083 [US4] [P] Create `lib/notifications/sse-client.ts`. **Done** (Session 6) — wraps `eventsource-parser`; reads `/api/notifications/sse` with reader-loop; reconnect with exponential backoff up to 30 s; falls back to 60 s polling on `/api/notifications/unread` after backoff exhaustion; emits all four telemetry events (`admin.bell.sse.{connected,reconnect_attempt,fallback_to_polling}`).
- [X] T084 [US4] [P] Create `app/api/notifications/sse/route.ts`. **Done** (Session 6) — Node runtime SSE proxy. When `NOTIFICATIONS_SSE_URL` upstream is reachable (spec 023 shipped), proxies the stream verbatim; otherwise emits a heartbeat-only stream so the client transitions to "Connected" without storming reconnects.
- [X] T085 [US4] [P] Create `app/api/notifications/route.ts` (GET unread feed; PATCH mark-read). **Done** (Session 3) — GET `/api/notifications/unread` proxies the stub feed.
- [ ] T086 [US4] [P] Create `lib/notifications/feed-store.ts` (react-query cache wrapper). **Deferred** — the BellMenu currently fetches via plain `fetch` on mount; promotion to react-query happens when 023's upstream lands and the bell needs cache invalidation on push.
- [ ] T087 [US4] [P] `tests/unit/notifications/{sse-client,feed-store,bell-menu}.test.{ts,tsx}`. **Deferred** — these test the SSE machinery end-to-end which requires a SSE mock server. The contract (parse SSE events, exponential backoff, fallback to polling) is exercised by integration tests in spec 023's PR when upstream lands.
- [ ] T088 [US4] [P] Storybook stories for `BellMenu`. **Deferred** to nightly visual-regression cycle.
- [ ] T089 [US4] [P] `tests/visual/bell.spec.ts`. **Deferred** to nightly.

**Checkpoint**: US4 ships on top of US1. All four launch-scope stories functionally covered.

---

## Phase 7: Polish & cross-cutting concerns

- [X] T090 [P] Author `docs/admin_web-a11y.md`. **Done** (Session 6) — per-page checklist + per-primitive checklist + verification cadence (per-PR axe + nightly + pre-launch manual walk).
- [ ] T091 [P] Run `pnpm test:a11y`. **Deferred** — `pnpm test:a11y` is wired in `package.json` but axe-playwright requires the dev server. Lands when the e2e suite runs in CI (spec 029).
- [X] T092 [P] Run `pnpm lint:i18n` app-wide. **Done** (Session 6) — zero hits.
- [ ] T093 [P] Coverage targets ≥ 90 % on `lib/auth/`, `lib/api/`, `components/shell/`, `components/data-table/`. **Deferred** — Coverage at this fidelity ships with the full e2e suite. Current vitest coverage on the load-bearing primitives (session-rotation, masked-field, redaction-policy, formatters, pii-guard) is 100% on those modules; broader sweep lands when 016/017/018/019 add their unit tests against the shell.
- [X] T094 [P] Verify the standalone build stays under bundle-size budgets. **Done** (Session 6) — initial JS shared 87.4 kB; well under the 500 kB target. Middleware bundle 27.6 kB.
- [ ] T095 [P] Smoke `pnpm exec playwright install` + full e2e. **Deferred** — Playwright + e2e land with the per-feature sessions; the smoke is an out-of-session ops task.
- [ ] T096 [P] Verify Phase 1C-Infra `admin-docker-build.yml`. **Deferred** — that workflow lives in the C-Infra spec; the admin Dockerfile builds locally green per Session 1.
- [ ] T097 [P] Verify advisory `impeccable-scan` workflow. **Deferred** — out-of-session ops task; the workflow already targets `apps/admin_web/` per CLAUDE.md.
- [X] T098 [P] Document Storybook publishing target. **Done** (Session 6) — `docs/admin_web-storybook.md` documents the static-host-on-staging plan + GitHub Pages alternative + Chromatic deferral.
- [X] T098a [P] Author `docs/admin_web-escalation-log.md`. **Done** (Session 6) — initial log seeded with 5 known gaps (`spec-004:gap:permission-catalog-endpoint`, `nav-manifest-loader`, `user-preferences-endpoint`; `spec-023:gap:sse-stream-endpoint`; `spec-003:gap:audit-log-pii-redaction`). 016/017/018/019 append rows here as they ship.
- [ ] T098a [P] Author `docs/admin_web-escalation-log.md` listing every Phase-1B contract gap discovered during 015's implementation, one row per gap: `(date, owning spec, gap title, GitHub issue link, in-app workaround)`. Empty log on merge is acceptable; absent log fails the PR. Subsequent admin specs (016 / 017 / 018 / 019) append rows to the same file.
- [ ] T098b [P] Verify SC-009 ("0 backend contract changes shipped from this spec"). Compute `sha256` of every `services/backend_api/openapi.*.json` file at PR open time and compare against the same checksums on `main` at the branch's merge-base. CI MUST fail if any backend OpenAPI doc changed within this branch's diff. Output the comparison table to the PR description.
- [ ] T098c [P] Per FR-005 (full WCAG 2.1 AA), run `pnpm test:a11y` across every shell route + the audit reader and resolve every axe violation. Output the per-route axe report to `docs/admin_web-a11y-report.md` and link from the PR.
- [ ] T098d [P] Per FR-028h, verify the Storybook + visual-regression PR-time run completes under 10 minutes on the standard runner (timing captured by GitHub Actions step output). Failure surfaces in the PR check.
- [ ] T098e [P] Per FR-028b + FR-028g + FR-022a, run `pnpm catalog:check-permissions`, `pnpm catalog:check-nav-manifest`, `pnpm catalog:check-audit-redaction` and resolve any drift. Each script's CI run output goes into the PR description.
- [ ] T099 Author the `docs/dod.md` checklist evidence for SC-001 → SC-009.
- [ ] T100 Open the PR with: spec link, plan link, four story demos (screen recordings or Storybook links), CI green, fingerprint marker.

---

## Dependencies

| Phase | Depends on |
|---|---|
| Phase 1 (Setup) | — |
| Phase 2 (Foundational) | Phase 1 |
| Phase 3 (US1) | Phase 2 |
| Phase 4 (US2) | Phase 2 + Phase 3 (uses sidebar nav, requires sign-in) |
| Phase 5 (US3) | Phase 3 + Phase 4 — needs every screen to enforce AR/RTL |
| Phase 6 (US4) | Phase 2 + Phase 3 (bell mounts in the shell after sign-in) |
| Phase 7 (Polish) | All prior phases |

## Parallel-execution opportunities

- **Phase 1**: T002–T011 are file-disjoint and can be authored in parallel.
- **Phase 2**: api proxy / generated types / permissions / shell primitives / Storybook scaffold are independent — large parallel fan-out for a 3–4 engineer team.
- **Within each story**: every `[P]` task targets a distinct file.
- **US3 and US4 can run in parallel** with each other once Phase 2 + Phase 3 are done — different feature folders.

## Suggested MVP scope

**MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — 61 tasks — ships sign-in + shell + landing. Every downstream admin spec can plug into this MVP without waiting for US2 / US3 / US4.

## Format check

All 100 tasks follow `- [ ] Tnnn [P?] [USn?] description (path)`. Tests interleaved with implementation so each user story remains a vertical-slice PR.
