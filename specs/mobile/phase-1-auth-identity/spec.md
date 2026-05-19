# Spec — Phase 1: Customer Mobile Auth & Identity (Foundation + Identity Screens)

> **Phase:** 1 of 8 (customer mobile) · **Owner:** mobile + identity · **Last updated:** 2026-05-19
> **OpenAPI source:** [`services/backend_api/openapi.identity.json`](../../../services/backend_api/openapi.identity.json)
> **Endpoint count:** 14 customer-tagged ops · **Screen count:** 10 user-facing + 1 hub
> **Index:** [`docs/mobile-app-screen-api-plan.md`](../../../docs/mobile-app-screen-api-plan.md)

---

## 1. Goal

Deliver the customer mobile app's authentication and identity surface end-to-end, plus the **foundation infrastructure** (Dio HTTP client, interceptors, gateway-repo pattern, error mapper, design tokens, theming, routing, session storage) that every later phase depends on.

After Phase 1:
- A new install can register, sign in, verify OTP, reset its password, confirm its email, manage sessions, and switch locale — in AR and EN, on both `SA` and `EG` markets.
- Every later phase can call `lib/features/<area>/data/<area>_gateway.dart` without re-implementing transport, error mapping, or auth handling.

## 2. User roles

| Role | Description | Endpoints scope in Phase 1 |
|---|---|---|
| Unauthenticated visitor | Has not signed in. May browse catalog (Phase 2) freely but cannot complete protected actions. | sign-in, register, otp/request, otp/verify, password/reset-request, password/reset-complete, email/confirm |
| Authenticated customer | Signed in with valid `accessToken`. | session/refresh, me, locale (PATCH), password/change, sign-out, sessions (list + revoke) |
| B2B buyer / approver (multi-user company) | Signed in customer who additionally belongs to a company. Same identity surface; company context resolves in Phase 8. | identical to authenticated customer |

> Admin and admin MFA endpoints (`/v1/admin/identity/*`) are explicitly out of scope — they live in the admin web app per ADR-006.

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Bilingual everywhere. Every label, error message, and copy block exists in AR and EN. AR is editorial-grade, not machine-translated. | Principle 4 |
| BR-2 | RTL mirroring on every AR screen. Icons that flip (back arrow, chevron, swipe affordances) mirror; brand and content icons do not. | Principle 4 |
| BR-3 | Brand palette only. Primary `#1F6F5F`, secondary `#2FA084`, accent `#6FCF97`, neutral `#EEEEEE`. Semantic colors for success/warning/error/info follow design system tokens. | Principle 7 |
| BR-4 | Market-aware. `X-Market-Code` (`SA` or `EG`) is sent on every request and influences server-side OTP delivery (Unifonic vs Vodafone Egypt) and locale fallback. | Principle 5, ADR-009 |
| BR-5 | One refresh attempt per 401. On 2nd consecutive 401, sign out and route to Login with the originating route preserved in a `redirectTo` query param. | Principle 24 |
| BR-6 | Bearer never sent over HTTP outside `kDebugMode`. | Principle 25 |
| BR-7 | OTP, password reset, and email confirmation are all rate-limited server-side; UI surfaces the cooldown timer where present in the error payload (`error.details.retryAfterSeconds`). | Principle 24 |
| BR-8 | Locale changes persist immediately on success (PATCH locale → restart Bloc → reload `MaterialApp` locale) and are also applied to the access token's downstream `Accept-Language`. | Principle 4 |
| BR-9 | Sessions list reflects server truth. Revoking the current session forces sign-out. Revoking another session shows it as "Revoked just now" without re-fetch (optimistic update + retry on conflict). | Principle 27 |
| BR-10 | Password change requires current password. After success, all other sessions are invalidated by the server; the current session keeps the new access token. UI shows a confirmation toast. | Principle 24 |
| BR-11 | Correlation-id surfaced in every error toast or banner (last 8 chars displayed; full id copyable). | Principle 25 |
| BR-12 | Email confirmation deep link opens the app, validates the token, and routes to the home tab on success. If the user is unauthenticated, the success state shows a "Sign in to continue" CTA. | Principle 27 |

## 4. Foundation work (non-screen but in scope)

