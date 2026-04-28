# Implementation Plan: Customer App Shell

**Branch**: `phase-1C-specs` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1C/014-customer-app-shell/spec.md`

## Summary

Deliver the **customer-facing app shell** that consumes every Phase 1B contract (004–013) and presents the entire pre-launch consumer + verified-professional flow on Android, iOS, and web — shell + routing + theming, register / login / OTP / reset, home (CMS-stub), product listing + detail, cart, checkout, orders + post-purchase, more menu (addresses + language + logout + verification CTA). Lane B per the implementation plan: **UI only** — every backend gap surfaced during build is escalated as an issue against the owning Phase 1B spec, never patched in this PR.

The shell is implemented as a **single Flutter app** (`apps/customer_flutter/`) using **Bloc / `flutter_bloc`** (ADR-002) for state, with all design tokens consumed from `packages/design_system` (Constitution Principle 7). Auth gates only at checkout / orders / addresses / verification (Principle 3 + Q1 clarification). Cart is server-synced across devices for authenticated customers (Q2). Order detail freshness is pull-to-refresh + open-time fetch (Q3) — live push is deferred to the notifications spec. Minimum platforms: iOS 14+, Android API 24+, evergreen browsers (Q4). OTP supports SMS + email with the channel chosen by spec 004 (Q5).

Generated OpenAPI clients (one per consumed Phase 1B service) provide every HTTP surface; no ad-hoc HTTP. AR-RTL is first-class — every screen ships in both languages with editorial-grade Arabic copy from launch (Principle 4).

## Technical Context

**Language/Version**: Dart 3.5 / Flutter 3.24 (stable channel; matches latest LTS on stable tracks at the planning date).

**Primary Dependencies**:

- `flutter_bloc` ^8.1 — state management (ADR-002).
- `go_router` ^14 — declarative routing, deep linking, web URL strategy.
- `dio` ^5 + `dio_cookie_manager` — HTTP client used by generated OpenAPI clients (cookie manager covers anonymous-cart-token cookie path).
- `openapi-generator-cli` (build-time) producing `dart-dio` clients into `lib/generated/api/<service>/` per Phase 1B service (one per spec 004–013 OpenAPI document under `services/backend_api/openapi.*.json`).
- `intl` ^0.19 + `flutter_localizations` — locale + numeral + currency formatting.
- `hijri` ^3 — Hijri calendar formatting where applicable per Principle 4.
- `flutter_secure_storage` ^9 — refresh + access tokens, anonymous cart token (Keychain on iOS, EncryptedSharedPreferences on Android, `localStorage` + WebCrypto wrap on web).
- `cached_network_image` ^3 — product media gallery + banners.
- `bloc_test` ^9, `mocktail` ^1, `golden_toolkit` ^0.15 — Bloc unit tests, widget tests, golden tests for AR-RTL parity.
- `sms_autofill` ^2 (Android only) — SMS auto-fill for OTP entry; iOS uses platform-native `oneTimeCode` `textContentType`.
- `app_links` ^6 — universal / app links for deep linking (auth resume after login, product detail share, password-reset link). Firebase Dynamic Links is **not** used (sunsetting; see research §R8).
- `package_info_plus`, `device_info_plus` — diagnostics surfaced in support-shortcut payloads.
- `packages/design_system` (in-repo path dep) — palette tokens (Principle 7 colours), typography, spacing, base components.
- `packages/shared_contracts` (in-repo path dep) — any cross-app contract types not covered by OpenAPI generation.

**Storage**: No server-side persistence introduced by this spec. Client-side: secure storage (tokens, anonymous cart token, language preference, market code) + ephemeral in-memory Bloc state. No client database — spec 010 cart drift detection is the source of truth, not a local cart cache.

**Testing**:

- `bloc_test` — every Bloc has unit tests covering all state transitions documented in `data-model.md`.
- `flutter_test` widget tests for all states (loading / empty / error / success / restricted / payment-failure-recovery) per FR-005.
- `golden_toolkit` golden tests rendering each screen in **AR-RTL** and **EN-LTR** at the three reference device sizes; CI fails on golden diff. This is how SC-003 (100% screens render in both locales) is mechanically enforced.
- `integration_test` end-to-end flows running against a `docker compose` stack of Phase 1B backend services with seeded data — used to verify Story 1 (browse → buy → confirmation) on Android emulator, iOS simulator, and Chrome.
- Localization lint: a Dart script in `tool/lint/` walks the widget tree and fails on any hard-coded user-facing string outside the ARB-backed localization layer (enforces FR-008).

**Target Platform**: iOS 14+, Android API 24+ (Android 7.0+), evergreen desktop browsers — Chrome / Edge / Safari / Firefox (current and previous major version). Reference devices for SC-006 perf budgets: Pixel 6a (Android), iPhone 13 (iOS), Chrome on a 2022-era laptop on broadband.

**Project Type**: Mobile + web app under the modular monorepo (ADR-001). Single Flutter app targeting all three platforms via the same code base; platform-specific code lives in `lib/core/platform/` behind a thin adapter.

**Performance Goals**:

- **SC-006**: cold launch → interactive home ≤ 3 s Android, ≤ 2 s iOS, ≤ 4 s web (broadband).
- **SC-001**: full purchase flow ≤ 4 minutes end-to-end on the reference Android device on 4G.
- Frame budget: 60 fps on listing scroll on the reference Android; no jank above 16.7 ms median.

**Constraints**:

- **No backend code in this PR** (FR-031). Any gap escalates to the owning 1B spec.
- **No hard-coded strings in non-ARB code paths** (FR-008). Lint enforces.
- **No Riverpod / Provider / GetX** anywhere — Bloc only (ADR-002, FR-032).
- **No ad-hoc HTTP** — all API calls go through generated clients (FR-033).
- **No hard-coded market literals in UI logic** (FR-011). Market comes from the active session or device-locale heuristic via a single `MarketResolver`.
- **No backend events on a live socket** in this spec. Order detail uses pull-to-refresh + open-time fetch (Q3, FR-026).
- **No new design tokens** outside `packages/design_system`. New tokens land in that package via a separate PR if needed.

**Scale/Scope**: ~22 screens across 7 feature folders, 4 prioritized user stories, 33 functional requirements, 8 success criteria, 5 clarifications integrated. Two locales × three platforms × four golden scenarios per screen ≈ ~528 golden images for the AR/EN parity check. Estimated ~120 task units when `/speckit-tasks` decomposes the user stories.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Guests browse home / listing / detail / search / prices without auth (FR-012). Auth required at checkout submit, orders, addresses, verification, and add-to-cart for restricted products (FR-013). | PASS |
| P4 Arabic / RTL editorial | Every screen ships AR + EN with full RTL via `Directionality` + `MaterialApp.localizationsDelegates` + ARB files; AR copy passes editorial review before launch (FR-007 – FR-010). Localization lint blocks hard-coded strings; golden tests block layout regressions in RTL. | PASS |
| P5 Market Configuration | `MarketResolver` derives KSA / EG from session, account, or device-locale heuristic — never hard-coded in business logic (FR-011). Currency, payment-method options, shipping options all flow from per-market server config. | PASS |
| P6 Multi-vendor-ready | UI presents catalog / orders / inventory contracts as opaque server data — no single-vendor assumptions baked into Blocs or UI flows. When 1B contracts gain vendor surfaces, this app upgrades by regenerating clients. | PASS |
| P7 Branding | Theme consumed exclusively from `packages/design_system` tokens; primary `#1F6F5F`, secondary `#2FA084`, accent `#6FCF97`, neutral `#EEEEEE` plus brand-overlay semantic colours. No inline `Color(0x…)` literals in feature code (lint enforces). | PASS |
| P8 Restricted Products | Restricted products visible with prices to all users; **Add to cart** for restricted products is gated with a clear verification CTA for guests / unverified customers (FR-021, Story 1 acceptance scenario 3). | PASS |
| P9 B2B | B2B-specific UI (quotation, company accounts, approvers, bulk order) is **out of scope** for this spec (deferred to spec 021). The shell is forward-compatible: navigation tree, more-menu, and order list reserve space for B2B entry points without baking B2B assumptions into the consumer flow. | PASS (forward-compatible deferral) |
| P15 Reviews | Review submission UI deferred to a later spec; product detail reserves layout for review summary but renders an empty placeholder. | PASS |
| P17 Order / Payment / Fulfillment / Refund | Orders list and detail render the four state streams as **independent signals** consumed from spec 011 (FR-025, FR-026). No single-status badge collapses them. | PASS |
| P22 Fixed Tech | Flutter (mobile + web) per Constitution. No deviation. | PASS |
| P23 Architecture | Modular monolith on the backend (Lane A); Lane B is a single Flutter app consuming the modular monolith via generated OpenAPI clients. No premature service extraction; no UI-side micro-frontend split. | PASS |
| P24 State Machines | Five client-side state machines explicit in `data-model.md`: `AuthSession`, `CartSync`, `CheckoutFlow`, `OrderListFilter`, `LocaleAndDirection`. Each documents states, triggers, transitions, failure handling. | PASS |
| P25 Data & Audit | Client never writes to audit storage. All audit-emitting actions happen server-side per spec 011 / 013 / etc. The client surfaces audit-derived state (e.g., order timeline) read-only. | PASS (UI does not own audit) |
| P27 UX Quality | Every screen implements loading / empty / error / success / restricted / payment-failure-recovery states (FR-005). Accessibility is enforced by `flutter_test`'s semantics-node checks and a per-screen accessibility checklist. | PASS |
| P28 AI-Build Standard | Spec ships with explicit FRs, acceptance scenarios, edge cases, success criteria, and 5 resolved clarifications. No "standard mobile app behaviour" hand-waving. | PASS |
| P29 Required Spec Output | Goal / roles / rules / flows / states / data / validation / contracts consumed / edge cases / acceptance / phase / deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1C Milestone 5. Depends on Phase 1B contracts being merged (per implementation plan dependency rule). No scope creep into 1D / 1E. | PASS |
| P31 Constitution Supremacy | No conflicts. | PASS |
| ADR-001 Monorepo | Code lives under `apps/customer_flutter/` and `packages/design_system/` in the existing monorepo. No new repo. | PASS |
| ADR-002 Bloc | `flutter_bloc` only — no Riverpod / Provider / GetX. Verified by lint rule + dependency review. | PASS |
| ADR-005 Search engine (Meilisearch) | Listing screen consumes spec 006 contracts which abstract Meilisearch — UI never speaks to Meilisearch directly. | PASS |
| ADR-007 Payment providers | Checkout payment-method picker is per-market data-driven (via spec 010) — the app never references Mada / Visa / Apple Pay / STC Pay / BNPL by hard-coded literal. | PASS |
| ADR-008 Shipping providers | Shipping picker is data-driven from spec 010 quotes endpoint. | PASS |
| ADR-009 OTP / Notification providers | OTP entry screen is provider-agnostic — channel chosen server-side (Q5). SMS auto-fill is platform-feature (Android / iOS), not provider-specific. | PASS |
| ADR-010 KSA residency | All API calls hit the Phase 1B backend hosted in Azure Saudi Arabia Central. No third-party data endpoints introduced here. | PASS |

