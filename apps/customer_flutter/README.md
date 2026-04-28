# customer_flutter

Flutter customer app for the Dental Commerce Platform — spec [014-customer-app-shell](../../specs/phase-1C/014-customer-app-shell/spec.md).

Targets Android (API 24+), iOS (14+), and evergreen desktop browsers per spec FR-001.
State management is Bloc / `flutter_bloc` per ADR-002. UI consumes design-system
tokens from `packages/design_system` per Constitution Principle 7.

## Prerequisites

| Tool | Minimum |
|---|---|
| Flutter SDK | 3.24 (stable channel) |
| Dart | 3.5 (bundled with Flutter 3.24) |
| Xcode | 15.x (iOS only; macOS host required) |
| Android Studio + SDK | API 24 platform installed |
| Docker Desktop | 4.30+ (local Phase 1B backend stack) |
| Java | 17 (required by `openapi-generator-cli`) |

## First-time setup

```bash
cd apps/customer_flutter
flutter pub get

# OpenAPI clients land under lib/generated/ via build_runner (T004 setup
# in Phase 2). Until then the app builds without generated clients.

flutter analyze
flutter test
```

## Run against local backend

```bash
cd ../../  # repo root
docker compose --profile customer up -d
curl -fsS http://localhost:5000/health  # spec 003 health endpoint

cd apps/customer_flutter
flutter run -d chrome           # web
flutter run -d "iPhone 13"      # iOS simulator
flutter run -d emulator-5554    # Android emulator
```

Configurable via build flags:

| Flag | Default | Purpose |
|---|---|---|
| `--dart-define=API_BASE_URL=…` | `http://localhost:5000` | Phase 1B backend base URL |
| `--dart-define=ALLOW_INSECURE_BACKEND=1` | on for `flutter run` debug | Per FR-015b — release builds enforce HTTPS |

## Constitutional locks

- **ADR-002 (Bloc)**: no Riverpod / Provider / GetX — `flutter_bloc` only.
- **Principle 7 (palette)**: tokens from `packages/design_system` only.
- **Principle 4 (AR + RTL)**: every screen ships in both languages with
  full RTL. AR copy is editorial-grade — never machine-translated.
  `tool/lint/no_hardcoded_strings.dart` enforces (FR-008).
- **FR-031 (UI-only spec)**: no backend code in this app's PRs. Backend
  gaps surface as issues against the owning Phase 1B spec.
- **FR-033 (no ad-hoc HTTP)**: every API call goes through generated
  clients in `lib/generated/api/`.

## Module layout (Phase 2 onwards)

```
lib/
├── main.dart                     # boot DI + run app
├── app/                          # shell (router, theme, DI)
├── features/                     # one folder per user story
│   ├── auth/
│   ├── home/
│   ├── catalog/
│   ├── cart/
│   ├── checkout/
│   ├── orders/
│   └── more/
├── core/                         # cross-cutting infra
│   ├── api/                      # Dio + interceptors
│   ├── auth/                     # session + secure storage
│   ├── localization/             # Bloc + ARB loaders
│   ├── market/                   # market resolver
│   ├── platform/                 # iOS/Android/web adapters
│   └── observability/            # telemetry adapter
├── generated/api/                # generated OpenAPI clients (gitignored)
└── l10n/
    ├── app_en.arb
    └── app_ar.arb
```

## Tests

```bash
flutter analyze --fatal-infos    # static analysis + linter
flutter test                     # bloc + widget tests
flutter test test/golden         # AR-RTL × EN-LTR golden snapshots
flutter test integration_test/   # E2E flows (slower; not on every PR)
```

## Known limitations on first launch

- **CMS-driven home content**: stub backed by curated catalog content
  until spec 022 ships its CMS (014 Assumptions).
- **Verification CTA**: placeholder until spec 020 ships.
- **Live order push**: pull-to-refresh + open-time fetch only —
  push notifications are owned by spec 023.
- **B2B UI**: deferred to spec 021.
