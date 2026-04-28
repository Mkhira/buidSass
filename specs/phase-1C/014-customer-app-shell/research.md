# Phase 0 Research: Customer App Shell

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

This document resolves every Technical Context decision in `plan.md` to a concrete library / pattern with rationale and rejected alternatives. Phase 1 artifacts (`data-model.md`, `contracts/`, `quickstart.md`) build on these decisions.

---

## R1. State management — Bloc / `flutter_bloc`

- **Decision**: `flutter_bloc` ^8.1 for every feature folder.
- **Rationale**: ADR-002 already locked Bloc; this research only confirms version + patterns. Bloc's strict event → state contract is the most AI-agent-friendly pattern in Flutter (well-defined event vocabulary, generator-friendly `copyWith`, `bloc_test` provides a proven test harness). Sealed event / state classes via `sealed class … {}` (Dart 3) replace freezed for these simple shapes; freezed is still pulled in for view-models with deep equality.
- **Conventions**:
  - One Bloc per feature folder. Cross-feature data flows through repositories injected by the composition root, never via Bloc-to-Bloc references.
  - Events are past-tense user intents (`CartLineRemoved`); states are nouns (`CartLoading`, `CartLoaded`, `CartError`).
  - Side-effects (navigation, toasts) flow as `Stream<UiEffect>` exposed by the Bloc; widgets listen via `BlocListener`.
- **Alternatives rejected**: Riverpod (better DX but ADR-002 lock), Provider (too low-level), GetX (anti-pattern in this org), MobX (less AI-agent-legible).

## R2. Routing — `go_router`

- **Decision**: `go_router` ^14 with `Refreshable` listenable wired to the `AuthSessionBloc` so route guards re-evaluate on session change.
- **Rationale**: Built on Navigator 2.0, declarative, supports deep links + universal links + web URL strategy out-of-the-box, well-supported by the Flutter team. Auth-gated route guards are the canonical pattern for "auth required at checkout / orders / addresses / verification" (FR-013).
- **Patterns**:
  - Top-level `ShellRoute` for the bottom-nav shell; nested routes per feature.
  - `redirect:` callback reads `AuthSessionBloc.state` and pushes the unauthenticated user to `/auth/login` with a `?continueTo=<original>` query so post-login deep-link resume is one line of code.
- **Alternatives rejected**: Navigator 2.0 raw API (too verbose, no deep-link helpers), `auto_route` (codegen overhead, fewer maintainers).

## R3. HTTP + generated clients — Dio + `openapi-generator-cli`

- **Decision**: `dio` ^5 as the base client, `openapi-generator-cli` (Java-runtime, run via `package.json` script in CI) generating `dart-dio` clients per Phase 1B service into `lib/generated/api/<service>/`. Generated dir is gitignored; CI regenerates on every PR.
- **Rationale**: One generator, one Dio interceptor stack, one error model. FR-033 (no ad-hoc HTTP) is enforced by an analyzer rule that fails the build on any direct `Uri.parse(http...)` outside `core/api/`. Each Phase 1B service ships an `openapi.<service>.json` (already present at `services/backend_api/openapi.{catalog,checkout,identity,inventory,invoices,orders,pricing,returns,search}.json`) so the generator has a stable input.
- **Interceptors** (composed in `core/api/`):
  1. `AuthInterceptor` — attaches `Authorization: Bearer <access>` from secure storage; on 401 + valid `refresh`, refreshes and retries once.
  2. `CorrelationIdInterceptor` — propagates a per-request UUID to the backend (matches spec 003 conventions).
  3. `LocaleAndMarketInterceptor` — sends `Accept-Language` and `X-Market-Code` headers from `LocaleBloc` + `MarketResolver`.
  4. `IdempotencyInterceptor` — used by the checkout flow only; injects `Idempotency-Key` from the active `CheckoutBloc` state.
- **Alternatives rejected**: hand-written services (drift risk), `chopper` (less popular, fewer codegen options for OpenAPI), `retrofit_generator` (annotation-driven, no OpenAPI source).

## R4. Localization, RTL, numerals — `intl` + `flutter_localizations` + ARB + `hijri`

- **Decision**:
  - Source of truth: `lib/l10n/app_en.arb` and `app_ar.arb`. Generated `AppLocalizations` class consumed by every screen.
  - `Directionality(textDirection: TextDirection.rtl)` is wrapped by `MaterialApp.builder` when `LocaleBloc.state.locale.languageCode == 'ar'`.
  - Numerals: per-locale via `intl`'s `NumberFormat`; for AR we use **Western Arabic numerals** (0123456789) by default — Eastern Arabic numerals (٠١٢…) deferred to a separate copy decision (cited in Assumptions).
  - Currency: per-market via `intl`'s `NumberFormat.currency(locale: …, symbol: <SAR|EGP>)`.
  - Dates: `intl`'s Gregorian formatters by default; Hijri parallel rendering for tax-invoice download dates is owned by spec 012, not here.
