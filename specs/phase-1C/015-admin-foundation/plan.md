# Implementation Plan: Admin Foundation

**Branch**: `phase-1C-specs` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1C/015-admin-foundation/spec.md`

## Summary

Deliver the **admin web shell** that every subsequent admin module (016 catalog, 017 inventory, 018 orders, 019 customers, 020 verification, 021 B2B, 022 CMS, 023 notifications) consumes — sign-in (incl. MFA), role-filtered sidebar, top bar with identity / market / language / notifications / global search, breadcrumb area, audit-log reader, shared `DataTable` + `FormBuilder` + state primitives, in-app notification bell. Lane B: **UI only** — backend gaps escalate to the owning Phase 1B spec (FR-029).

The app is built as a **Next.js 14+ App Router** project under `apps/admin_web/` using **shadcn/ui** + **Tailwind CSS** (ADR-006). Auth is **httpOnly + SameSite=Strict cookie** with the Next.js server acting as the auth proxy (Q1) — no admin token is ever readable from client-side JavaScript. Render strategy is **Server Components by default**, Client Components only where interactivity demands them (Q2). Per-admin saved views persist on **spec 004's user-preferences endpoint** (Q3). Sidebar is rendered from a **server-driven navigation manifest** + per-route middleware permission check (Q4). The notification bell consumes a **Server-Sent Events** stream owned by spec 023 (Q5).

i18n is first-class — every shell surface and the audit-log reader ship in editorial-grade Arabic with full RTL from launch (Constitution Principle 4 + 20). The advisory `impeccable-scan` workflow already wired in CLAUDE.md targets this app's PRs and is **promoted to merge-blocking in Phase 1F spec 029**.

## Technical Context

**Language/Version**: TypeScript 5.5, Node.js 20 LTS (matches the admin_web Dockerfile recipe in the Phase 1C-Infra plan).

**Primary Dependencies**:

- `next` ^14.2 — App Router; `output: 'standalone'` build for the multi-stage container per Phase 1C-Infra.
- `react` 18 / `react-dom` 18.
- `@radix-ui/react-*` + `shadcn/ui` (vendored components, not a runtime dep) + `tailwindcss` ^3.4 + `class-variance-authority`, `tailwind-merge`, `lucide-react`.
- `next-intl` ^3 — App-Router-compatible localization (AR + EN ARB-equivalent message catalogs) with built-in RTL support.
- `@tanstack/react-query` ^5 — client-side data fetching for Client Components (Server Components fetch directly).
- `@tanstack/react-table` ^8 — column engine under the shared `DataTable` (FR-023).
- `react-hook-form` ^7 + `zod` ^3 — `FormBuilder` primitive (FR-024).
- `iron-session` ^8 — encrypts the cookie payload server-side; rotation on refresh.
- `openapi-typescript` (build-time) generating types from each `services/backend_api/openapi.<svc>.json` into `lib/api/types/<svc>.ts`; runtime fetch wrappers under `lib/api/clients/<svc>.ts`.
- `eventsource-parser` ^3 — SSE consumption for the notification bell (Q5).
- `axe-core` + `@axe-core/playwright` — accessibility regression tests (SC-008).
- `playwright` ^1.46 — end-to-end Story 1 + Story 2 tests across Chromium / Firefox / WebKit.
- `vitest` ^2 + `@testing-library/react` ^16 — component unit tests.
- `eslint` + `@typescript-eslint`, `eslint-plugin-react`, `eslint-plugin-jsx-a11y`, `eslint-plugin-no-unsanitized` — lint rules including a custom rule blocking direct `fetch('http…')` outside `lib/api/`.
- `packages/design_system` (in-repo path dep) — palette tokens shared with the customer app (Principle 7).

**Storage**: No server-side persistence introduced by this spec. Server-side state lives entirely in the cookie (session payload) + spec 004 (user preferences). Client-side: `react-query` cache only — no `localStorage` for tokens (Q1).

**Testing**:

- **Unit / component**: `vitest` + `@testing-library/react` — every Server / Client Component test renders in EN-LTR and AR-RTL.
- **Visual regression**: Playwright + a Storybook-backed snapshot suite (Storybook hosts every shell primitive in EN-LTR / AR-RTL × dark/light theme); CI fails on visual diff. This is the SC-003 enforcement mechanism.
- **End-to-end**: Playwright running Story 1 (sign-in incl. MFA → shell) and Story 2 (audit-log reader filter / detail / permalink) on Chromium / Firefox / WebKit against the docker-compose backend stack.
- **Accessibility**: `@axe-core/playwright` runs against every shell surface and the audit-log reader pages; CI fails on any new WCAG 2.1 AA violation (SC-008).
- **i18n lint**: a `tsx`-AST script in `tools/lint/no-hardcoded-strings.ts` rejects any user-facing string literal outside `messages/{en,ar}.json` (FR-013 enforcement).
- **No-ad-hoc-fetch lint**: ESLint rule blocking direct `fetch('http…')` / `axios` outside `lib/api/` (FR-030 enforcement).

**Target Platform**: Modern desktop browsers (Chrome / Edge / Safari / Firefox — current and previous major). Optimized for ≥ 1280-px-wide viewports per Assumptions; responsive but not optimized for mobile-web. Deployed as a Node 20 container (Phase 1C-Infra).

**Project Type**: Next.js admin web app under the modular monorepo (ADR-001).

**Performance Goals**:

- **SC-001**: cold load → interactive shell ≤ 5 s on broadband.
- Audit-log reader **first page** ≤ 1 s on 1 M-row staging dataset (SC-005).
- 60 fps on table scroll over 100-row pages.

**Constraints**:

- **No backend code in this PR** (FR-029). Any gap escalates to the owning 1B spec.
- **No client-side token access** (Q1). All API calls go through the Next.js auth-proxy route handlers.
- **No hard-coded user-facing strings** outside `messages/{en,ar}.json` (FR-013, lint enforces).
- **No inline `style={{ color: …}}` literals** outside `packages/design_system/`. Design tokens only.
- **No client-side fetch** outside `lib/api/clients/` (FR-030, lint enforces).
- **Mobile breakpoints not optimised** — Assumption documented; admin-on-mobile is a separate later spec if ever.

**Scale/Scope**: ~12 shell-level pages + ~6 audit-reader pages + ~15 shared primitives. 4 prioritized user stories, 31 functional requirements, 9 success criteria, 5 clarifications integrated. Storybook target: ~80 stories covering primitives × locales × themes for the SC-003 visual-regression suite.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Customer browse-without-auth is irrelevant here — every admin route is gated. | PASS (n/a) |
| P4 Arabic / RTL editorial | Every shell surface + audit-reader pages ship AR + EN with full RTL via `next-intl` + `dir` attribute on `<html>`. Lint blocks hard-coded strings; visual-regression goldens block AR-RTL layout regressions. | PASS |
| P5 Market Configuration | The top-bar market badge and the audit-log reader's market filter source from spec 004's role-scope manifest — never hard-coded. | PASS |
| P6 Multi-vendor-ready | The shell makes no single-vendor assumptions. The navigation manifest is data-driven, so adding vendor-scoped admin entries in Phase 2 is a server change with zero shell change. | PASS |
| P7 Branding | Design tokens consumed exclusively from `packages/design_system`. No inline hex literals (lint enforces). Admin-specific density (denser tables, tighter padding) is layered as a Tailwind preset on top of the shared tokens. | PASS |
| P9 B2B | B2B admin workflows are deferred to spec 021. Forward-compatible: the navigation manifest accommodates a B2B group when 021 ships. | PASS |
| P15 Reviews | Review moderation deferred. | PASS |
| P20 Admin Dashboard | This spec **delivers** the dashboard shell, AR + EN, plus the audit-log reader. Future modules (016 – 023) plug into it without re-implementing the shell. | PASS |
| P22 Fixed Tech | Next.js + shadcn/ui per ADR-006. No deviation. | PASS |
| P23 Architecture | Modular monolith on the backend (Lane A); Lane B is a single Next.js app consuming generated TypeScript clients. No micro-frontend split. | PASS |
| P24 State Machines | Three client-side state machines explicit in `data-model.md`: `AdminSession`, `NavigationManifest`, `NotificationFeed`. | PASS |
| P25 Data & Audit | Audit emission is server-side (spec 003); this app is a **read** surface for those emissions. No client-side audit writes. | PASS |
| P27 UX Quality | Every page implements loading / empty / error / restricted states (FR-004). Accessibility enforced via axe + manual keyboard walk; SC-008 captures the WCAG bar. | PASS |
| P28 AI-Build Standard | Spec ships with explicit FRs, acceptance scenarios, edge cases, success criteria, 5 resolved clarifications. | PASS |
| P29 Required Spec Output | Goal / roles / rules / flows / states / data / validation / contracts consumed / edge cases / acceptance / phase / deps — all present. | PASS |
| P30 Phasing | Phase 1C Milestone 5/6. Depends on spec 004 contract merged. No scope creep into 1D. | PASS |
| P31 Constitution Supremacy | No conflicts. | PASS |
| ADR-001 Monorepo | Code lives under `apps/admin_web/` and `packages/design_system/` in the existing monorepo. | PASS |
| ADR-006 Next.js + shadcn/ui | Locked. App Router (Q2). | PASS |
| ADR-010 KSA residency | All API calls hit the Phase 1B backend hosted in Azure Saudi Arabia Central. The Next.js server runs in the same region (Phase 1C-Infra). | PASS |

**No violations.** No entries needed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/phase-1C/015-admin-foundation/
├── plan.md              # This file
├── research.md          # Phase 0 — library/pattern research
├── data-model.md        # Phase 1 — view models + state machines
├── quickstart.md        # Phase 1 — local dev setup
├── contracts/
│   ├── consumed-apis.md      # OpenAPI documents consumed
│   ├── routes.md             # Admin route table + permission requirements
│   └── client-events.md      # Telemetry event vocabulary (PII-safe)
├── checklists/
│   └── requirements.md       # Already created by /speckit-specify
└── tasks.md             # Phase 2 — produced by /speckit-tasks
```

