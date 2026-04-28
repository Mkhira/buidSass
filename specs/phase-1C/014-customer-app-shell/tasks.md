---

description: "Tasks for Spec 014 — Customer App Shell"
---

# Tasks: Customer App Shell

**Input**: Design documents from `/specs/phase-1C/014-customer-app-shell/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{consumed-apis.md,deeplink-routes.md,client-events.md}, quickstart.md

**Tests**: Tests are part of this spec — `bloc_test` for state machines, `flutter_test` widget tests for state coverage, `golden_toolkit` goldens for AR-RTL × EN-LTR parity (mandated by SC-003), `integration_test` for Story 1/2 happy paths. Localization lint enforces FR-008.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and demoed independently. Stories run in priority order (P1 → P4); within a story, foundational widgets / Blocs precede screen wiring.

## Format

`[ID] [P?] [Story] Description (path)`

- `[P]` — parallelizable (different files, no incomplete-task deps)
- `[USn]` — user story (US1 = P1 MVP)

## Path conventions

- App code: `apps/customer_flutter/lib/`
- Tests: `apps/customer_flutter/test/` and `apps/customer_flutter/integration_test/`
- Generated clients (gitignored): `apps/customer_flutter/lib/generated/api/`
- Design system (in-repo path dep): `packages/design_system/`

---

## Phase 1: Setup (project initialization)

**Purpose**: Scaffolding the Flutter app, dependencies, and CI before any feature work.

- [X] T001 Run `flutter create --org com.dentalcommerce --platforms=android,ios,web --project-name customer_flutter apps/customer_flutter`. **Done** (Session 7) — Flutter 3.32.x scaffold (81 files written) under `apps/customer_flutter/`. Targets Android + iOS + web per FR-001.
- [X] T002 [P] Author `pubspec.yaml` with the dependency list. **Done** (Session 7) — installed: flutter_bloc 8.1, go_router 14.6, dio 5.7, dio_cookie_manager 3.1, cookie_jar 4, intl 0.20.2 (intl pinned by flutter_localizations), hijri 3, flutter_secure_storage 9.2, cached_network_image 3.4, sms_autofill 2.4, app_links 6.3, package_info_plus 8, device_info_plus 11, get_it 7.7. Dev: bloc_test 9.1, mocktail 1, golden_toolkit 0.15, integration_test (sdk). 104 transitive deps installed clean. **Spec deviation**: `intl: ^0.19.0` in spec → `^0.20.2` actual (forced by flutter_localizations from current Flutter SDK).
- [X] T003 [P] Author `analysis_options.yaml`. **Done** (Session 7) — extends `flutter_lints`; enables prefer_relative_imports, unawaited_futures, avoid_print, prefer_single_quotes, sort_pub_dependencies, immutability rules, async hygiene rules, directives_ordering. Excludes `lib/generated/**` and `**/*.g.dart`.
- [ ] T004 [P] Author `build.yaml` for `openapi_generator`. **Deferred** — happens when Phase 2 wires the Dio interceptor stack + generated clients. The dep `openapi_generator` is heavy (Java + node wrapper); the generation step runs out-of-band when spec 004–013 OpenAPI docs are stable on `main`.
- [X] T005 [P] Add `lib/generated/` to `.gitignore`. **Done** (Session 7) — appended `lib/generated/`, `test/golden/failures/`, `test/golden/baselines/__diffs__/`, `.env*.local`, `docs/perf/`.
- [X] T006 [P] Author `README.md`. **Done** (Session 7) — full quickstart + constitutional locks + module layout + tests section + known limitations.
- [X] T007 [P] Author `.github/workflows/customer_flutter-ci.yml`. **Done** (Session 7) — `pub get → l10n lint (deferred to T029) → analyze → unit+widget tests → goldens → smoke build for web + apk`. Drift-checks job verifies SC-008 (no OpenAPI files changed within the branch's diff).
- [X] T008 Run `flutter pub get` end-to-end. **Done** (Session 7) — `flutter analyze` passes with 3 minor info-level warnings (sort_pub_dependencies + directives_ordering); zero errors. Build toolchain green.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Shell, DI, API client stack, localization, market resolver, theme. **Every user story depends on this phase.**

⚠️ **CRITICAL**: No user-story work can begin until this phase is complete.

### Composition root + shell

- [X] T009 Create `apps/customer_flutter/lib/main.dart` that boots the DI container and runs `AppShell`.
- [X] T010 Create `lib/app/di.dart` (GetIt composition root) registering all services declared in later phases as factories / singletons.
- [X] T011 Create `lib/app/app.dart` with `MaterialApp.router`, `localizationsDelegates`, `supportedLocales: [en, ar]`, theme + dark theme placeholders, and `Directionality` driven by `LocaleBloc`.
- [X] T012 Create `lib/app/router.dart` with `go_router` config covering all routes from `contracts/deeplink-routes.md`, including the `redirect:` callback wired to `AuthSessionBloc` for auth-gated routes.
- [X] T013 Create `lib/app/theme.dart` reading every colour / typography / spacing token from `packages/design_system/lib/tokens/` (Constitution Principle 7 palette).

### Design-system extensions (only what's missing for v1)

- [X] T014 [P] Audit `packages/design_system/lib/tokens/` against `spec.md` palette — primary `#1F6F5F`, secondary `#2FA084`, accent `#6FCF97`, neutral `#EEEEEE` plus brand-overlay semantics — add any missing tokens.
- [X] T015 [P] Add base components needed by every feature (`AppScaffold`, `AppButton`, `AppTextField`, `AppListTile`, `LoadingState`, `EmptyState`, `ErrorState`, `RestrictedBadge`) to `packages/design_system/lib/components/`.

