# spec 014 customer_flutter — DoD evidence

> Evidence ledger for spec 014's eight success criteria (SC-001 → SC-008).
> Updated alongside the PR description; CI ratchets on the items marked
> **Automated**.

Doc owner: spec 014 implementation team
Last updated: 2026-04-27 (Phase 7 polish closure)

## SC ledger

| SC | Description | Status | Evidence |
|---|---|---|---|
| SC-001 | Reference Android, on 4G, completes the full purchase flow ≤ 4 min end-to-end | ⬜ pending | Captured by integration test T081 once docker-compose stack lands. Manual measurement on staging in the meantime. |
| SC-002 | ≥ 95% of checkout submissions complete without an un-localized error | ⬜ pending | Drift + failure paths are covered in `checkout_bloc_test.dart`; un-localized error guard sits in `no_hardcoded_strings.dart` (0 violations app-wide). Real-traffic SC measurement is owned by spec 023's analytics. |
| SC-003 | 100% of screens render in EN-LTR and AR-RTL without layout regressions | 🟡 partial | `semantics_walker_test.dart` exercises a representative primitive set in both locales. Per-screen golden tests T043/T049/T055/T061/T072/T080/T092/T110 are deferred — they need reference devices + AR translation closure (T094). |
| SC-004 | Zero hard-coded user-facing strings in features/ | ✅ | `tool/lint/no_hardcoded_strings.dart` — 0 violations. |
| SC-005 | All five client-side state machines pass `bloc_test` coverage of every documented transition | ✅ | `auth_session_bloc_test.dart` (8), `cart_bloc_test.dart` (4), `checkout_bloc_test.dart` (5 incl. drift + retry), `order_list_bloc_test.dart` (4), `locale_bloc_test.dart` (4), plus listing/detail/orders supporting Blocs. |
| SC-006 | Cold launch → interactive home: ≤ 3 s Android, ≤ 2 s iOS, ≤ 4 s web | ⬜ pending | T115 perf-budget capture on reference devices is the blocking artifact. |
| SC-007 | All audit-emitting client surfaces emit only allow-listed properties | ✅ | `pii_guard_test.dart` cross-checks `kAllowedTelemetryProps` against `contracts/client-events.md`. |
| SC-008 | Zero backend contract changes shipped from this spec | ✅ | T118b ratcheted via SHA comparison of `services/backend_api/openapi.*.json` at PR open vs. merge-base on main. The spec 014 branch only touches `apps/customer_flutter/`, `packages/design_system/`, `docs/`, and `specs/phase-1C/014-*/` — no backend OpenAPI files in the diff. |

## Static guard ledger

| Guard | Status | Evidence |
|---|---|---|
| `flutter analyze` | ✅ | 2 info-level warnings (pubspec sort) — same as scaffold baseline. |
| `flutter test` | ✅ | 81/81 tests passing as of Phase 7 closure. |
| `dart run tool/lint/no_hardcoded_strings.dart` | ✅ | 0 violations. |
| `dart run tool/lint/no_locale_leaky_cache.dart` | ✅ | 0 violations against the live registry. |
| `dart run tool/lint/no_custom_url_scheme.dart` | ✅ | 0 violations — Android manifest uses https only; iOS entitlements use `applinks:` only. |
| `dart run tool/lint/locale_endpoints_have_mixin.dart` | ✅ | 0 violations — at least one repository in `lib/features/**/data/` adopts the mixin. |
| `pubspec` doesn't introduce new tokens outside `packages/design_system` | ✅ | Tokens consumed via `package:design_system` in every feature file. |

## Constitutional locks (re-verified at Phase 7)

| Principle | Status | Evidence |
|---|---|---|
| P3 Experience model | ✅ | `core/auth/auth_session_bloc.dart` + `app/router.dart` — only `/checkout`, `/orders`, `/o/`, `/more` redirect guests through `/auth/login?continueTo=…`. |
| P4 AR/RTL editorial | 🟡 | English ARB seeded; AR ARB ships with `EN_PLACEHOLDER` markers. T094 human translator gate is the launch blocker. |
| P5 Market configuration | ✅ | `core/market/market_resolver.dart` derives KSA/EG; UI never reaches a market literal. |
| P7 Branding | ✅ | All colour tokens consumed via `packages/design_system`. |
| P22 Fixed tech | ✅ | Flutter, dio, intl, flutter_secure_storage, get_it. |
| P24 State machines | ✅ | SM-1 through SM-5 all wired + tested. |
| P28 AI-build standard | ✅ | This spec's tasks.md, FRs, success criteria all explicit. |

## Open follow-ups (not blocking the PR but tracked)

- **T094** — human translator replaces every `EN_PLACEHOLDER` marker in `app_ar.arb`. CI already fails the AR build while any marker remains.
- **Widget + golden tests** — T042/T048/T054/T060/T071/T079/T091/T109/T043/T049/T055/T061/T072/T080/T092/T110 covering every screen × state × locale × device size.
- **Integration tests** — T081 (Story 1) + T093 (Story 2) once `customer_e2e` seed mode + docker-compose stack land.
- **Perf budgets** — T115 cold-start traces on Pixel 6a + iPhone 13 + Chrome.
- **Generated OpenAPI clients** — replace every `*GapException` adapter once specs 004/005/006/009/010/011/012/013 OpenAPI clients are imported into `lib/generated/api/`.

## Sign-off

PR will reference this file in the description; the deferred items are the only delta from full DoD pass.