- **Rationale**: Built into Flutter's localization story. ARB enables the editorial AR pass (translators work in a shared format) without code changes.
- **Lint**: `tool/lint/no_hardcoded_strings.dart` walks the AST of every file under `lib/features/**/screens/**` and `lib/features/**/widgets/**` and fails on any `String` literal passed to `Text(...)`, `SnackBar(content: ...)`, `AlertDialog(title: ..., content: ...)`, or any `*tooltip*` parameter. Allowlist file documents constants like SKU column headers that are intentionally locale-neutral (none expected for v1).
- **Alternatives rejected**: `easy_localization` (re-implements stdlib without enough wins), `slang` (codegen but less common, smaller community).

## R5. Secure storage — `flutter_secure_storage`

- **Decision**: `flutter_secure_storage` ^9 with platform-specific backends auto-selected.
  - iOS: Keychain, accessibility `first_unlock_this_device`.
  - Android: EncryptedSharedPreferences (AES-256-GCM, master key via Android Keystore).
  - Web: an `EncryptedLocalStorage` adapter using `webcrypto` (`AES-GCM` with a key derived from a session-stable browser fingerprint via PBKDF2). Web has no real keychain — this is best-effort, and the threat model (Constitution Principle 27 + Q4) accepts this trade-off because evergreen browsers are not used as B2B endpoints in the Phase 1C scope.
- **Stored secrets**: refresh token, access token, anonymous cart token, language preference, market code.
- **Rotation**: on every refresh-token use (per spec 004 FR-012). On logout, all keys are wiped via `deleteAll()`.
- **Alternatives rejected**: `shared_preferences` (plaintext), `hive` (third-party encryption, no Keychain), `sqflite` (overkill, no encryption out-of-the-box).

## R6. Test stack — `bloc_test`, `mocktail`, `golden_toolkit`, `integration_test`

- **Decision**:
  - **Bloc unit tests** (`test/bloc/`): `bloc_test` ^9. One file per Bloc; every state transition documented in `data-model.md` has a matching `blocTest`.
  - **Widget tests** (`test/widget/`): `flutter_test` + `mocktail` for repository fakes. One file per screen with one test per state (loading / empty / error / success / restricted / payment-failure-recovery).
  - **Golden tests** (`test/golden/`): `golden_toolkit` ^0.15. Each screen × {AR-RTL, EN-LTR} × {Pixel 6a, iPhone 13, web 1280×800}. Total target: ~528 goldens across 22 screens × 2 locales × 3 devices × ~4 states. Goldens live in `test/golden/baselines/` and are committed.
  - **Integration tests** (`integration_test/`): one Story-1 happy-path test per platform (Android emulator, iOS simulator, Chrome) running against a `docker compose` stack of backend services seeded by spec 003's seeder. Story 2 reorder flow is covered too.
  - **Localization lint**: `tool/lint/no_hardcoded_strings.dart` runs as a `dart_test` so it lives in CI alongside the test suite.