### API client stack

- [X] T016 Create `lib/core/api/dio_factory.dart` building a `Dio` instance with default timeouts and base URL from `--dart-define=API_BASE_URL=…`.
- [X] T016a Add an HTTPS-enforcement guard in `dio_factory.dart` per FR-015b.
- [X] T017 [P] Create `lib/core/api/auth_interceptor.dart` (attaches `Authorization: Bearer <access>`; refresh-on-401 path).
- [X] T018 [P] Create `lib/core/api/correlation_id_interceptor.dart` (UUID v4 per request).
- [X] T019 [P] Create `lib/core/api/locale_market_interceptor.dart` (`Accept-Language` + `X-Market-Code`).
- [X] T020 [P] Create `lib/core/api/idempotency_interceptor.dart` (used by checkout submit only).
- [X] T021 Create `lib/core/api/api_module.dart` registering Dio + interceptors + every generated client into the DI container.
- [X] T022 Create `tool/lint/no_ad_hoc_http.dart` failing the build on direct `Uri.parse('http…')` outside `lib/core/api/` (FR-033).

### Auth session foundation

- [X] T023 Create `lib/core/auth/secure_token_store.dart` wrapping `flutter_secure_storage` with web `EncryptedLocalStorage` adapter (research §R5).
- [X] T023a Versioned-schema migration runner in `secure_token_store.dart` per FR-015a; emits `auth.storage.migrated`. Tests cover clean install / no-op / downgrade-wipe / corrupt-keychain.
- [X] T024 Create `lib/core/auth/auth_session_bloc.dart` implementing SM-1 from `data-model.md` (`Guest` ↔ `Authenticating` ↔ `Authenticated` ↔ `Refreshing` ↔ `RefreshFailed` ↔ `LoggingOut`).
- [X] T025 [P] Create `lib/core/auth/auth_session_bloc_test.dart` with one `blocTest` per SM-1 transition.

### Localization scaffold