### Source Code (repository root)

```text
apps/admin_web/
├── app/                            # Next.js App Router
│   ├── layout.tsx                  # Root layout — locale + dir attribute + theme
│   ├── (auth)/                     # Auth route group — unguarded
│   │   ├── login/
│   │   ├── mfa/
│   │   └── reset/
│   ├── (admin)/                    # Auth-required route group
│   │   ├── layout.tsx              # Shell — sidebar + topbar + breadcrumb + bell
│   │   ├── page.tsx                # Landing — today's tasks
│   │   ├── audit/
│   │   │   ├── page.tsx
│   │   │   └── [entryId]/page.tsx  # Detail panel
│   │   └── (placeholder routes for 016–019; only the shell + 014/015 routes ship here)
│   ├── api/                        # Next.js Route Handlers — auth proxy
│   │   ├── auth/
│   │   │   ├── login/route.ts
│   │   │   ├── mfa/route.ts
│   │   │   ├── refresh/route.ts
│   │   │   └── logout/route.ts
│   │   ├── audit/route.ts
│   │   ├── nav-manifest/route.ts
│   │   └── notifications/sse/route.ts
│   └── middleware.ts               # Auth-required guard + per-route permission check
├── components/
│   ├── ui/                         # shadcn/ui vendored primitives
│   ├── shell/                      # AppShell, SidebarNav, TopBar, BreadcrumbBar, BellMenu
│   ├── data-table/                 # FR-023 DataTable + saved views + filters
│   ├── form-builder/               # FR-024 typed form primitive + validation
│   └── audit/                      # AuditFilterPanel, AuditEntryDetail, JsonDiffViewer
├── lib/
│   ├── api/
│   │   ├── clients/<svc>.ts        # Generated wrappers per spec 004 / 003 audit / 023 notifications
│   │   ├── types/<svc>.ts          # Generated TS types
│   │   └── proxy.ts                # The single fetch wrapper used by route handlers
│   ├── auth/
│   │   ├── session.ts              # iron-session setup
│   │   ├── permissions.ts          # `requires('audit.read')` helper
│   │   └── nav-manifest.ts
│   ├── i18n/
│   │   ├── server.ts               # next-intl server config
│   │   └── client.ts               # next-intl client provider
│   ├── observability/
│   │   ├── telemetry.ts
│   │   └── pii-guard.ts
│   └── feature-flags/              # Notifications stub flag, etc.
├── messages/
│   ├── en.json
│   └── ar.json
├── stories/                        # Storybook stories (per primitive × locale × theme)
├── tests/
│   ├── unit/                       # vitest + RTL
│   ├── visual/                     # Playwright snapshot config
│   └── a11y/                       # axe-playwright tests
├── e2e/                            # Playwright end-to-end (Story 1, Story 2)
├── tools/lint/                     # no-hardcoded-strings, no-ad-hoc-fetch
├── public/
├── styles/                         # Tailwind config + globals.css
├── tailwind.config.ts              # Reads tokens from packages/design_system
├── next.config.mjs                 # output: 'standalone' for the C-Infra Dockerfile
├── tsconfig.json
├── package.json
└── README.md

packages/design_system/             # Already exists — extended only if needed
└── …
```