**No violations.** No entries needed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/phase-1C/014-customer-app-shell/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 — library / pattern research
├── data-model.md        # Phase 1 — client-side view models + state machines
├── quickstart.md        # Phase 1 — local dev setup
├── contracts/
│   ├── consumed-apis.md      # OpenAPI specs consumed (one row per 1B service)
│   ├── deeplink-routes.md    # Universal-link / app-link route table
│   └── client-events.md      # Client-emitted analytics / audit-trigger events
├── checklists/
│   └── requirements.md       # Already created by /speckit-specify
└── tasks.md             # Phase 2 — produced by /speckit-tasks
```

### Source Code (repository root)

```text
apps/customer_flutter/
├── lib/
│   ├── main.dart                   # Entrypoint — boot DI, run app
│   ├── app/                        # Shell
│   │   ├── app.dart                # MaterialApp.router + theme + localization wiring
│   │   ├── router.dart             # go_router config + auth-gated route guards
│   │   ├── theme.dart              # Reads tokens from packages/design_system
│   │   └── di.dart                 # GetIt composition root
│   ├── features/
│   │   ├── auth/                   # Story 1 dependency + Story 4 logout
│   │   │   ├── bloc/
│   │   │   ├── screens/            # Register, Login, OtpEntry, ResetRequest, ResetConfirm
│   │   │   └── widgets/
│   │   ├── home/                   # Story 1 — banners + featured + categories
│   │   ├── catalog/                # Story 1 — listing + detail
│   │   ├── cart/                   # Story 1 — cart + Q2 server-sync
│   │   ├── checkout/               # Story 1 — drift + idempotency + state outcome
│   │   ├── orders/                 # Story 2 — list + detail + reorder + support
│   │   └── more/                   # Story 4 — addresses + language + logout + verification CTA
│   ├── core/
│   │   ├── api/                    # Dio + interceptors (auth header, correlation id, locale, market)
│   │   ├── auth/                   # AuthSession Bloc + secure-storage adapter
│   │   ├── cart/                   # AnonymousCartToken + CartSync utilities
│   │   ├── localization/           # ARB loaders + LocaleBloc + RTL flag
│   │   ├── market/                 # MarketResolver
│   │   ├── routing/                # Deep-link parser, post-login resume
│   │   ├── platform/               # Android / iOS / web adapters (sms_autofill, app_links, etc.)
│   │   └── observability/          # Crash reporting + analytics adapter
│   ├── generated/api/              # Generated OpenAPI clients (build-time, gitignored)
│   └── l10n/
│       ├── app_en.arb
│       └── app_ar.arb
├── test/
│   ├── bloc/                       # Bloc unit tests
│   ├── widget/                     # Widget tests per state
│   ├── golden/                     # AR-RTL × EN-LTR × 3 device sizes per screen
│   └── localization_lint/          # No hard-coded strings test
├── integration_test/               # End-to-end Story 1 / 2 flows
├── android/                        # Generated by `flutter create` — minimal customization
├── ios/                            # Generated by `flutter create` — minimal customization
├── web/                            # Generated by `flutter create` — index.html + manifest
├── tool/
│   └── lint/                       # Custom Dart lint scripts (l10n-no-hardcoded, design-system-only)
├── analysis_options.yaml           # Pedantic + custom lints
├── build.yaml                      # OpenAPI generator config
├── pubspec.yaml
└── README.md