- [X] T026 Create `lib/l10n/app_en.arb` and `lib/l10n/app_ar.arb` seeded with all string keys discovered while writing screens in subsequent phases. **AR file ships with `EN_PLACEHOLDER` markers — Constitution Principle 4 forbids autonomous translation; T094 is the human-translator gate.**
- [X] T027 Create `lib/core/localization/locale_bloc.dart` implementing SM-5 (`EN_LTR` ↔ `AR_RTL`); reads first-launch state from device locale.
- [X] T028 [P] Create `lib/core/localization/locale_bloc_test.dart` covering toggle + first-launch resolution.
- [X] T029 Create `tool/lint/no_hardcoded_strings.dart` per research §R4 walking every file under `lib/features/**/{screens,widgets}/` and failing on user-facing string literals.
- [X] T029a `tool/lint/no_locale_leaky_cache.dart` reads the contracts registry and fails on data-layer files that touch a registered endpoint without adopting the mixin or keying cache by locale.
- [X] T029b `lib/core/api/i18n_aware_repository.dart` mixin exposes `isI18nBearing` + `discardInflightOnLocaleChange()` + `localeChanges` stream + `bindLocale()`.

### Market resolver

- [X] T030 Create `lib/core/market/market_resolver.dart` deriving market from session → account → device locale (`*-SA` → `ksa`, `*-EG` → `eg`, otherwise `ksa` per ADR-010 / Assumptions).
- [X] T031 [P] Create `lib/core/market/market_resolver_test.dart`.

### Cart token foundation

- [X] T032 Create `lib/core/cart/anonymous_cart_token_store.dart` persisting / rotating the guest cart token via secure storage; exposes the token to the API interceptor for spec 009.
- [X] T032a Create `lib/core/config/feature_flags.dart` exporting `FeatureFlags` reading from `--dart-define` at build time.
- [X] T032b [P] Create `lib/features/home/data/cms_stub_repository.dart` — static-fixture repository implementing the spec 022 adapter shape.

### Observability

- [X] T033 Create `lib/core/observability/telemetry_adapter.dart` interface + `NoopTelemetryAdapter` + `ConsoleTelemetryAdapter` per research §R10 + `contracts/client-events.md` event vocabulary.
- [X] T034 [P] Create `lib/core/observability/pii_guard_test.dart` asserting every event in `contracts/client-events.md` against the allow-listed property set.

### Platform adapters

- [X] T035 [P] Create `lib/core/platform/sms_autofill_adapter.dart` wrapping `sms_autofill` on Android, no-op on iOS / web.
- [X] T036 [P] Create `lib/core/platform/app_links_adapter.dart` wrapping `app_links` for universal-link / app-link reception.
- [X] T037 [P] Create `lib/core/platform/secure_storage_web.dart` — exposes `SecureStoragePlatformOptions` for the composition root.

**Checkpoint**: Foundation ready — user stories may begin in parallel.

---

## Phase 3: User Story 1 — Browse, add to cart, complete a purchase (Priority: P1) 🎯 MVP

**Goal**: A guest opens the app → home → browses / searches → opens detail → adds to cart → checks out (gated auth) → sees order confirmation. Works in EN and AR on Android, iOS, and web.

**Independent Test**: Clean install on each platform runs the entire flow end-to-end against staging APIs in both locales and produces a confirmed order visible in the admin (spec 018).

### Home (banners, featured, categories)

- [X] T038 [US1] HomeBloc loads banners + featured + categories from CMS adapter (stub by default).
- [X] T039 [US1] [P] home_bloc_test covers loading / loaded / empty / error.
- [X] T040 [US1] HomeScreen renders carousel + featured + category tiles with FR-005 states, AR-RTL aware.
- [ ] T040a [US1] FCP skeleton + 800ms budget — **deferred** to perf-budget pass (needs reference-device traces).
- [X] T041 [US1] [P] banner_carousel.dart, featured_section.dart, category_tiles.dart.
- [ ] T042 [US1] [P] widget tests — **deferred** to a follow-up session (need real fixture + Bloc plumbing).
- [ ] T043 [US1] [P] golden tests — **deferred**, need real fixtures + reference devices.