- **Rationale**: SC-003 (100% screens render in both locales) is **only** mechanically verifiable via golden diffs at this scale; review alone misses regressions. SC-002 (95% checkout completion without un-localized error) is verified by widget tests covering every error state in both locales.
- **Alternatives rejected**: `patrol` for integration (heavier setup; overkill for Phase 1C scope); `screenshot_tests` (Stripe's lib, not Flutter-native enough).

## R7. SMS / email auto-fill for OTP

- **Decision**:
  - **Android**: `sms_autofill` ^2 — uses the SMS Retriever API (no SMS read permission required). The OTP message includes the app's hash so the OS auto-extracts the code. Spec 004 must include the Android app hash in the OTP SMS body — a small ask escalated to spec 004 if missing.
  - **iOS**: platform-native `TextField(textContentType: TextInputType.text)` with `oneTimeCode` autofill — works zero-config on iOS 12+.
  - **Web**: WebAuthn / OTP Credential API (`navigator.credentials.get({otp: {transport: ['sms']}})`) where supported (Chromium-based browsers); falls back to manual entry on Safari / Firefox.
  - **Email**: no auto-fill — clipboard paste via the standard text-field contextmenu only.
- **Rationale**: Q5 chose SMS + email; auto-fill is a hard UX win on Android specifically. Email magic-link auto-detection from the browser is not standardised — manual paste is acceptable.
- **Alternatives rejected**: reading SMS via `READ_SMS` permission (Play Store policy violation); custom SMS parser (brittle, breaks the moment 004 changes copy).

## R8. Deep linking — `app_links` only (Firebase Dynamic Links rejected)

- **Decision**:
  - **`app_links` ^6** is the universal-link / app-link receiver on Android + iOS + web. **No Firebase Dynamic Links.** FDL is sunsetting per Google's deprecation notice; integrating it now would mean a tear-out within v1's lifetime.
  - Cross-install survival ("share a product link before app is installed → link still works after store install") is **deferred** to a later spec; v1 ships with universal/app links only and acknowledges the install-time gap as a known limitation in `contracts/deeplink-routes.md`.
- **Routes** (full table in `contracts/deeplink-routes.md`):
  - `/p/<productId>` → product detail
  - `/c/<categoryId>` → category listing
  - `/o/<orderId>` → order detail (auth required)
  - `/auth/reset?token=…` → password reset confirm
  - `/auth/verify?token=…` → email verification
  - `/cart` → cart
- **Auth-gated routes**: `go_router` `redirect:` enforces; the original deep link is preserved as `?continueTo=<encoded>` so post-login resume is automatic.
- **Alternatives rejected**: `uni_links` (deprecated), branch.io (heavier, not needed for the Phase 1C launch scope).

## R9. Image caching — `cached_network_image`

- **Decision**: `cached_network_image` ^3 for product media gallery, banners, and category tiles. Disk cache TTL: 7 days; memory cache: default (1000 entries).
- **Rationale**: Industry-standard for Flutter. CDN URLs are stable per spec 005 product-media response.
- **Alternatives rejected**: `flutter_cache_manager` directly (lower level, no `Image` widget integration), bundling images in the app (impossible — content is dynamic).

## R10. Analytics + telemetry adapter (deferred adapter only)

- **Decision**: Define a `TelemetryAdapter` interface in `core/observability/` with two implementations:
  - `NoopTelemetryAdapter` — default, ships in v1.
  - `ConsoleTelemetryAdapter` — Dev-only, prints events to logs.
- A real provider (Mixpanel / Amplitude / Application Insights) is **not** chosen here — that's the notifications / observability spec's call. The adapter exists so wiring telemetry later is one composition-root swap, not a feature-folder rewrite.
- **Events emitted in v1**: `app.cold_start`, `auth.login.success`, `auth.login.failure`, `cart.add`, `checkout.start`, `checkout.submit.success`, `checkout.submit.failure`, `order.detail.opened`, `order.reorder.tapped`, `language.toggled` — all enumerated in `contracts/client-events.md`.
- **Alternatives rejected**: pulling in Mixpanel SDK now (premature — provider not yet decided), no adapter at all (forces a rewrite later).

## R11. CI integration

- **Decision**: A new workflow `.github/workflows/customer_flutter-ci.yml` runs on PRs that touch `apps/customer_flutter/**` or `packages/design_system/**`. Steps:
  1. `flutter pub get`
  2. `dart run tool/lint/no_hardcoded_strings.dart`
  3. `flutter analyze --fatal-infos`
  4. `flutter test --coverage`
  5. `flutter test test/golden` with `--update-goldens=false` (golden diff fails the build)
  6. `flutter build web --release` and `flutter build apk --debug` (smoke build to catch tree-shaking / web compilation issues)
- Integration tests run on a separate scheduled workflow (slower; not on every PR).
- **Rationale**: Mirrors the `lint-format`, `build`, and `seed-pii-guard` jobs already established in the backend CI. The advisory `impeccable-scan` mentioned in CLAUDE.md is **not** wired here — it targets `apps/admin_web` only per the design-agent-skills doc.

## R12. Bootstrap / first-run UX

- **Decision**:
  - On first launch, `MarketResolver` reads device locale and picks a default market (KSA for `*-SA`, EG for `*-EG`, KSA otherwise per the Assumptions section).
  - `LocaleBloc` initializes from device locale; user can toggle from `more` menu (FR-030).
  - No onboarding / splash beyond the standard launch screen — kept lean per Constitution Principle 27 ("simplicity for consumers").
- **Alternatives rejected**: an explicit market-picker on first launch (adds friction; data shows ≥ 90% of users come in via locale-correct device anyway).

---

## Open follow-ups for downstream specs

- **Spec 004**: confirm the Android OTP-SMS body includes the app hash for `sms_autofill`. Filed against spec 004 if missing; not blocking this spec.
- **Spec 022 (CMS)**: home banners + featured sections rely on a stub today (per Assumptions). When 022 ships, the home Bloc swaps the stub repository for the real CMS client — no UI change required.
- **Spec 020 (verification)**: verification CTA is a placeholder until 020 ships; feature flag in `more` menu controls whether the CTA is visible (default: visible with placeholder body).
- **Spec 023 (notifications)**: live order push is owned there; this spec only does pull-to-refresh + open-time fetch.
