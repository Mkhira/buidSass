# Feature Specification: Admin Foundation

**Feature Branch**: `phase-1C-specs`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Spec 015 admin-foundation (Phase 1C) — Next.js + shadcn/ui admin web shell. Per docs/implementation-plan.md §Phase 1C item 015: depends on spec 004 contract merged to main; consumes Lane A backend (no inline backend changes — escalate gaps to the owning 1B spec). Exit criteria: Next.js + shadcn/ui shell; admin auth (incl. MFA where required); role-based nav; AR + EN with RTL toggle; audit-log reader (filter by actor / resource / timeframe). Constitution P20 mandates separate admin web app supporting AR + EN. ADR-006 locks Next.js + shadcn/ui."

## Clarifications

### Session 2026-04-27

- Q: Where should admin session tokens live in the browser? → A: httpOnly Same-Site cookie. Session + refresh tokens issued as `httpOnly`, `Secure`, `SameSite=Strict` cookies; access tokens injected server-side by Next.js middleware before forwarding to the .NET backend. The Next.js app acts as a thin auth proxy — no admin token is ever visible to client-side JavaScript.
- Q: Next.js rendering strategy? → A: App Router with Server Components default. Auth check in middleware before any render; Client Components used only where interactivity demands them (DataTable, filter panels, modals). Per ADR-006.
- Q: Where should per-admin saved views persist? → A: Server-side via spec 004 user-preferences endpoint. Survives device + browser. If the endpoint does not yet exist on 004, file a spec-004 issue and use localStorage as a transitional fallback (per FR-029 escalation policy).
- Q: Permission model granularity? → A: Server-driven navigation manifest + per-route permission check. Spec 004 returns the navigation entries the admin's roles permit at session start; each admin route also declares its required permission keys; middleware enforces before render. Two layers — manifest for discoverability, per-route check for direct-URL access — with the server as the single source of truth.
- Q: How fresh is the bell badge? → A: Server-Sent Events while the shell is open. One SSE stream per admin session; server pushes notification deltas as they arrive (with a 30 s heartbeat so the client detects connection drops). Bell badge is near-real-time; no client polling. SSE endpoint is owned by spec 023 (notifications).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in to admin and reach a working shell (Priority: P1)