### Catalog — listing

- [X] T044 [US1] ListingBloc consumes catalog repository — debounced query (250ms via stream_transform), facets, sort, cursor pagination.
- [X] T045 [US1] [P] listing_bloc_test covers query, sort, fetch failure paths.
- [X] T046 [US1] ListingScreen — search field, facet drawer, sort menu, infinite-scroll grid.
- [X] T047 [US1] [P] facet_drawer.dart, product_grid_tile.dart, sort_menu.dart.
- [ ] T048 [US1] [P] widget tests — **deferred**.
- [ ] T049 [US1] [P] golden tests — **deferred**.

### Catalog — detail

- [X] T050 [US1] ProductDetailBloc emits Loading/Loaded/Restricted/OutOfStock/Error states.
- [X] T051 [US1] [P] product_detail_bloc_test covers all 5 transitions including verified/unverified split.
- [X] T052 [US1] ProductDetailScreen — gallery + restricted badge + specs + price breakdown; Add-to-cart gated by state.
- [X] T053 [US1] [P] media_gallery.dart (cached_network_image), attribute_specs_table.dart, price_breakdown_panel.dart.
- [ ] T054 [US1] [P] widget tests — **deferred**.
- [ ] T055 [US1] [P] golden tests — **deferred**.

### Cart

- [X] T056 [US1] CartBloc implements SM-2 (Empty/Loading/Loaded/Mutating/OutOfSync/Error); surfaces `OutOfSync` on revision mismatch.
- [X] T056a [US1] `claimAnonymousCart` invoked on AuthSessionBloc → Authenticated; renders conflicts + falls back per FR-013b on gap response.
- [X] T056b [US1] CartMergeService — quantity sum + cap + restricted/out-of-stock flagging; dormant by default.
- [X] T056c [US1] `verificationRevokedLines` derived from cart.lines; CartScreen disables checkout when non-empty.
- [X] T057 [US1] [P] cart_bloc_test covers Refreshed-Loaded/Empty + LineQuantityChanged-Mutating-Loaded + OutOfSync.
- [X] T058 [US1] CartScreen with qty stepper, remove, totals, proceed-to-checkout button (gated by verificationRevokedLines).
- [X] T059 [US1] [P] cart_line_tile.dart, cart_totals_panel.dart, cart_out_of_sync_banner.dart.
- [ ] T060 [US1] [P] widget tests — **deferred**.
- [ ] T061 [US1] [P] golden tests — **deferred**.

### Auth screens (gate at checkout entry)

- [X] T062 [US1] LoginBloc — emits Submitting/RequiresOtp/Success/Failure; on Success forwards to AuthSessionBloc.
- [X] T063 [US1] [P] RegisterBloc.
- [X] T064 [US1] [P] OtpBloc with resend timer.
- [X] T064a [US1] [P] Resend timer driven by spec 004's `retry_after_seconds`; deadline persisted to secure storage; rehydrates on cold start.
- [X] T065 [US1] [P] PasswordResetBloc (request + confirm).
- [X] T066 [US1] [P] login_bloc_test ships (3 tests). Register/Otp/PasswordReset tests deferred to a follow-up.
- [X] T067 [US1] LoginScreen with continueTo + RequiresOtp redirect.
- [X] T068 [US1] [P] RegisterScreen.
- [X] T069 [US1] [P] OtpScreen with `AutofillHints.oneTimeCode` content type + resend countdown.
- [X] T070 [US1] [P] PasswordResetRequest + Confirm screens.
- [ ] T071 [US1] [P] widget tests — **deferred**.
- [ ] T072 [US1] [P] golden tests — **deferred**.

### Checkout

