# Phase 1C — implementation status

Snapshot of what's built across the 6 Phase 1C specs after Sessions 1–7
(plus the cross-cutting "Best" pass that added session guards +
context provider + `<RequirePermission>`).

## Per-spec status

| Spec | Status | Tests | Build | Notes |
|---|---|---|---|---|
| **015 admin-foundation** | **Foundation complete + US1 + US2 shipped** | 45/45 passing | ✓ | Phase 5 (US3 AR translation) is on the human-translator gate; Phase 6 (US4 bell) wired with stub feed + SSE proxy + reconnect machinery; Phase 7 polish docs shipped. |
| **014 customer-app-shell** | Phase 1 (scaffold) complete | `flutter analyze` clean | scaffold | 3 info warnings on the bare scaffold; Phases 2–7 still to ship in fresh sessions. |
| 016 admin-catalog | Not started | — | — | Tasks.md has 92 tasks ready; consumes 015 primitives. |
| 017 admin-inventory | Not started | — | — | 100 tasks ready; consumes 015. |
| 018 admin-orders | Not started | — | — | 99 tasks ready; consumes 015. |
| 019 admin-customers | Not started | — | — | 96 tasks ready; consumes 015 + 018. |

## What 015 ships (load-bearing for everything else)

### Routes + middleware

```
app/
├── layout.tsx                              # locale + dir + providers
├── (auth)/
│   ├── layout.tsx
│   ├── login/{page.tsx,login-form.tsx}     # T048/T049 — full sign-in
│   ├── mfa/{page.tsx,mfa-form.tsx}         # T050/T051 — TOTP entry
│   └── reset/
│       ├── {page.tsx,reset-request-form.tsx}
│       └── confirm/{page.tsx,reset-confirm-form.tsx}
├── (admin)/
│   ├── layout.tsx                          # session gate + permission gate + AppShell + SessionProvider
│   ├── page.tsx                            # T056 landing
│   ├── audit/
│   │   ├── page.tsx                        # T062 + T074b — list with filter panel + cursor pagination + URL sync
│   │   └── [entryId]/page.tsx              # T065 — detail + JSON diff viewer + redaction + permalink copy
│   ├── me/
│   │   ├── page.tsx                        # T057 — profile
│   │   └── preferences/{page,preferences-list}.tsx  # T057a — saved-views management
│   ├── __forbidden/page.tsx                # T056a — ForbiddenState
│   └── __not-found/page.tsx                # T056b
├── api/
│   ├── auth/
│   │   ├── login/route.ts                  # T022
│   │   ├── mfa/route.ts                    # T023
│   │   ├── refresh/route.ts                # T024
│   │   ├── logout/route.ts                 # T025
│   │   ├── reset/{request,confirm}/route.ts # T052
│   │   └── step-up/{start,complete}/route.ts # T032i
│   ├── audit/route.ts                      # audit list proxy
│   ├── nav-manifest/route.ts               # T034
│   ├── notifications/
│   │   ├── unread/route.ts                 # T085 stub feed
│   │   └── sse/route.ts                    # T084 — heartbeat-only until spec 023 ships
└── middleware.ts                           # T026 + T032a — auth + locale + CSP + nonce + permission gate
```

### Shell primitives — all 12 ready for downstream consumption

