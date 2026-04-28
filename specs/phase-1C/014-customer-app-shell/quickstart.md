# Quickstart: Customer App Shell

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

This is the local-dev onboarding for `apps/customer_flutter/`. The app does not exist yet — it lands when `/speckit-tasks` decomposes the work. This quickstart is the contract for what "ready to develop" looks like.

---

## Prerequisites

| Tool | Minimum | Notes |
|---|---|---|
| Flutter SDK | 3.24 stable | `flutter --version` |
| Dart | 3.5 (bundled with Flutter 3.24) | |
| Xcode | 15.x | iOS only; macOS host required |
| Android Studio + SDK | API 24 platform installed | |
| Docker Desktop | 4.30+ | For local Phase 1B backend stack |
| Node.js | 20.x | For `openapi-generator-cli` (Java + Node wrapper) |
| Java | 17 | Required by `openapi-generator-cli` |

## First-time setup

```bash
# From repo root
cd apps/customer_flutter

# Install Flutter dependencies
flutter pub get

# Generate OpenAPI clients into lib/generated/api/
dart run build_runner build --delete-conflicting-outputs
# OR (whichever wrapper script ships):
npm run gen:api

# Sanity check — should pass clean on a fresh checkout
flutter analyze --fatal-infos
flutter test
```

## Run against local backend

```bash
# In one terminal — bring up the Phase 1B backend stack
cd <repo-root>
docker compose --profile customer up -d

# Verify
curl -fsS http://localhost:5000/health   # spec 003 health endpoint
```

Pick a target:

```bash
cd apps/customer_flutter

# Android emulator (adb device must be running)
flutter run -d emulator-5554

# iOS simulator
flutter run -d "iPhone 13"

# Web (Chrome)
flutter run -d chrome
```

Backend base URL is read from `--dart-define=API_BASE_URL=…`. Defaults baked in:

| Env | URL |
|---|---|
| Dev (default) | `http://localhost:5000` |
| Staging | `https://api.staging.dental-commerce.com` |
| Prod | `https://api.dental-commerce.com` |

## Tests

```bash
# Bloc unit tests + widget tests
flutter test

# Golden tests (AR-RTL + EN-LTR, three reference devices)
flutter test test/golden

# Update goldens after an intentional UI change
flutter test test/golden --update-goldens

# Localization lint (FR-008)
dart run tool/lint/no_hardcoded_strings.dart

# Integration tests (slower; runs Story 1 happy path E2E)
flutter test integration_test/story1_purchase_flow_test.dart -d chrome
flutter test integration_test/story1_purchase_flow_test.dart -d emulator-5554
flutter test integration_test/story1_purchase_flow_test.dart -d "iPhone 13"
```

## Story-level smoke acceptance

After setup, walk these flows manually before opening a PR:

1. **Story 1 (P1) — Browse → buy**: launch app → open a category → tap a product → **Add to cart** → tap **Proceed to checkout** → register or login → complete checkout → see confirmation. Run once in EN, once in AR.
2. **Story 2 (P2) — Orders**: open orders list → confirm four state-stream signals visible → open detail → tap **Reorder** → confirm a fresh cart is built.
3. **Story 3 (P3) — Locale toggle**: from more menu, toggle AR ↔ EN; confirm RTL flips and no English string remains in AR.
4. **Story 4 (P4) — More menu**: add an address → set as default → log out → land on guest home.

## CI pipeline (summary)

`.github/workflows/customer_flutter-ci.yml` runs on PRs touching `apps/customer_flutter/**` or `packages/design_system/**`:

1. `flutter pub get`
2. `dart run tool/lint/no_hardcoded_strings.dart`
3. `flutter analyze --fatal-infos`
4. `flutter test --coverage`
5. `flutter test test/golden`
6. `flutter build web --release` + `flutter build apk --debug` (smoke)

Integration tests run on a separate scheduled workflow (slower; not on every PR).

## Known limitations on first launch

- **CMS-driven home content**: stub until spec 022 ships (per Assumptions in `spec.md`).
- **Verification CTA**: placeholder until spec 020 ships.
- **Live order push**: not in scope here — owned by spec 023.