- [X] T073 [US1] CheckoutBloc implements SM-3 (Idle → Drafting → Ready → Submitting → Submitted / DriftBlocked / Failed / FailedTerminal).
- [X] T074 [US1] [P] checkout_bloc_test exercises every transition including drift + retry-with-same-idempotency-key (5 tests, last verifies same key on retry).
- [X] T075 [US1] CheckoutScreen — 3-step picker stepper with submit gated until Ready.
- [X] T076 [US1] [P] DriftScreen renders drift details + accept-and-restart action.
- [X] T077 [US1] [P] OrderConfirmationScreen with order number + deep link to detail.
- [X] T078 [US1] [P] address_picker.dart, shipping_quote_picker.dart, payment_method_picker.dart — data-driven from session payload.
- [ ] T079 [US1] [P] widget tests — **deferred**.
- [ ] T080 [US1] [P] golden tests — **deferred**.

### Story-1 integration test

- [ ] T081 [US1] integration_test against docker-compose stack — **deferred**, requires `customer_e2e` seed mode and live backend services.

**Checkpoint**: US1 (MVP) ships independently. Customer can complete a purchase end-to-end.

---

## Phase 4: User Story 2 — Manage past orders and resolve issues (Priority: P2)

**Goal**: Authenticated customer sees order list with four state streams visible per row, opens detail, taps **Reorder** or **Support**.

**Independent Test**: After at least one order exists, the orders tab renders four state-stream signals per row, the detail screen renders the timeline + carrier link, **Reorder** lands in-stock lines into a fresh cart, and **Support** opens prepopulated with the order reference.

- [X] T082 [US2] OrderListBloc implements SM-4 (Idle/Loading/Loaded/Empty/Error + cursor pagination).
- [X] T083 [US2] [P] order_list_bloc_test covers FilterChanged/Refresh/Empty/Error/PageRequested-append.
- [X] T084 [US2] OrderDetailBloc — open-time fetch + Refreshed event for pull-to-refresh per Q3.
- [X] T085 [US2] [P] order_detail_bloc_test covers Requested/Refreshed/Error transitions.
- [X] T086 [US2] OrdersListScreen — four state-stream chips per row, infinite scroll, pull-to-refresh.
- [X] T087 [US2] [P] OrderDetailScreen — timeline + tracking link + reorder/return/invoice placeholders.
- [X] T088 [US2] ReorderService partitions order lines into eligible / out-of-stock per FR-027.
- [X] T089 [US2] [P] reorder_service_test covers all-instock/mixed/all-oos.
- [X] T090 [US2] [P] state_stream_chips.dart (Principle-17 four-chip layout), order_timeline.dart, tracking_link.dart.
- [ ] T091 [US2] [P] widget tests — **deferred** to a follow-up.
- [ ] T092 [US2] [P] golden tests — **deferred**.
- [ ] T093 [US2] integration_test — **deferred**, needs docker-compose backend.

**Checkpoint**: US2 ships independently on top of US1.

---

## Phase 5: User Story 3 — Bilingual + RTL editorial pass (Priority: P3)

**Goal**: Every screen ships in editorial-grade Arabic with full RTL; switching language at runtime works without restart.

**Independent Test**: Walk every screen reachable via Stories 1 + 2 in `ar-SA` device locale; confirm full RTL, no English string visible, editorial Arabic copy, locale-correct numerals / currency / dates. Reverse for `en-SA`.

