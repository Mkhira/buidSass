# Implementation Plan — Phase 1: Auth & Identity (Foundation)

> Companion to [`spec.md`](./spec.md). Drives `/speckit-implement` execution.

## Module layout

```
apps/customer_flutter/lib/
├── core/
│   ├── api/
│   │   ├── api_module.dart                 # registers per-alias Dio instances
│   │   ├── dio_factory.dart                # builds Dio per ApiAlias enum
│   │   ├── auth_interceptor.dart           # bearer + single-attempt refresh
│   │   ├── locale_market_interceptor.dart  # Accept-Language + X-Market-Code
│   │   ├── correlation_id_interceptor.dart # X-Correlation-Id
│   │   ├── idempotency_interceptor.dart    # Idempotency-Key (opt-in)
│   │   └── i18n_aware_repository.dart      # locale helper base
│   ├── error/
│   │   ├── error_mapper.dart               # DioException → Failure
│   │   └── failure.dart                    # sealed Failure hierarchy
│   ├── session/
│   │   ├── session_store.dart              # secure storage + Stream<SessionState>
│   │   └── session_state.dart              # sealed SessionState
│   ├── theme/
│   │   ├── colors.dart                     # brand palette (Principle 7)
│   │   ├── app_theme.dart                  # ThemeData
│   │   └── text_directionality.dart        # picks rtl/ltr
│   ├── router/
│   │   ├── app_router.dart                 # go_router
│   │   └── redirect_guard.dart             # SessionStore-driven redirects
│   └── widgets/
│       ├── error_banner.dart               # localized + correlation-id
│       ├── empty_state.dart                # reusable empty illustration + CTA
│       ├── loading_skeleton.dart           # shimmer
│       └── conflict_dialog.dart            # 409 drift dialog (used by Phase 4+)
└── features/
    └── identity/
        ├── data/
        │   ├── identity_gateway.dart       # interface
        │   ├── identity_gateway_impl.dart  # Dio-backed impl
        │   └── models/                     # request/response DTOs
        ├── bloc/
        │   ├── splash_bloc.dart
        │   ├── login_bloc.dart
        │   ├── register_bloc.dart
        │   ├── otp_bloc.dart
        │   ├── password_reset_request_bloc.dart
        │   ├── password_reset_complete_bloc.dart
        │   ├── email_confirm_bloc.dart
        │   ├── locale_settings_bloc.dart
        │   ├── account_security_bloc.dart
        │   ├── sign_out_cubit.dart
        │   ├── sessions_bloc.dart
        │   └── more_hub_cubit.dart
        └── screens/
            ├── splash_screen.dart
            ├── login_screen.dart          # existing — verify
            ├── register_screen.dart       # existing — verify
            ├── otp_screen.dart            # existing — verify
            ├── password_reset_screen.dart # existing — verify
            ├── email_confirm_screen.dart
            ├── locale_settings_screen.dart
            ├── account_security_screen.dart
            ├── more_screen.dart           # existing in features/more — relocate to features/identity OR keep cross-feature; decide in T-1.5
            └── sessions_screen.dart
```

> **Note on layout:** `apps/customer_flutter/lib/features/auth/` already exists. Phase 1 consolidates auth + identity surfaces under `features/identity/`. Migration is one rename + import-path sweep handled in T-1.4.

## Bloc structure (uniform per-Bloc shape)

Every Bloc in this phase follows the same shape:

```dart
sealed class XxxState extends Equatable { /* ... */ }
final class XxxInitial extends XxxState {}
final class XxxLoading extends XxxState {}
final class XxxLoaded<T> extends XxxState { final T data; /* ... */ }
final class XxxEmpty extends XxxState {}
final class XxxFailure extends XxxState { final FailureKind kind; final String message; final String correlationId; /* ... */ }

sealed class XxxEvent extends Equatable {}

final class XxxBloc extends Bloc<XxxEvent, XxxState> {
  XxxBloc({required this.gateway, required this.sessionStore}) : super(const XxxInitial()) {
    on<XxxStarted>(_onStarted);
    /* ... */
  }
  /* ... */
}
```

Rules:
- No `setState`. No mutable mutable state outside the Bloc.
- `Equatable` on every state and event.
- `Failure` from `core/error/failure.dart` is the only error type a Bloc emits.
- Constructor takes interfaces (e.g., `IdentityGateway`, `SessionStore`), never concrete impls.

## Routing (go_router)