These deliverables are produced under `apps/customer_flutter/lib/core/` and `apps/customer_flutter/lib/features/identity/data/`. They are reused by every later phase.

### 4.1 HTTP layer

Reuses existing files; this spec verifies them, completes them, and adds missing pieces.

| File | Purpose | Phase 1 work |
|---|---|---|
| `core/api/dio_factory.dart` | Constructs base `Dio` per alias (`IDN`, `CAT`, …). Sets baseUrl, timeouts, default headers. | **Verify** existing; add per-alias factory entries for `IDN` (other aliases delivered in later phases). |
| `core/api/auth_interceptor.dart` | Attaches `Authorization: Bearer <accessToken>`. Handles single-attempt refresh on 401. Falls back to sign-out on 2nd 401. | **Verify** retry-and-clear policy. Add `redirectTo` preservation on forced sign-out. |
| `core/api/locale_market_interceptor.dart` | Attaches `Accept-Language` and `X-Market-Code`. | **Verify** uses session store as the source of truth, not the device locale. |
| `core/api/correlation_id_interceptor.dart` | Generates UUIDv4 per request, attaches `X-Correlation-Id`, mirrors it into error log records. | **Verify** copies id into `DioException.requestOptions` so `ErrorMapper` can surface it. |
| `core/api/idempotency_interceptor.dart` | Attaches `Idempotency-Key` to opted-in unsafe ops only (see matrix in `docs/mobile-app-screen-api-plan.md` §6). | **Verify** key is bound to one user intent (see BR in §3 — one intent ⇒ one key). |
| `core/api/i18n_aware_repository.dart` | Helper base class so repos can read current locale without injection. | **Verify** existing. |
| `core/error/error_mapper.dart` | **New.** Maps `DioException` → typed `Failure` sealed class. Pulls `error.code`, `error.message`, `error.correlationId`, `error.details` from response body. Fallbacks for network/timeout/connection errors. | **Create.** |
| `core/error/failure.dart` | **New.** Sealed `Failure` class hierarchy: `ValidationFailure`, `AuthFailure`, `ForbiddenFailure`, `ConflictFailure`, `ServerFailure`, `NetworkFailure`. | **Create.** |

### 4.2 Session store

| File | Purpose | Phase 1 work |
|---|---|---|
| `core/session/session_store.dart` | Persists access + refresh tokens, profile snapshot, locale (`ar`/`en`), market (`SA`/`EG`). Backed by `flutter_secure_storage`. Exposes `Stream<SessionState>` for app-level routing. | **Create or verify**; this is the single source of truth for the auth interceptor and Bloc layer. |
| `core/session/session_state.dart` | Sealed: `Unknown`, `Anonymous`, `Authenticated(profile)`. Drives the root router. | **Create.** |

### 4.3 Theme & design tokens

| File | Purpose | Phase 1 work |
|---|---|---|
| `core/theme/colors.dart` | Brand palette constants per Principle 7. | **Create or verify.** |
| `core/theme/app_theme.dart` | Builds `ThemeData` for AR + EN with palette applied. Defines text styles, button shapes, input decorations. | **Create or verify.** |
| `core/theme/text_directionality.dart` | Picks `TextDirection.rtl` when locale is `ar`. | **Create or verify.** |

### 4.4 Routing

| File | Purpose | Phase 1 work |
|---|---|---|
| `core/router/app_router.dart` | `go_router` configuration. Defines public routes (login, register, otp, password-reset, email-confirm, splash) and protected shell with bottom-nav (home, categories, cart, orders, more). Reads `SessionStore` for redirect logic. | **Create or verify.** |
| `core/router/redirect_guard.dart` | Centralizes `Unknown → Splash`, `Anonymous → Login (preserving redirectTo)`, `Authenticated → requested route`. | **Create.** |

### 4.5 Identity gateway (shared by every screen in this phase)

`lib/features/identity/data/identity_gateway.dart` — typed wrapper around 14 endpoints. Every screen Bloc calls the gateway, never raw Dio.