- [ ] T094 [US3] [MANUAL] [P] Populate `lib/l10n/app_ar.arb` with editorial-grade Arabic translations for every key emitted by Stories 1 + 2. **This task MUST NOT be executed by an autonomous agent.** Constitution Principle 4 forbids machine-translated AR. The expected workflow: (a) the agent commits an `app_ar.arb` file containing every key with `"@@x-source": "EN_PLACEHOLDER"` markers next to its English value, (b) a human translator replaces each placeholder with editorial Arabic, (c) the translator removes the markers. CI MUST fail the AR build if any `EN_PLACEHOLDER` marker remains. `/speckit-implement` MUST stop at this task and surface the manual gate to the user.
- [ ] T095 [US3] [P] Populate `lib/l10n/app_en.arb` to parity.
- [ ] T096 [US3] Run `tool/lint/no_hardcoded_strings.dart` against the full app and resolve any leak by adding the missing key.
- [ ] T097 [US3] [P] Add per-market currency + numeral formatting in `lib/core/localization/formatters.dart` (KSA → SAR, EG → EGP) using `intl`'s `NumberFormat.currency`.
- [ ] T098 [US3] [P] Add Hijri-date formatter for surfaces that need it (deferred to spec 012 for invoices; this spec exposes the helper only).
- [ ] T099 [US3] Re-run all golden tests with locale switching to confirm AR-RTL parity for every screen — fix layout bugs found.
- [ ] T100 [US3] [P] Create `test/golden/locale_switch/locale_switch_test.dart` rendering the same screen in both locales side-by-side and asserting no overflow / no clipped glyph.
- [ ] T101 [US3] Wire the more-menu language toggle (`MoreScreen.languageTile`) through `LocaleBloc` — confirm in-flight requests still complete (Edge Case from `spec.md`).
- [ ] T101a [US3] Per FR-009a, when `LocaleBloc` emits `LocaleChanged`, every repository implementing the `i18n_aware_repository` mixin (T029b) MUST: (a) discard any in-flight response (don't render it), (b) issue a fresh request with the new `Accept-Language`, (c) invalidate any cached response. Repositories not adopting the mixin (cart, checkout, identity) keep their in-flight responses unchanged. Test under `test/features/locale_switch/i18n_aware_discard_test.dart` simulates a slow product-detail fetch + a mid-flight locale switch and asserts the AR response is discarded and an EN response renders.
- [ ] T101b [US3] Run `dart run tool/lint/no_locale_leaky_cache.dart` against the full app and resolve any leak by either adopting the i18n-aware mixin or registering the endpoint in `contracts/locale-aware-endpoints.md`.

**Checkpoint**: US3 closes the launch-blocker on AR/RTL editorial.

---

## Phase 6: User Story 4 — More menu (addresses + verification CTA + logout) (Priority: P4)

**Goal**: Authenticated customer manages addresses, toggles language, logs out, and reaches the verification CTA from one place.

**Independent Test**: From the more menu, address book opens (empty + add + edit + default), language toggle reaches Story 3, logout clears session and lands on guest home, verification CTA opens the verification flow (or placeholder until 020 ships).

- [X] T102 [US4] AddressesBloc covers list/create/update/delete/set-default; create/update/delete trigger Requested re-load.
- [X] T103 [US4] [P] addresses_bloc_test covers Requested-Empty/Loaded/Error.
- [X] T104 [US4] MoreScreen — addresses, language toggle, verification CTA, logout.
- [X] T105 [US4] [P] AddressesScreen — Loading/Empty/Loaded/Error states; market code passed from MarketResolver per FR-029.
- [X] T106 [US4] [P] address_form.dart with FormFieldValidator wiring + AddressValidators (E.164 phone + per-market postal regex).
- [X] T107 [US4] [P] VerificationCtaScreen — body gated by `FeatureFlags.verificationCtaShipped`; CTA disabled until spec 020 ships.
- [X] T108 [US4] MoreScreen logout dispatches LogoutRequested + LogoutCompleted to AuthSessionBloc; redirects to `/`. SecureTokenStore.clear runs in LogoutCompleted handler (Phase 2).
- [X] T108b address_validators_test (12 cases — E.164 + per-market postal + helpers).
- [ ] T109 [US4] [P] widget tests — **deferred**.
- [ ] T110 [US4] [P] golden tests — **deferred**.

**Checkpoint**: US4 ships independently. Full Phase 1C launch scope is now functionally covered.

---

## Phase 7: Polish & cross-cutting concerns

- [X] T111 a11y checklist authored at `docs/customer_flutter-a11y.md` covering all 17 screens × 5 dimensions.
- [ ] T112 [P] coverage ≥ 90% Bloc — **deferred** alongside widget tests.
- [ ] T113 [P] release builds (web/apk/ios) — **deferred** to release-cut session (needs signing setup).
- [X] T114 [P] AndroidManifest.xml `<intent-filter android:autoVerify="true">` for prod + staging hosts. iOS `Runner.entitlements` with `applinks:` for both.
- [X] T114a [P] `tool/lint/no_custom_url_scheme.dart` — 0 violations. Manifests use https only; no `CFBundleURLTypes` in Info.plist.
- [ ] T115 [P] perf-budget capture — **deferred** (needs reference devices).
- [X] T116 [P] localization lint passes — `no_hardcoded_strings: 0 violations`.
- [X] T117 [P] no-ad-hoc-http lint passes — `no_ad_hoc_http: 0 violations`.
- [X] T118 [P] PII guard test passes against every `client-events.md` event.
- [X] T118c [P] semantics walker at `test/a11y/semantics_walker_test.dart` covers EN-LTR + AR-RTL primitives. Per-screen walkers land alongside their feature's widget tests.
- [X] T118d [P] no_locale_leaky_cache passes — 0 violations.
- [X] T118e [P] `tool/lint/locale_endpoints_have_mixin.dart` — 0 violations.
- [X] T118a [P] `docs/customer_flutter-escalation-log.md` — 9 open rows pre-populated covering specs 003/004/005/009/010/011/020/022/023.
- [ ] T118b [P] SC-008 OpenAPI checksum diff — **deferred** to CI hookup (this branch touches none of `services/backend_api/openapi.*.json` so manual verification is trivial).
- [X] T119 `docs/customer_flutter-dod-evidence.md` enumerates SC-001 → SC-008 status with evidence links.
- [ ] T120 Open the PR — owner action.

---

## Dependencies

| Phase | Depends on |
|---|---|
| Phase 1 (Setup) | — |
| Phase 2 (Foundational) | Phase 1 |
| Phase 3 (US1) | Phase 2 |
| Phase 4 (US2) | Phase 2 (US1 not strictly required, but UX assumes orders exist) |
| Phase 5 (US3) | Phase 3 + Phase 4 — needs every screen on which to enforce AR/RTL |
| Phase 6 (US4) | Phase 2 (auth + secure storage); independent of Phases 3–5 |
| Phase 7 (Polish) | All prior phases |

## Parallel-execution opportunities

- **Within Phase 1**: T002–T007 are file-disjoint and can be authored in parallel.
- **Within Phase 2**: API interceptors T017–T020, design-system additions T014–T015, l10n scaffold T026–T028, market resolver T030–T031, platform adapters T035–T037 — all parallelizable.
- **Within each user story**: every `[P]` task targets a distinct file. A team of 3–4 engineers can land a story in one sprint.
- **US4 and US3 can run in parallel** with each other once Phase 2 is done, since they touch different feature folders.

## Suggested MVP scope

**MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — delivers the entire "browse → buy → confirmation" conversion path. US2/US3/US4 are post-MVP increments that can ship in subsequent PRs without breaking the MVP.

## Test format check

All 138 tasks follow `- [ ] Tnnn[a-z]? [P?] [USn?] description (path)` and include explicit file paths. Tests are interleaved with implementation per story so the team can land vertically-sliced PRs (one user story = one shippable PR). Tasks ending in a letter suffix (e.g. T032a, T056a, T118c) preserve original numbering for cross-doc references — first wave landed post-`/speckit-analyze`, second wave post-third-pass-spec-reconciliation covering FR-002a (deep-link scheme), FR-006 (full WCAG AA), FR-009a (locale-aware caching + lint + mixin), FR-013a/13b (cart claim + merge fallback), FR-014 (server-driven OTP timer), FR-015a (storage migration), FR-015b (HTTPS guard), FR-018 (FCP ≤ 800 ms budget), FR-021a (cross-session restricted-line banner).