packages/design_system/             # Already exists — extended in this spec only if absolutely needed
├── lib/
│   ├── tokens/                     # Colour / typography / spacing tokens
│   └── components/                 # Base components consumed by features
└── pubspec.yaml
```

**Structure Decision**: One Flutter app, multi-platform (mobile + web) under `apps/customer_flutter/`. Vertical-slice feature folders under `lib/features/<feature>/{bloc,screens,widgets}` to mirror the per-feature folder convention used on the backend (ADR-003) — every Phase 1C feature folder maps 1:1 to a user story and to a depended-on Phase 1B spec, so cross-spec navigation stays trivial. `lib/core/` holds infrastructure shared across features. `packages/design_system/` is a path-dep package, not duplicated inside the app.

## Complexity Tracking

> No constitution violations. The following entries document **intentional non-obvious choices** for downstream reviewers, not violations.

| Choice | Why | Simpler alternative rejected because |
|---|---|---|
| Generated OpenAPI clients per service (10 generated client packages) | Type-safe, one drop-in regen per backend contract change. Aligns with FR-033 ("no ad-hoc HTTP"). | Hand-written `Dio` services would re-introduce drift risk every time a 1B service updates its OpenAPI doc. |
| Single Flutter app for mobile + web (not separate web SPA) | One Bloc layer, one design-token consumer, one localization layer, one analytics surface — matches the implementation-plan's Lane B intent of "one customer app shell". | A separate Next.js SPA would duplicate the entire client domain logic for marginal SEO gains; SEO is not a launch goal for the customer app per the implementation plan. |
| Server-side anonymous cart token (not local-only guest cart) | Required by Q2 (server-synced cart) and by the conversion-path requirement that a guest cart survives the auth step. | A device-local guest cart would force a merge step at login that's both lossy and confusing for users. |
| Pull-to-refresh + open-time fetch for order detail (not server push) | Per Q3 — owned by the notifications spec. Keeps Phase 1C scope clean and avoids reconnection / battery cost. | A websocket / SSE channel here would couple this spec to spec 023 (notifications) which has not yet shipped its server contract. |
| Golden tests per screen × locale × device size | This is the only mechanism that catches AR-RTL layout regressions before launch — manual screenshot review across ~528 combinations is not feasible. | Manual QA would either miss regressions or take days per change. |
| Localization lint script (custom Dart tool) | FR-008 ("no English string in the AR build") is non-trivial to enforce by review alone — a code lint is the only repeatable signal. | Code review alone has missed locale leaks in past projects on every team. |