```
/                                 → SplashScreen           (Unknown only)
/login                            → LoginScreen            (Anonymous only)
/register                         → RegisterScreen         (Anonymous only)
/otp?challengeId=…&intent=…       → OtpScreen              (Anonymous + post-step-up Authenticated)
/password-reset                   → PasswordResetRequestScreen (Anonymous)
/password-reset/complete?…        → PasswordResetCompleteScreen (Anonymous)
/email-confirm?token=…            → EmailConfirmScreen     (any)
─── shell with bottom nav ───
/home                             → HomeScreen (Phase 2)
/categories                       → CategoriesScreen (Phase 2)
/cart                             → CartScreen (Phase 4 — existing)
/orders                           → OrdersListScreen (Phase 5 — existing)
/more                             → MoreScreen
   /more/security                 → AccountSecurityScreen
   /more/locale                   → LocaleSettingsScreen
   /more/sessions                 → SessionsScreen
   /more/verification             → (Phase 7 placeholder)
   /more/reviews                  → (Phase 7 placeholder)
   /more/company                  → (Phase 8 placeholder)
```

Redirect guard rules (centralized):
- `SessionState.unknown` ⇒ any request route stashed as `redirectTo`, navigate to `/`.
- `SessionState.anonymous` ⇒ any non-public route stashed as `redirectTo`, navigate to `/login`.
- `SessionState.authenticated` ⇒ public routes redirect to `/home`.

## Build sequence (T-1.x tasks)

See [`tasks.md`](./tasks.md) for the ordered task list. Summary order:

1. **Foundation** (T-1.1 – T-1.6): error types, session store, Dio factory + interceptors, theme, router, identity gateway. Block on completion.
2. **Splash** (T-1.7): consume foundation; first end-to-end exercise of refresh + me.
3. **Login + Register + OTP** (T-1.8 – T-1.10): existing screens, verify + retrofit to Bloc shape + foundation interceptors.
4. **Password reset** (T-1.11 – T-1.12): split existing single screen into request + complete sub-routes.
5. **Email confirm** (T-1.13): new screen + deep-link wiring.
6. **More hub + Account Security + Locale + Sessions** (T-1.14 – T-1.17): More tab destinations.
7. **Phase exit** (T-1.18): run flutter analyze + flutter test, update overview doc §8 status row.

## Testing strategy

- **Unit (Bloc):** every Bloc class has tests for: initial state, happy path, each failure branch (401/403/409/422/429/5xx/network/offline). Use `bloc_test` package.
- **Repo:** `IdentityGatewayImpl` unit tests stub `Dio` with `MockClient` and assert request payloads (headers, body, idempotency key).
- **Widget:** every screen has tests for each UI state (initial/loading/loaded/empty/error variants/offline) in both `Locale('ar')` and `Locale('en')`.
- **Integration:** one end-to-end test driving Splash → Login → Home using mocked gateway. Locale switch test in More.
- **Golden:** brand-palette check on Login, More hub, Sessions (sample of screens). Run on CI under both locales.

## Coverage targets

- Bloc: ≥ 90% line coverage.
- Screens: smoke (renders without error) for every screen × every state × both locales.
- Overall feature: ≥ 80% line coverage.

## Risks specific to this phase

| # | Risk | Mitigation |
|---|---|---|
| 1 | Existing auth screens may not use Bloc consistently. | T-1.8 audits each existing screen and migrates to the per-Bloc shape above. |
| 2 | Splash race with deep-link cold start. | `SessionStore.stateStream` emits `Unknown` until first refresh resolves; router waits for non-Unknown before honoring `redirectTo`. |
| 3 | OTP expiry recompute on app resume drifts if wall clock is used. | `OtpBloc` persists the server-supplied `expiresAt` (UTC) and recomputes remaining time from `DateTime.now().toUtc()` on each resume. |
| 4 | `MaterialApp` rebuild on locale change drops Bloc state. | Put `BlocProvider` instances above `MaterialApp` so they survive rebuilds. Already standard in `app_module.dart`. |
| 5 | Refresh-token storage on Android prior to API 23 lacks Keystore. | `flutter_secure_storage` falls back to encrypted shared prefs; document the gap. Acceptable for Phase 1. |

## Dependencies on prior work

- Identity backend (spec 004 — `phase-1B/004-identity-and-access`) is live.
- Design tokens for the brand palette already exist in `packages/design_system/` (verify before T-1.3).
- `go_router`, `flutter_bloc`, `flutter_secure_storage`, `dio`, `equatable`, `bloc_test` are already in `apps/customer_flutter/pubspec.yaml`. Verify versions.

## Definition of Done (phase exit gate)

- All §7 acceptance criteria in `spec.md` are checked.
- `tasks.md` shows every T-1.x task as Done.
- Code review approved by mobile and identity owners.
- `flutter analyze` and `flutter test` both green in CI.
- Smoke test recorded in `quickstart.md` executed manually on iOS + Android.
- `docs/mobile-app-screen-api-plan.md` §8 status row for Phase 1 is updated.