An admin opens the admin web app, signs in with email + password, completes MFA where their role requires it (super-admin and finance per spec 004), and lands on the admin shell — sidebar with role-filtered navigation entries, top bar with their identity / market / language toggle, breadcrumb area, and a default landing screen (overview / today's tasks). Refreshing the browser keeps them signed in until session expiry. Logout returns them to the sign-in screen.

**Why this priority**: Without a working shell + auth, no other admin spec (016 catalog, 017 inventory, 018 orders, 019 customers, …) can ship its UI. This is the hard P1 dependency for every admin module that follows. Shipping just this story unblocks every downstream Phase 1C/1D admin module.

**Independent Test**: Walk the sign-in flow end-to-end as a super-admin (with MFA) and a market-scoped admin (without MFA), verify the shell renders with a role-filtered sidebar, switch language, log out. Ship-ready when this works on Chrome / Edge / Safari / Firefox at the support matrix and in both AR and EN.

**Acceptance Scenarios**:

1. **Given** a registered super-admin, **When** they enter correct credentials and a valid TOTP code, **Then** they are placed on the admin landing screen with a sidebar listing every navigation entry their roles permit.
2. **Given** a registered market-scoped admin without MFA enrolment, **When** they enter correct credentials, **Then** they are placed on the admin landing screen with a sidebar listing only entries permitted by their role / market scope.
3. **Given** a signed-in admin, **When** they refresh the browser, **Then** the session resumes and the same landing screen renders without re-prompting credentials (until session expiry).
4. **Given** a signed-in admin, **When** they tap **Logout**, **Then** the session is revoked and they are returned to the sign-in screen.
5. **Given** an admin attempts a wrong password / wrong TOTP / disabled account, **When** auth fails, **Then** an editorial-grade error message is shown in the active language with no information leak about which input was wrong.

---

### User Story 2 - Read the audit log to investigate any admin action (Priority: P2)

A super-admin (or any role with `audit.read` permission) opens the audit-log reader, filters by actor (specific admin), resource (entity type or specific id), action, market, and timeframe, paginates through results, opens a single entry to see before/after JSON. They can copy a permalink to that entry to share with a colleague.

**Why this priority**: The platform's compliance + accountability story (Constitution Principle 25) is unenforceable without an admin-readable audit log. Phase 1C ships the reader so we don't ship 016 / 017 / 018 / 019 modules whose audit emissions go to a write-only table. P2 because the modules can technically ship without the reader, but launch readiness can't.

**Independent Test**: Seed the audit-log table with N entries spanning multiple actors, resources, and timestamps. Open the reader, apply each filter category in turn, confirm results are correct, paginate, open an entry, copy permalink, share to a fresh tab, confirm permalink resolves the same entry.

**Acceptance Scenarios**:

1. **Given** an admin with `audit.read` permission, **When** they open the audit-log reader, **Then** the most recent N entries are listed with actor, action, resource type, resource id, timestamp, and a "view" affordance.
2. **Given** the reader is open, **When** they filter by actor, the listed entries narrow to that actor's events only.
3. **Given** the reader is open, **When** they filter by resource type + id, the listed entries narrow to that entity's history only.
4. **Given** the reader is open, **When** they pick a timeframe (last 24 h, last 7 days, last 30 days, custom), the listed entries fall inside that window.
5. **Given** an entry is selected, **When** the detail panel opens, **Then** before-state and after-state JSON are shown side-by-side, the correlation id is shown, and a permalink copies to clipboard with a toast confirmation.
6. **Given** an admin without `audit.read`, **When** they navigate to `/admin/audit`, **Then** they see a 403-style "you do not have access" screen — never a partial render of audit data.

---

### User Story 3 - Operate comfortably in Arabic with full RTL (Priority: P3)

An Arabic-speaking admin toggles the language to Arabic from the top bar (or it's picked up from browser preference on first visit), and every shell surface and reader screen flips to RTL with editorial-grade Arabic copy, localized numerals/dates, and no English fallbacks. Switching back to English is instant.

**Why this priority**: Constitution Principle 4 + Principle 20 — the admin app must support AR + EN for launch. Critical for KSA + EG admin staff but separable from the auth + shell + audit-reader correctness path during early development.

**Independent Test**: Set browser language to `ar-SA`, log in, walk every shell surface (sidebar, top bar, breadcrumbs, global search, audit-log reader, error pages), confirm full RTL, no English string visible, editorial Arabic copy, locale-correct numerals + dates.

**Acceptance Scenarios**:

1. **Given** the browser preference is Arabic, **When** the admin opens the app for the first time, **Then** the sign-in screen renders in Arabic with RTL without any toggle.
2. **Given** the admin is signed in, **When** they toggle language from the top bar, **Then** every visible surface re-renders in the new language and direction without a full page reload (except for the language tag in the URL if such a route segment is used).
3. **Given** the active locale is Arabic, **When** the audit-log reader displays timestamps and amounts, **Then** numerals + dates use locale-correct formatting.
4. **Given** the active locale is Arabic, **When** any error / empty / loading state renders, **Then** the copy is editorial-grade Arabic — never an English fallback or raw error code.

---

### User Story 4 - See and act on in-app admin notifications (Priority: P4)

An admin opens the bell icon in the top bar and sees recent in-app alerts targeted at admins (e.g., "5 returns awaiting approval", "low-stock alert for SKU X", "verification submission pending review"), each with a deep link into the relevant admin screen. They can mark a notification as read; unread count updates in the bell badge.

**Why this priority**: Reduces missed work without being launch-blocking. The same alerts also surface server-side as audit events / events emitted by the owning module — the bell is a UX layer over them. Lower priority because admin staff can polled the relevant module screens manually until this lands.

**Independent Test**: Trigger a server-side admin event (e.g., admin-targeted notification emitted by spec 008 low-stock, spec 011 returns-pending, spec 020 verification-pending). Verify the bell badge increments, the entry shows in the dropdown, deep link resolves, mark-as-read clears the badge counter.

**Acceptance Scenarios**:

1. **Given** an admin with unread notifications, **When** they open the bell dropdown, **Then** the entries list with title + timestamp + deep link, sorted newest first.
2. **Given** a notification entry, **When** the admin clicks it, **Then** they navigate to the deep-link target and the notification is marked read.
3. **Given** an admin with no unread notifications, **When** they open the bell, **Then** they see an empty state — never a stale badge count.

---

### Edge Cases

- Admin's session expires mid-action → transparent redirect to sign-in with a `?continueTo=…` query so the original action page resumes after re-auth.
- MFA-enrolled admin loses their TOTP device → spec 004 owns the recovery flow; this app links to it from the sign-in error state.
- Admin's role permissions change while they're signed in → next API call returns 403 → app re-fetches the user's permission set and re-renders the sidebar within the same session (no forced logout).
- Browser open in two tabs, admin logs out in one → the other tab detects session revocation on its next API call and routes to sign-in.
- Admin filters audit log to a window with millions of entries → pagination + server-side filtering keeps the UI responsive; the reader never tries to fetch all entries.
- Permission missing for a sidebar entry → entry is not rendered (not just disabled) so the admin doesn't see capabilities they can't use.
- Audit entry with extremely large before/after JSON → detail panel virtualises the JSON viewer; copy-permalink still works.
- Locale switched while a long-running query is in flight → the request still completes, the response is rendered in the now-active locale.
- Notification deep-link target requires a permission the admin lost since the notification was emitted → app shows the same 403-style screen as direct navigation.

## Requirements *(mandatory)*

### Functional Requirements

#### Shell, navigation, theming

- **FR-001**: The admin app MUST run as a separate web application from the customer app per Constitution Principle 20.
- **FR-002**: The shell MUST provide: left sidebar with role-filtered navigation, top bar with identity / market / language / notifications, breadcrumb area, primary content area, and a global search affordance in the top bar.
- **FR-003**: The active palette MUST match Constitution Principle 7 plus brand-overlay semantic colours; design tokens MUST come from a single source consumed by the admin app (no inline hex literals in feature code).
- **FR-004**: Every page MUST implement loading, empty, error, success, and (where applicable) restricted-state messaging per Constitution Principle 27.
- **FR-005**: The shell and every page rendered inside it MUST meet **WCAG 2.1 AA** end-to-end — keyboard navigation (tab order, focus rings, skip-to-content link), contrast, ARIA semantics, focus management on dialogs, and screen-reader compatibility. Verified by `axe-playwright` across every shell surface and every feature page (SC-008). Same bar as the customer app per Constitution §27.

#### Auth + RBAC

- **FR-006**: The sign-in screen MUST require email + password; if the admin's role demands MFA per spec 004 (super-admin, finance), the screen MUST progress to a TOTP entry step before completing sign-in.
- **FR-007**: Session + refresh tokens MUST be persisted as `httpOnly`, `Secure`, `SameSite=Strict` cookies set by the Next.js app server after a successful spec 004 sign-in. Access tokens MUST NOT be readable from client-side JavaScript. The Next.js app server MUST act as the auth proxy — every backend call from the browser passes through a Next.js route handler that attaches the access token from the cookie store. Refresh MUST be silent (server-side, before token expiry); only refresh failure surfaces a re-auth prompt.
- **FR-008**: Every admin route MUST be guarded server-side and client-side. Pages MUST NOT render their primary content for an unauthenticated request — the unauthenticated page renders the sign-in screen with `?continueTo=<original>`.
- **FR-009**: Every admin route MUST declare a required permission (or set of permissions). The shell MUST source the sidebar from a server-driven **navigation manifest** returned by spec 004 at session start (filtered to the admin's role set). The shell MUST hide sidebar entries the current admin lacks permission for. Direct navigation to a route the admin lacks permission for MUST return a 403-style screen — Next.js middleware enforces the per-route permission check before render.
- **FR-010**: Permission changes MUST take effect within one API round-trip — the sidebar refreshes when a 403 is returned for an action the user previously had permission for.
- **FR-011**: Logout MUST revoke the session via spec 004 and clear all client-side session material before returning to sign-in.

#### Localization, RTL, market awareness

- **FR-012**: Both Arabic and English MUST be fully supported across every shell surface and the audit-log reader, with full RTL when Arabic is active.
- **FR-013**: All user-visible copy MUST come from the localization layer; no English string may appear in the AR build.
- **FR-014**: Numerals, currency symbols, and dates MUST follow the active market's locale conventions.
- **FR-015**: The market context displayed in the top bar (KSA / EG / "platform" for super-admins) MUST come from the admin's role scope per spec 004 — never hard-coded.
- **FR-016**: Arabic copy MUST be editorial-grade — copy approval gate before launch; machine-translated strings are non-compliant.

#### Audit-log reader

- **FR-017**: The reader MUST list audit-log entries with: actor (admin email or role-scope label), action key, resource type, resource id, timestamp (locale-formatted), correlation id (truncated, hover-expand).
- **FR-018**: The reader MUST support filters: actor, resource type, resource id, action key, market scope, timeframe (last 24 h / last 7 d / last 30 d / custom range). Filters compose (AND).
- **FR-019**: Results MUST be paginated server-side (cursor-based) — the client MUST NOT request more than one page at a time.
- **FR-020**: An entry's detail panel MUST show before-state JSON, after-state JSON, correlation id, actor identity, and a copy-permalink action.
- **FR-021**: The permalink format MUST be `https://admin.<env>.dental-commerce.com/audit/<entryId>?permalink=1&locale=<en|ar>` (canonicalized in `contracts/routes.md`). When opened by another admin with `audit.read` permission, it MUST resolve to the same entry's detail panel without intermediate navigation. The `?permalink=1` flag triggers a confirmation toast on landing; `&locale=` is optional and the receiving admin's cookie wins when present.
- **FR-022**: Direct navigation to the audit-log reader without `audit.read` permission MUST yield the `ForbiddenState` screen (FR-009 applies; primitive defined in FR-025).
- **FR-022a**: The audit-log reader MUST redact JSON paths in the before/after viewer when the actor lacks the matching field-level permission. The set of sensitive paths and the permission required to view each unredacted lives in `contracts/audit-redaction.md` (created with this FR). At minimum, `customer.email`, `customer.phone`, address fields, and free-text reason notes are listed there. Redacted paths render via the shared `<MaskedField>` primitive (FR-025). Server-side enforcement (the audit-read endpoint pre-redacts the JSON for the calling admin's permission set) is the source of truth; the client mask is defence-in-depth. An admin with `audit.read` but without `customers.pii.read` MUST NOT see customer email / phone via this reader. Every emitting spec MUST append its sensitive paths to the registry; CI fails on an unregistered path that lands in a seeded fixture.

#### Shared components (used by spec 015 and consumed by 016 – 019)

- **FR-023**: A shared `DataTable` component MUST support: server-side pagination, column-level filters, sortable columns, saved views (per-admin, persisted server-side via spec 004 user-preferences endpoint), row selection, bulk actions, empty / loading / error states.
- **FR-024**: A shared `FormBuilder` primitive MUST support: typed fields, server-side validation surfacing, optimistic disable on submit, dirty-state warning before navigation.
- **FR-025**: Shared shell primitives MUST cover the following — every admin spec consumes them, none reimplements them, and they all live under `apps/admin_web/components/shell/`:
  - `AppShell`, `SidebarNav`, `TopBar`, `BreadcrumbBar`, `PageHeader` — layout chrome.
  - `EmptyState`, `ErrorState`, `LoadingState` — page-state stubs (Constitution Principle 27).
  - `RestrictedState` — the restricted-product / restricted-content state for Principle 27 (e.g., a customer-app surface rendered inside the admin for preview).
  - `ForbiddenState` — the 403 / `/__forbidden` route screen. **Distinct from `RestrictedState`**: renders a localized "you do not have access" headline + a "go to landing" CTA + a "request access" hint copy. Used by middleware on direct-nav permission denial.
  - `Confirmation` dialog and `Toast` host.
  - `StepUpDialog` — wraps spec 004's step-up MFA challenge. Consumed by spec 018 refunds and spec 019 account actions.
  - `ConflictReloadDialog` — the "another admin updated this <thing>; reload?" overlay rendered on every 412 row-version conflict. Preserves typed local edits in a side panel. Consumed by 016 / 017 / 018 / 019.
  - `ExportJobStatus<TFilterSnapshot>` — the queued / in_progress / done / failed status widget for any async export job. Consumed by 017 ledger export and 018 finance export.
  - `<AuditForResourceLink resourceType resourceId />` — the "view audit log" header affordance per FR-028f. Hidden when actor lacks `audit.read`.
  - `<MaskedField kind="email"|"phone"|"generic" value canRead />` — the single PII redaction component used by every admin spec. Promoted from spec 019 into the shell so spec 018's customer card, the audit-reader JSON viewer (FR-022a), and any future module reuse the same masking. The component MUST emit the `customers.pii.field.rendered` telemetry event per spec 019 FR-007a (debounced; once per mount; no PII-bearing properties).

#### Notification center

- **FR-026**: The bell affordance in the top bar MUST display an unread count badge. Clicking it MUST open a dropdown listing the most recent N notifications with title, timestamp, deep link. The badge MUST update in near-real-time via a single Server-Sent Events stream opened while the shell is mounted (with a 30 s server-side heartbeat so the client detects disconnects); polling fallback only kicks in if SSE is unavailable.
- **FR-027**: Clicking a notification entry MUST navigate to its deep-link target and mark it read; the badge counter MUST decrement immediately.
- **FR-028**: Notifications data MUST come from a server-side endpoint owned by spec 023 (notifications). Until 023 ships, the bell consumes a stub feed — see Assumptions.

#### Identity, errors, and shared infrastructure routes

- **FR-028a**: The shell MUST register the following infrastructure routes at the top level of the `(admin)` group: `/__forbidden` (renders `ForbiddenState`; reached by middleware on a permission-denied direct nav), `/__not-found` (unknown route inside `(admin)`), `/me` (read-only profile + locale toggle target), `/me/preferences` (per-admin saved-views management UI; reads / writes via spec 004's user-preferences endpoint per Q3, with localStorage transitional fallback per the **Saved views storage** Assumption below). Concrete route table lives in `contracts/routes.md`.
- **FR-028b**: The single registry of permission keys consumed across admin specs (015 – 019) MUST be maintained at `contracts/permission-catalog.md` (created with this spec). Each downstream module appends its keys via PR rather than re-declaring them locally; spec 004's permission catalog mirrors this file. Drift between the two is caught by a CI check that diffs the registry against spec 004's emitted catalog.

#### Cross-cutting platform policies

- **FR-028c (CSP)**: The admin app MUST ship a Content-Security-Policy header from the Next.js middleware (`Strict-Transport-Security` and `Referrer-Policy: strict-origin-when-cross-origin` accompany it). Default policy: `default-src 'self'; script-src 'self' 'nonce-<per-request>'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://<storage-host>; connect-src 'self' <backend-host> <sse-host> <storage-host>; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none';`. Per-route carve-outs (e.g., spec 016 Tiptap may need `'unsafe-eval'` for one specific extension) MUST be declared in `contracts/csp.md` (created with this spec) and applied as **route-scoped** overrides via `app/(admin)/<feature>/layout.tsx` setting a tighter / looser header — never as a global relaxation. The policy MUST NOT relax `script-src` away from nonce-based without an explicit ADR amendment.
- **FR-028d (Session-secret rotation)**: The `iron-session` cookie-encryption secret (`IRON_SESSION_PASSWORD`) MUST be rotated via a **dual-secret window**: the Next.js server reads two env vars, `IRON_SESSION_PASSWORD` (current) and `IRON_SESSION_PASSWORD_PREV` (previous, optional). On every request, sealed cookies are decrypted with the current secret first; on failure, the previous secret is tried. Successful previous-secret reads trigger an immediate re-seal with the current secret, transparent to the user. Operations rotates by (1) setting `_PREV` to the current value, (2) setting current to a freshly generated 32-char value, (3) deploying, (4) waiting one full session-expiry window (default: refresh-token TTL from spec 004), (5) clearing `_PREV`. This MUST NOT log out active admins. The rotation runbook lives in `quickstart.md` "Operations" section.
- **FR-028e (Locale-aware data caching)**: All `react-query` keys for any query whose response contains server-localized strings (product names, descriptions, audit reason notes carrying customer-supplied free text, role labels resolved server-side, etc.) MUST include the active locale as a key segment (e.g., `['catalog', 'product', productId, locale]`). Switching language MUST invalidate localized caches; numeric / pure-id responses (totals, ids, timestamps) need not be re-keyed. A lint rule under `tools/lint/no-locale-leaky-cache.ts` flags `react-query` hooks fetching i18n-bearing endpoints whose key array does not include `useLocale()`. The list of i18n-bearing endpoints lives in `contracts/locale-aware-endpoints.md` (each consuming spec maintains its rows).
- **FR-028f (Audit-from-resource deep links)**: Every feature page that surfaces a resource (product, SKU, order, customer, etc.) MUST expose a "View audit log" affordance in its page header that deep-links to `/audit?resourceType=<Type>&resourceId=<id>` with the filter pre-applied. The button MUST be hidden when the actor lacks `audit.read`. This affordance is the canonical way to get from "this thing" to "history of this thing" — it satisfies the operational need that drove Constitution §25 and saves admins from manual id pasting. The convention MUST be implemented by a shared `<AuditForResourceLink resourceType resourceId />` shell primitive (added to FR-025).
- **FR-028g (Module nav-manifest contributions)**: When an admin module ships, it MUST contribute its sidebar group to spec 004's nav-manifest endpoint via a manifest contribution file under `services/backend_api/.../NavManifest/<module>.json` (spec 004 owns the loader). Each contribution declares: group id, label keys (en + ar), icon key, child route entries with their `requiredPermissions`. The spec 004 endpoint composes contributions filtered to the admin's permission set. **Adding a new admin module is therefore one PR with one manifest contribution, not a spec-004 escalation per route.** Until spec 004 ships the loader (`spec-004:gap:nav-manifest-loader`), the shell falls back to a static manifest assembled at build time from each module's contribution file — same shape, same filter logic.
- **FR-028h (Storybook CI runtime budget)**: The combined Storybook visual-regression suite (015 + 016 + 017 + 018 + 019) MUST keep CI walltime under 10 minutes on the standard runner. Levers: (a) per-module `--grep` runs on PRs touching only that module's `app/(admin)/<module>/` files (full-suite only on `main`); (b) parallelization across at least 3 shards; (c) snapshot diff threshold 0.2% per snapshot, raised only with reviewer approval. A scheduled nightly job runs the full suite end-to-end across all shards and posts results to the team channel; PR-time runs MUST stay incremental. Quickstart documents the per-module commands.

#### Architectural guardrails

- **FR-029**: This spec MUST NOT modify any backend contract. Any gap discovered MUST be raised as an issue against the owning Phase 1B spec, not patched here (per Phase 1C intent).
- **FR-030**: All API access MUST go through generated typed clients (one per consumed Phase 1B service); no ad-hoc HTTP fetch calls in feature code.
- **FR-031**: The app MUST be deployed as a separate container per the Phase 1C-Infra plan — independent from the backend container and the customer-app web build.

### Key Entities *(client-side state, no backend persistence)*

- **AdminSession**: tokens, expiry, admin id, email, display name, locale, market scope (`platform | ksa | eg`), role set, permission set.
- **NavigationEntry**: id, label key (l10n), icon, route, required-permission set, group / order.
- **AuditFilterModel**: actor, resource type, resource id, action key, market scope, timeframe.
- **AuditEntryViewModel**: id, actor, action, resource type, resource id, before json, after json, correlation id, occurred-at.
- **NotificationViewModel**: id, title key, body key, deep link, occurred-at, read flag.
- **SavedView**: id, owner admin id, screen key, filter blob, sort blob, name.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can sign in (incl. MFA) and reach an interactive shell in under 5 seconds on broadband (cold load) on the reference browser matrix.
- **SC-002**: ≥ 99 % of admin sign-in attempts that pass server-side validation succeed without the user encountering a generic / un-localized error state.
- **SC-003**: 100 % of shell surfaces and audit-reader screens render correctly in both Arabic-RTL and English-LTR — measured by a launch-blocker checklist that walks every screen in both locales.
- **SC-004**: 0 user-visible English strings on any screen when the active locale is Arabic.
- **SC-005**: Audit-log filters return the first page of results in under 1 second for typical timeframes (last 7 days, single resource id) on the staging dataset (target: 1M audit rows).
- **SC-006**: Permalinks to audit entries resolve correctly across admins with the required permission ≥ 99 % of the time on the staging dataset.
- **SC-007**: ≥ 95 % of admins find a feature they have permission for within 2 clicks from the post-login landing screen, validated against the navigation tree captured in `data-model.md`.
- **SC-008**: WCAG 2.1 AA contrast + keyboard-nav passes on every shell surface and the audit-log reader (automated axe + manual keyboard walk).
- **SC-009**: 0 backend contract changes shipped from this spec — escalations to the owning Phase 1B spec are tracked as separate issues.

## Assumptions

- **Notifications dependency**: The bell consumes a server-side feed owned by spec 023 (notifications). Until 023 ships, the bell is wired to a stub feed (single seeded entry "Welcome — admin notifications go here when spec 023 lands") so the affordance is discoverable and the wiring is exercised.
- **Saved views storage**: Per-admin saved views (FR-023) are persisted via a user-preferences key on spec 004's account endpoint. If 004 lacks a generic preferences blob, this spec uses `localStorage` as a transitional fallback and files an issue against 004 to add the endpoint.
- **Global search**: Spec 015 ships the **affordance** (top-bar search) but its results provider is intentionally minimal in v1 — search across `audit.log`, `accounts`, `roles`, and limited catalog metadata via the existing 1B endpoints. Cross-module global search (e.g., orders, customers, products in one box) is deferred to a later spec; the affordance is ready to grow.
- **Admin operating environment**: Admins use up-to-date desktop browsers on a corporate / clinic LAN. Mobile-web admin support is **not** in scope for v1 (the customer app is the mobile surface). The admin app is responsive but optimized for ≥ 1280-px-wide screens.
- **Branding**: The admin app reuses the same palette (Principle 7) as the customer app, with admin-specific density (denser tables, tighter padding) — but no separate brand mark. The customer app and the admin app share design tokens.
- **Lane-A handoff**: Spec 004 (identity) merges its contract before this spec begins implementation. Audit-log reader specifically depends on spec 003's audit-log emission contract being stable and on spec 004's RBAC permission catalog.

## Dependencies

- **Spec 003 (foundations)** — `audit_log_entries` schema + read endpoint
- **Spec 004 (identity & access)** — admin auth, MFA, sessions, RBAC permission catalog, user-preferences endpoint
- **`packages/design_system`** — palette tokens, typography, spacing
- **Spec 023 (notifications)** — bell dropdown feed (stubbed until 023 ships)
- Future: **specs 016 – 019** (admin catalog / inventory / orders / customers) consume the shell + shared components from this spec

## Out of Scope (this spec)

- Catalog CRUD, inventory CRUD, order management, customer management — separate specs 016 – 019.
- Verification approval workflow — spec 020.
- B2B quotation administration — spec 021.
- CMS administration — spec 022.
- Support ticketing — spec 023 area.
- Mobile-web admin (≤ 1024-px breakpoints).
- Cross-module global search ranking.