**Structure Decision**: One Next.js App Router app under `apps/admin_web/`. Route groups separate auth surfaces (`(auth)`) from admin surfaces (`(admin)`); the latter has a `layout.tsx` that mounts the shell. API route handlers under `app/api/` form the auth-proxy boundary (cookies → tokens → backend). `lib/` holds shared infra; `components/ui/` is shadcn/ui's vendored primitives; feature-folder convention mirrors the customer app's `lib/features/` for mental-model parity.

## Complexity Tracking

> No constitution violations. The following entries document **intentional non-obvious choices**.

| Choice | Why | Simpler alternative rejected because |
|---|---|---|
| Auth proxy via Next.js route handlers (httpOnly cookies) | Q1 — no token visible to client JS, sharply lowers XSS impact. | Memory + httpOnly refresh would still let an XSS read access tokens during the session lifetime. |
| Server Components default + Client Components on demand | Q2 — natural fit with cookie auth, keeps client bundle small. | Pages Router would forfeit streaming, server components, and the cookie-as-source-of-truth flow. |
| Server-driven navigation manifest | Q4 — single source of truth for "what can this admin see"; survives role / permission changes without app redeploy. | Hard-coded sidebar drifts the moment the permission catalog gains an entry. |
| SSE for notification freshness | Q5 — push without WebSocket complexity; survives load balancers; cheap on backend. | Polling adds latency + load with no UX win on a desktop admin surface. |
| Storybook visual-regression suite | SC-003 enforcement — manual review of ~80 primitives × locales × themes is not feasible per PR. | Manual QA misses RTL regressions. |
| Per-admin saved views via spec 004 user-preferences | Q3 — survives device + browser; matches the cross-device admin workflow. | localStorage-only would frustrate admins who switch laptops. |