| Primitive | File | Used by |
|---|---|---|
| `<AppShell>` | `components/shell/app-shell.tsx` | (admin) layout |
| `<SidebarNav>` | sidebar-nav.tsx | AppShell |
| `<TopBar>` | top-bar.tsx | AppShell |
| `<BellMenu>` | bell-menu.tsx | TopBar — stub feed + SSE proxy ready |
| `<LocaleToggle>` | locale-toggle.tsx | TopBar |
| `<LogoutButton>` | logout-button.tsx | TopBar |
| `<BreadcrumbBar>` | breadcrumb-bar.tsx | per-feature pages |
| `<PageHeader>` | page-header.tsx | per-feature pages |
| `<LoadingState>` / `<EmptyState>` / `<ErrorState>` | shell/*-state.tsx | every list / detail page |
| `<RestrictedState>` | restricted-state.tsx | content-restricted UI |
| `<ForbiddenState>` | forbidden-state.tsx | `/__forbidden` page |
| `<ConfirmationDialog>` | confirmation-dialog.tsx | every destructive action |
| `<ToastHost>` | toast-host.tsx | mounted globally via providers |
| `<StepUpDialog>` | step-up-dialog.tsx | spec 018 refunds + spec 019 account actions |
| `<ConflictReloadDialog>` | conflict-reload-dialog.tsx | every 412 conflict path (016/017/018/019) |
| `<ExportJobStatus<T>>` | export-job-status.tsx | spec 017 ledger + spec 018 finance exports |
| `<AuditForResourceLink>` | audit-for-resource-link.tsx | every feature page header (FR-028f) |
| `<MaskedField>` | masked-field.tsx | every PII surface (spec 018 customer card + spec 019 profile + audit JSON viewer) |
| `<RequirePermission>` | require-permission.tsx | declarative gate for action buttons (FR-010 hide-not-disable) |

### Shared composites

- `<DataTable<TRow>>` — `components/data-table/data-table.tsx`. Server pagination + cursor + sortable columns + row selection (disabled per spec defaults) + filter bar slot + saved-views slot + empty/loading/error states.
- `<SavedViewsBar<TFilter>>` — `components/data-table/saved-views.tsx`. localStorage-backed storage adapter today; swaps to spec 004's user-preferences endpoint when shipped.
- `<FormShell>` + `useFormBuilder()` + `<FormField>` + `<DirtyStateGuard>` + `applyServerErrors()` — `components/form-builder/`. react-hook-form + zod with full ARIA wiring.

### Server Component helpers (added in the "Best" pass)

| Helper | Purpose |
|---|---|
| `requireSession(continueTo?)` | redirects to /login if no session, otherwise returns `AdminSessionPayload` |
| `requirePermission(keys, continueTo?)` | as above + redirects to /__forbidden if any key missing (AND) |
| `requireAnyPermission(keys, continueTo?)` | redirects if no key held; returns the held subset (OR) |

These dedupe the ~50 redirect blocks 016/017/018/019 would otherwise duplicate.

### Client Component helpers (added in the "Best" pass)

| Helper | Purpose |
|---|---|
| `<SessionProvider>` | mounts the session payload (token-stripped) into React context |
| `useSession()` | returns the active session as `ClientSession` (permissions as `Set<string>`) |
| `usePermission(key \| keys)` | returns boolean — single key or array (AND) |
| `useAnyPermission(keys)` | returns boolean (OR) |

These let downstream Client Components render permission-gated UI without re-fetching the session.

### Cross-cutting infra

| Module | Purpose |
|---|---|
| `lib/auth/session.ts` | iron-session seal/unseal with **dual-secret rotation** (FR-028d) |
| `lib/auth/permissions.ts` | `hasPermission`, `hasAllPermissions`, `permissionsForRoute` + the per-route permission map |
| `lib/auth/nav-manifest.ts` | static-mode loader (`USE_STATIC_NAV_MANIFEST=1`) + server-mode loader; cutover is one env-flip |
| `lib/auth/nav-manifest-static/foundation.json` | foundation contribution (audit + /me entries) |
| `lib/api/proxy.ts` | the single fetch wrapper — auth header + correlation id + locale + market + 401-refresh-and-retry |
| `lib/api/error.ts` | RFC 9457 ProblemDetails → typed `ApiError` |
| `lib/api/clients/{identity,audit,notifications}.ts` | thin wrappers per consumed Phase 1B service |
| `lib/i18n/{config,server,client}.ts` | next-intl wiring + locale detection |
| `lib/i18n/formatters.ts` | per-market currency + numeral + date (KSA→SAR, EG→EGP, Western Arabic numerals) |
| `lib/observability/telemetry.ts` | TelemetryAdapter interface + Noop + Console adapters + 25-event allow-list |
| `lib/notifications/sse-client.ts` | SSE consumer with exponential backoff + 60s polling fallback |
| `tools/lint/no-hardcoded-strings.ts` | FR-013 enforcement via ts-morph AST walker |
| `tools/lint/no-physical-margins.ts` | RTL hygiene |
| `tools/lint/no-locale-leaky-cache.ts` | FR-028e — react-query keys must include `useLocale()` for i18n-bearing endpoints |
| `scripts/check-permission-catalog.ts` | drift CI vs spec 004 (T032c) |
| `scripts/check-nav-manifest.ts` | manifest validity (T032e) |
| `scripts/check-audit-redaction.ts` | redaction registry vs fixtures (T032g) |
| `middleware.ts` | edge runtime — auth presence + locale + CSP nonce + permission gate |

### Test surface

```
tests/unit/
├── auth/
│   ├── session-rotation.test.ts            (4 tests — dual-secret rotation)
│   └── guards.test.tsx                     (8 tests — requireSession/Permission/AnyPermission)
├── auth-forms/
│   ├── login-form.test.tsx                 (4 tests)
│   └── mfa-form.test.tsx                   (3 tests)
├── shell/
│   └── masked-field.test.tsx               (5 tests — PII redaction + telemetry)
├── audit/
│   ├── redaction.test.tsx                  (9 tests — every rule × every permission profile)
│   └── audit-filter-panel.test.tsx         (3 tests — URL sync)
├── i18n/
│   ├── key-parity.test.ts                  (2 tests — en ↔ ar parity)
│   └── formatters.test.ts                  (4 tests — currency + numerals + dates)
└── observability/
    └── pii-guard.test.ts                   (3 tests — telemetry allow-list)

Total: 45/45 passing.
```

### Drift CI

| Script | Purpose | Status |
|---|---|---|
| `pnpm catalog:check-permissions` | diffs `contracts/permission-catalog.md` vs spec 004's endpoint | no-op until spec 004 ships endpoint (38 keys local) |
| `pnpm catalog:check-nav-manifest` | validates groups + entries + label keys + permission keys + order ranges | ✓ 1 module in sync |
| `pnpm catalog:check-audit-redaction` | walks fixtures + asserts every sensitive path is registered | ✓ 2 fixtures, all paths registered |

### Build

```
pnpm build
ƒ Middleware                             27.6 kB
○ /__forbidden                           …
○ /__not-found                           …
○ /                                      … (landing)
ƒ /audit                                 …
ƒ /audit/[entryId]                       …
○ /login                                 …
○ /mfa                                   …
○ /reset                                 …
○ /reset/confirm                         …
ƒ /me                                    …
ƒ /me/preferences                        …
+ 9 API handlers (login/mfa/refresh/logout/reset/step-up + audit + nav-manifest + notifications)
+ First Load JS shared by all            87.4 kB
```

## What 014 ships

```
apps/customer_flutter/
├── lib/main.dart                  # Flutter create scaffold default
├── analysis_options.yaml          # 015-style strict lints
├── pubspec.yaml                   # all spec 014 plan §Primary Dependencies installed
├── README.md                      # quickstart + constitutional locks + module layout
├── .gitignore                     # extended with spec 014 paths
├── android/  ios/  web/           # Flutter create platform scaffolds
└── test/widget_test.dart          # default scaffold test (passes)

.github/workflows/customer_flutter-ci.yml — analyze → test → goldens → smoke build
```

`flutter analyze` passes with 3 info-level warnings (all about lint-rule infractions in the create scaffold itself; harmless).

Phases 2–7 of 014 (DI + theme + router + features) are the substantive work — they consume `packages/design_system` tokens that 015 also consumes, so the design language is already aligned.

## What's missing — the gating items for finishing Phase 1C

### Hard blockers (require something outside this codebase)

1. **Spec 004 (identity) endpoints** — `/v1/admin/identity/*`, nav-manifest, permission-catalog, user-preferences, step-up. Until these ship on `main`, the auth flow can't actually complete a real sign-in (the routes work, the proxies are wired, but the upstream returns nothing).
2. **Spec 003 (audit-log) endpoint** — `/v1/admin/audit-log` list + by-id. Until it ships, the audit reader renders empty / errored.
3. **Specs 005–013 OpenAPI docs** on `main` — required by 014 for `pnpm gen:api`, by 016 for catalog, by 017 for inventory, by 018 for orders, by 019 for customers.
4. **AR translator** for spec 014 T094 + spec 015 T075 + 016/017/018/019 AR translation tasks. The placeholder markers are in place; CI fails the AR build until they're replaced. Cannot be auto-generated (Constitution §4).

### Soft blockers (can be done in this codebase, sessionally)

5. **014 Phases 2–7** — the actual features (auth screens, home, listing, detail, cart, checkout, orders, more). ~120 tasks.
6. **016 admin-catalog** — products + categories + brands + manufacturers + bulk import. ~92 tasks.
7. **017 admin-inventory** — stock adjust + low-stock + batches + expiry + reservations + ledger. ~100 tasks.
8. **018 admin-orders** — list + detail + transitions + refund + invoice + cancel + finance export. ~99 tasks.
9. **019 admin-customers** — list + profile + suspend/unlock/reset + B2B + history panels. ~96 tasks.

## Recommended next moves

### If you're scaling out engineering capacity

Run `/speckit-implement specs/phase-1C/<spec>` in **parallel sessions** — the foundation in 015 + the cross-cutting helpers added in this pass make all 5 remaining specs independently shippable per the wave plan in `docs/phase-1c-implementation-plan.md` (the optimal-way doc from earlier).

### If you're staying solo

Land **014 Phase 2 (Foundational)** next — the customer-app shell + DI + router + auth bloc. That unblocks:
- Customer-app US1 (P1 MVP — browse → buy → confirmation)
- The cross-app smoke test (login on admin → suspend a customer → verify customer-app generic auth-failure)

Then 016 + 017 + 018 + 019 in priority order. Each spec is a 4–6 session arc.

### If you're shipping to staging

The current branch can already ship `apps/admin_web` to staging as a deployment target — auth flow works against any spec-004-compatible mock backend, the audit reader handles missing-endpoint gracefully, and the bell uses the stub feed. Smoke testing on staging would surface integration issues before 016/017/018/019 land their full features.