```dart
abstract class IdentityGateway {
  Future<SessionResponse> register(RegisterRequest req);
  Future<SessionResponse> signIn(SignInRequest req);
  Future<void> signOut();
  Future<SessionResponse> refreshSession(RefreshRequest req);
  Future<MeResponse> me();
  Future<void> setLocale(SetLocaleRequest req);            // PATCH /locale
  Future<void> requestOtp(RequestOtpRequest req);
  Future<SessionResponse> verifyOtp(VerifyOtpRequest req);
  Future<void> requestPasswordReset(RequestPasswordResetRequest req);
  Future<void> completePasswordReset(CompletePasswordResetRequest req);
  Future<void> changePassword(ChangePasswordRequest req);
  Future<void> confirmEmail(ConfirmEmailRequest req);
  Future<List<SessionEntry>> listSessions();
  Future<void> revokeSession(String sessionId);
}
```

Implementation: `IdentityGatewayImpl` injected with `Dio` (built via `DioFactory.forAlias(ApiAlias.identity)`).

---

## 5. Screens

Per-screen template defined in [`docs/mobile-app-screen-api-plan.md` §5](../../../docs/mobile-app-screen-api-plan.md#5-per-screen-template-mandatory-schema). Status values reflect `apps/customer_flutter/lib/features/auth/screens/` as of 2026-05-19.

### S-1.1 Splash / session bootstrap

**Status:** Planned · **Route:** `/` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-splash`](../../../docs/mobile-screens-wireframes.md#phase-1-splash--s-11-splash--session-bootstrap)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/session/refresh | on app start if `refreshToken` present | yes | failure ⇒ Anonymous |
| GET | /v1/customer/identity/me | after refresh succeeds | safe | populates `Authenticated(profile)` |

#### Response data shape
```json
// session/refresh
{
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900
}

// me
{
  "accountId": "uuid",
  "displayName": "string",
  "email": "string",
  "phone": "+9665…",
  "locale": "ar | en",
  "marketCode": "SA | EG",
  "emailConfirmed": true,
  "roles": ["customer"]
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | first frame | brand splash + spinner ≤ 200 ms hold |
| loading | refresh in-flight | spinner + "Checking session…" |
| loaded-authenticated | both calls 2xx | route to `/home` |
| loaded-anonymous | no `refreshToken` in store | route to `/login` |
| error-refresh-401 | 401 on refresh | clear store, route to `/login` |
| error-5xx | 5xx on refresh or me | retry banner with correlation-id + Retry CTA |
| offline | network error | "Tap to retry" + offline badge |

#### Bloc scaffold (ADR-002)
- Bloc: `SplashBloc`
- Events: `SplashStarted`, `SplashRetried`
- States: `SplashInitial`, `SplashLoading`, `SplashAuthenticated(profile)`, `SplashAnonymous`, `SplashFailure(reason, correlationId)`

#### Acceptance criteria
- [ ] Brand splash holds ≤ 200 ms before showing the spinner (no flicker on warm starts).
- [ ] Single refresh attempt; never silently retries.
- [ ] On 401: secure storage is cleared **before** routing to `/login`.
- [ ] On 5xx / network: surface localized message + last-8 of correlation-id; Retry button re-runs `SplashStarted`.
- [ ] AR mirrors layout; EN is LTR.
- [ ] No `setState`; all state lives in `SplashBloc`.
- [ ] Unit test: `SplashAuthenticated` reached when both calls succeed; `SplashAnonymous` reached when no refresh token; `SplashFailure` reached on 5xx.
- [ ] Widget test: each state renders without exception in both locales.

#### Edge cases
- Cold start with expired refresh token (refresh returns 401) ⇒ Anonymous.
- App resumed from background while session was revoked on another device ⇒ next protected call hits 401 ⇒ standard 401 handler takes over.
- Locale change mid-bootstrap is impossible (Splash hides nav and other entry points).

---

### S-1.3 Login

**Status:** **Done** — verify against `apps/customer_flutter/lib/features/auth/screens/login_screen.dart`
**Route:** `/login` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-login`](../../../docs/mobile-screens-wireframes.md#phase-1-login--s-13-login)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/sign-in | on submit | yes | clears prior refresh-token before request |

#### Response data shape
```json
{
  "accountId": "uuid",
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900,
  "mfaRequired": false,
  "profile": {
    "displayName": "string",
    "locale": "ar | en",
    "marketCode": "SA | EG"
  }
}
```
If `mfaRequired = true`, response also carries `mfaChallengeId` and routes to OTP entry (S-1.5) in step-up mode.

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | form, focus identifier field |
| loading | submit | button spinner, inputs disabled |
| loaded | 2xx (no MFA) | route to `redirectTo` query or `/home` |
| loaded-mfa | 2xx with `mfaRequired=true` | route to `/otp?challengeId=…&intent=mfa` |
| validation | 422 | inline per-field errors |
| error-401 | 401 (bad credentials) | banner "Invalid email/phone or password" |
| error-403 | 403 (account locked / not active) | banner "Account temporarily disabled — contact support" |
| error-429 | 429 | banner with cooldown timer from `error.details.retryAfterSeconds` |
| error-5xx | 5xx | retry banner + correlation-id |
| offline | DioException | offline badge |

#### Bloc scaffold
- Bloc: `LoginBloc`
- Events: `LoginIdentifierChanged`, `LoginPasswordChanged`, `LoginSubmitted`, `LoginPasswordVisibilityToggled`
- States: sealed — `LoginForm(identifier, password, obscure, error?)`, `LoginLoading`, `LoginSuccess(session)`, `LoginMfaRequired(challengeId)`, `LoginFailure(reason, correlationId)`

#### Acceptance criteria
- [ ] Submit blocked when identifier or password is empty (client-side, before HTTP).
- [ ] On success: tokens persisted to `SessionStore`, root router transitions to Authenticated, deep-link `redirectTo` honored if present.
- [ ] On `mfaRequired`: route to OTP step-up (carries `challengeId` and an `intent=mfa` flag so OTP screen routes correctly on verify).
- [ ] On 401: previous tokens cleared **before** showing the banner.
- [ ] On 429: button stays disabled until `retryAfterSeconds` elapses; visible countdown.
- [ ] AR copy is editorial; "Sign in" reads as "تسجيل الدخول".
- [ ] Bloc unit tests: each state branch; widget tests: each UI state.

#### Edge cases
- Pasting email with leading whitespace ⇒ trim before submit.
- Network drop mid-submit ⇒ `LoginFailure(NetworkFailure)`, button re-enabled, draft preserved.
- Soft keyboard overlap on smaller devices ⇒ form is scrollable.

---

### S-1.4 Register

**Status:** **Done** — verify against `register_screen.dart`
**Route:** `/register` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-register`](../../../docs/mobile-screens-wireframes.md#phase-1-register--s-14-register)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/register | on submit | no | issues OTP automatically (server-side); 2xx implies "OTP sent" |

#### Response data shape
```json
{
  "accountId": "uuid",
  "otpChallengeId": "uuid",
  "otpDestination": "+9665***42 | u***@example.com",
  "otpExpiresInSeconds": 300
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | empty form |
| loading | submit | disabled form + button spinner |
| loaded | 201 | route to `/otp?challengeId=…&intent=register` |
| validation | 422 | per-field error (name, email, phone, password, terms) |
| error-409 | 409 (email/phone already in use) | banner with "Sign in instead?" link |
| error-429 | 429 | cooldown banner |
| error-5xx | 5xx | retry banner |
| offline | DioException | offline badge |

#### Bloc scaffold
- Bloc: `RegisterBloc`
- Events: `RegisterFieldChanged(field, value)`, `RegisterTermsToggled`, `RegisterSubmitted`
- States: `RegisterForm(fields, errors, termsAccepted)`, `RegisterLoading`, `RegisterOtpSent(challengeId, destination)`, `RegisterFailure(reason, correlationId)`

#### Acceptance criteria
- [ ] Terms checkbox is required; submit disabled until checked.
- [ ] Password rules surfaced as a live checklist (length ≥ 8, mixed case, digit).
- [ ] Phone is normalized to E.164 before submit using market code.
- [ ] On 409: deep-link to Login pre-fills identifier.
- [ ] On success: route to OTP with `intent=register` so OTP verify routes to home on success.
- [ ] Editorial AR copy.
- [ ] Tests as for Login.

#### Edge cases
- Auto-fill on iOS strong-password generator must not break the rules checklist.
- Pasted phone with country code prefix ⇒ collapse to single E.164 form.
- Email-only or phone-only registration is allowed if backend permits; UI surfaces "at least one of email or phone" validation locally.

---

### S-1.5 OTP request / verify

**Status:** **Done (verify)** — verify against `otp_screen.dart`
**Route:** `/otp` (params: `challengeId`, `intent`) · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-otp`](../../../docs/mobile-screens-wireframes.md#phase-1-otp--s-15-otp-request--verify)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/otp/request | on "Resend code" tap after cooldown | yes | rate-limited |
| POST | /v1/customer/identity/otp/verify | on 6-digit code complete | no | one-shot per code |

#### Response data shape
```json
// otp/request
{ "otpChallengeId": "uuid", "expiresInSeconds": 300, "resendAvailableInSeconds": 30 }

// otp/verify (intent=register or step-up)
{ "accessToken": "jwt", "refreshToken": "jwt", "expiresInSeconds": 900, "profile": {...} }
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | 6 input boxes, countdown to expiry, "Resend in N s" disabled |
| typing | code in-progress | live update; auto-submit on 6th digit |
| loading-verify | verify in-flight | spinner over inputs |
| loaded | 2xx | route per `intent`: `register` → `/home`, `mfa` → `redirectTo` or `/home`, `step-up` → return to caller |
| error-invalid | 401/422 | inputs cleared, banner "Invalid code" |
| error-expired | 410 / `code=otp_expired` | banner "Code expired" + Resend CTA enabled |
| error-429 | 429 (request) | countdown overrides Resend button |
| error-5xx | 5xx | retry banner |

#### Bloc scaffold
- Bloc: `OtpBloc`
- Events: `OtpStarted(challengeId, intent, destination)`, `OtpCodeChanged(value)`, `OtpResendRequested`, `OtpResendTicked` (every 1s)
- States: `OtpForm(code, expiresIn, resendIn, error?)`, `OtpVerifying`, `OtpSuccess(intent, session?)`, `OtpFailure(reason, correlationId)`

#### Acceptance criteria
- [ ] Code paste populates all 6 boxes at once.
- [ ] Auto-submit on 6th digit; manual submit also works.
- [ ] Resend disabled until `resendAvailableInSeconds` elapses; visible countdown.
- [ ] "Change phone/email" link routes back to the caller's input screen (register vs login vs MFA — pass via `intent`).
- [ ] Editorial AR digits — keep digits Latin per `intl` defaults unless market opts in (config flag).
- [ ] Tests as above.

#### Edge cases
- Auto-fill OTP from incoming SMS (iOS) populates all 6 boxes.
- App backgrounded during countdown ⇒ on resume, recompute remaining time from server-supplied expiry, not wall clock.

---

### S-1.6 Password reset — request

**Status:** **Done (verify)** — verify against `password_reset_screen.dart` (one screen handles both request + complete)
**Route:** `/password-reset` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-pwd-reset-request`](../../../docs/mobile-screens-wireframes.md#phase-1-pwd-reset-request--s-16-password-reset--request)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/password/reset-request | on submit | yes | does not reveal whether identifier exists (timing-safe) |

#### Response data shape
```json
{ "resetChallengeId": "uuid", "expiresInSeconds": 600, "destination": "u***@example.com" }
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | empty form |
| loading | submit | spinner |
| loaded | 2xx | route to `/password-reset/complete?challengeId=…` |
| validation | 422 | inline error |
| error-429 | 429 | cooldown |
| error-5xx | 5xx | retry banner |
| offline | DioException | offline badge |

#### Bloc scaffold
- Bloc: `PasswordResetRequestBloc`
- Events: `PasswordResetRequestIdentifierChanged`, `PasswordResetRequestSubmitted`
- States: `PasswordResetRequestForm`, `PasswordResetRequestLoading`, `PasswordResetRequestSuccess(challengeId, destination)`, `PasswordResetRequestFailure(...)`

#### Acceptance criteria
- [ ] Identifier accepts email or phone (E.164 normalization for phone).
- [ ] Success state shows masked destination ("Code sent to u***@example.com") but does not disclose whether the identifier is registered.
- [ ] AR copy editorial.
- [ ] Tests as above.

#### Edge cases
- Same identifier hit twice within cooldown ⇒ second submit blocked client-side with countdown.

---

### S-1.7 Password reset — set new password

**Status:** **Done (verify)** — verify the second pane of `password_reset_screen.dart`
**Route:** `/password-reset/complete` (param: `challengeId`) · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-pwd-reset-complete`](../../../docs/mobile-screens-wireframes.md#phase-1-pwd-reset-complete--s-17-password-reset--set-new)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/password/reset-complete | on submit | no | token-bound one-shot |

#### Response data shape
```json
{ "accountId": "uuid", "passwordChangedAt": "2026-05-19T10:00:00Z" }
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | empty form + rules checklist |
| loading | submit | spinner |
| loaded | 2xx | route to `/login` with pre-filled identifier + toast "Password updated" |
| validation | 422 | per-field errors |
| error-410 | 410 (expired challenge) | banner "Reset link expired — request a new one" + CTA back to S-1.6 |
| error-5xx | 5xx | retry banner |
| offline | DioException | offline badge |

#### Bloc scaffold
- Bloc: `PasswordResetCompleteBloc`
- Events: `PasswordResetCompleteNewPasswordChanged`, `PasswordResetCompleteConfirmChanged`, `PasswordResetCompleteSubmitted`
- States: `PasswordResetCompleteForm(fields, rulesProgress, errors?)`, `…Loading`, `…Success`, `…Failure(...)`

#### Acceptance criteria
- [ ] Password rules checklist updates live (length, mixed case, digit, no whitespace).
- [ ] Confirm-password field is checked client-side before submit.
- [ ] On success: do NOT auto-sign-in — route to Login with toast.
- [ ] AR copy editorial.
- [ ] Tests as above.

#### Edge cases
- Deep-link land with no `challengeId` ⇒ banner "Open the reset link from your email/SMS" + CTA back to S-1.6.

---

### S-1.8 Email confirmation deep link

**Status:** Planned
**Route:** `/email-confirm` (param: `token`) · **Bottom nav:** hidden
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-email-confirm`](../../../docs/mobile-screens-wireframes.md#phase-1-email-confirm--s-18-email-confirmation-deep-link)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/identity/email/confirm | on screen mount with `token` | no | token-bound |

#### Response data shape
```json
{ "emailConfirmed": true, "accountId": "uuid" }
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| loading | screen mount | brand splash + spinner |
| loaded-authed | 2xx + user is signed in | "Email Verified" + Continue → `/home` |
| loaded-anon | 2xx + user is signed out | "Email Verified" + "Sign in to continue" → `/login` |
| error-410 | 410 (token expired) | "Link expired" + CTA to re-send (re-uses S-1.5 OTP entry via dedicated server endpoint or hint) |
| error-5xx | 5xx | retry banner |
| offline | DioException | "We can't reach the server. Try again later." |

#### Bloc scaffold
- Bloc: `EmailConfirmBloc`
- Events: `EmailConfirmStarted(token)`, `EmailConfirmRetried`
- States: `EmailConfirmLoading`, `EmailConfirmSuccess`, `EmailConfirmFailure(reason, correlationId)`

#### Acceptance criteria
- [ ] Token consumed exactly once; reopening the same link shows the success-already state.
- [ ] If user is unauthenticated, do NOT auto-sign-in.
- [ ] AR copy editorial.
- [ ] Tests as above.

#### Edge cases
- Cold start via deep link before `SessionStore` resolves ⇒ wait for `SessionState != Unknown` before deciding which success branch to render.

---

### S-1.9 Locale & market

**Status:** Planned (entry exists in More hub once S-1.10 ships)
**Route:** `/settings/locale` · **Bottom nav:** visible (More tab active)
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-locale`](../../../docs/mobile-screens-wireframes.md#phase-1-locale--s-19-locale--market)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| PATCH | /v1/customer/identity/locale | on Save when value changed | yes | server records the user-preferred locale + market |

#### Response data shape
```json
{ "locale": "ar | en", "marketCode": "SA | EG" }
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | current values pre-selected |
| loading | submit | spinner |
| loaded | 2xx | `MaterialApp` rebuilds with new locale/market; toast "Saved"; pop |
| validation | 422 (unsupported market for user) | inline error |
| error-5xx | 5xx | retry banner |
| offline | DioException | offline badge; allow local-only persistence with "will sync" note |

#### Bloc scaffold
- Bloc: `LocaleSettingsBloc`
- Events: `LocaleSettingsChanged(locale)`, `LocaleSettingsMarketChanged(market)`, `LocaleSettingsSubmitted`
- States: `LocaleSettingsForm(locale, market, dirty)`, `LocaleSettingsLoading`, `LocaleSettingsSuccess`, `LocaleSettingsFailure(...)`

#### Acceptance criteria
- [ ] Save persists to `SessionStore` first (optimistic), then PATCHes. On failure, revert local store and surface error.
- [ ] After success, every visible screen's text direction and translations update without app restart.
- [ ] Currency display in Phase 4 cart panel reflects the chosen market immediately.
- [ ] AR copy editorial.
- [ ] Tests: Bloc transitions + an integration test verifying a sibling screen's text direction flips.

#### Edge cases
- User picks an unsupported `market` for their account (server returns 422) ⇒ inline error; offer support contact.

---

### S-1.10 More hub + Account security

**Status:** Partially Done — `more_screen.dart` exists; account-security entry to be added in this phase.
**Route:** `/more`, `/settings/security` · **Bottom nav:** visible (More tab)
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-account-security`](../../../docs/mobile-screens-wireframes.md#phase-1-account-security--s-110-account-security)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/identity/me | on hub open | safe | display name, email, market |
| POST | /v1/customer/identity/password/change | on Account Security submit | no | requires current pwd |
| POST | /v1/customer/identity/sign-out | on Sign Out tap | no | invalidates current refresh-token |

#### Response data shape
```json
// me — already documented in S-1.1
// password/change — 204 No Content
// sign-out — 204 No Content
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial (hub) | mount | static menu (Account Security, Language & Market, Sessions, Verification CTA (Phase 7), Reviews (Phase 7), Company (Phase 8), Sign Out) |
| initial (security form) | mount | empty form |
| loading | submit / sign-out | spinner |
| loaded-change | 2xx (password change) | toast + clear form |
| loaded-signout | 2xx (sign-out) | clear store + route to `/login` |
| validation | 422 | per-field errors |
| error-401 | 401 (wrong current pwd) | banner |
| error-5xx | 5xx | retry banner |
| offline | DioException | offline badge |

#### Bloc scaffold
- `MoreHubCubit` for the static menu (single state — profile snapshot from session store).
- `AccountSecurityBloc` for the change-password form.
- `SignOutCubit` for the sign-out action.

#### Acceptance criteria
- [ ] Menu rows route to: `/settings/security`, `/settings/locale`, `/settings/sessions`, `/verification` (Phase 7), `/my-reviews` (Phase 7), `/company` (Phase 8), and sign-out action.
- [ ] Phase-7/8 rows are visible but their target routes are placeholders this phase (route to a "coming soon" stub if not yet implemented — final implementations replace the stub).
- [ ] Sign-out clears `SessionStore` **before** routing.
- [ ] Account-security: on success, server invalidates other sessions; show toast "Other sessions signed out".
- [ ] Tests as above.

#### Edge cases
- Sign-out while a long-running upload is in flight (Phase 7 verification doc upload, Phase 6 return photos) ⇒ cancel the in-flight Dio request and discard the upload Bloc.

---

### S-1.11 Device / session management

**Status:** Planned
**Route:** `/settings/sessions` · **Bottom nav:** visible (More tab)
**OpenAPI source:** `openapi.identity.json`
**Wireframe:** [`#phase-1-sessions`](../../../docs/mobile-screens-wireframes.md#phase-1-sessions--s-111-device--session-management)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/identity/sessions | on mount and pull-to-refresh | safe | |
| DELETE | /v1/customer/identity/sessions/{sessionId} | on Revoke tap | no | revoking current session forces sign-out |

#### Response data shape
```json
[
  {
    "sessionId": "uuid",
    "isCurrent": true,
    "deviceLabel": "iPhone 15 Pro",
    "ipCity": "Riyadh",
    "lastActiveAt": "2026-05-19T10:00:00Z",
    "userAgent": "Mozilla/5.0 …"
  }
]
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | skeleton list |
| loaded | 2xx | grouped list ("This device" + "Other devices") |
| empty | 2xx + 1 row (only current) | "No other devices" section + current device card |
| loading-revoke | DELETE in-flight on a row | spinner on that row |
| loaded-revoke-current | DELETE of current succeeds | clear store + route to `/login` |
| loaded-revoke-other | DELETE of another succeeds | optimistically mark row as "Revoked just now" |
| error-409 | 409 (already revoked) | refresh list silently |
| error-5xx | 5xx | retry banner |

#### Bloc scaffold
- Bloc: `SessionsBloc`
- Events: `SessionsStarted`, `SessionsRefreshed`, `SessionsRevokeRequested(sessionId)`
- States: `SessionsLoading`, `SessionsLoaded(items)`, `SessionsFailure(reason, correlationId)`

#### Acceptance criteria
- [ ] Current device is non-revocable from the row UI — instead use Sign Out (S-1.10).
- [ ] Revoking another session optimistically updates the row; on 409, silently refresh.
- [ ] Pull-to-refresh resets the list.
- [ ] AR copy editorial.
- [ ] Tests as above.

#### Edge cases
- Refresh-token rotation mid-screen (after re-auth from a different tab) ⇒ refresh-token in store may not match a listed session; do nothing visible — server will reconcile on next call.

---

## 6. Edge cases (cross-screen)

- **Deep-link cold start** (email confirm, password-reset complete, invitation accept in Phase 8): `SplashBloc` must finish before the deep-link route is resolved; achieved by the redirect guard waiting on `SessionStore.stateStream`.
- **Locale switch mid-flow** (rare; possible from background notification CTAs that route to More): re-render does not lose form state because Bloc-held drafts survive `MaterialApp` rebuilds when the `BlocProvider` is above the locale-changing scope.
- **Refresh-token expiry mid-flow on a long form** (e.g., Account Security): 401 handler kicks in, single refresh attempt, on failure the user is signed out — the draft is lost. Acceptable trade-off; an unsigned session must not retain protected drafts.
- **Background server-side revocation**: detected on the next protected call. Until then, the app appears authenticated. Acceptable.
- **OTP delivery delay > expiry** (e.g., SMS provider lag): Resend CTA after cooldown; on second 410, surface a "Switch to email" CTA if the user has both identifiers (server-side decides delivery channel for now).

## 7. Acceptance criteria — phase-wide

- [ ] All 10 screens above are implemented and pass the per-screen DoD checkboxes.
- [ ] Foundation files in §4 exist and are exercised by Phase 1 screens.
- [ ] `IdentityGateway` is the only entry point to the identity domain — no widget or Bloc calls `Dio` directly.
- [ ] Every protected screen guarded by `SessionStore.requireAuthenticated`.
- [ ] `flutter analyze` clean in `apps/customer_flutter/`.
- [ ] `flutter test` passes (unit + widget tests for each screen + Bloc).
- [ ] Manual smoke test (recorded in `quickstart.md`): register → OTP → home (authenticated); separately: sign-in → home; separately: locale switch en↔ar persists across cold start.
- [ ] `docs/mobile-app-screen-api-plan.md` §8 row for Phase 1 flipped from "Partially done" to **Done** once all checkboxes pass.

## 8. Dependencies

- **Upstream (must be ready):** `services/backend_api/Modules/Identity` (spec 004) — endpoints are live per `openapi.identity.json`.
- **Downstream (this phase unblocks):** every other mobile phase (2–8) depends on foundation deliverables (gateway pattern, error mapper, session store, theme, router).

## 9. Out of scope (explicitly)

- Admin identity (`/v1/admin/identity/*`) — admin web app.
- MFA TOTP enroll/rotate flows for customers — not exposed on customer surface in current OpenAPI (only admins enroll TOTP).
- Biometric unlock — not in launch scope.
- Social sign-in (Apple, Google) — not in launch scope.
- Notification preferences UI — lives in Phase 7 once notifications module surfaces a customer-pref endpoint.

## 10. Phase assignment & references

- **Phase:** 1 (customer mobile)
- **Repo phase mapping:** consumes Phase 1B / spec 004 backend.
- **Constitution references:** Principles 4, 5, 7, 24, 25, 27, 28; ADR-002 (Bloc), ADR-009 (Unifonic / Vodafone Egypt SMS for OTP, AWS SES for email).
- **Files referenced in this spec:** see §4 (foundation) and `apps/customer_flutter/lib/features/auth/screens/*` for already-Done screens.
