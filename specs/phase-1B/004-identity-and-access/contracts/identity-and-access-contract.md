# HTTP Contract — Identity and Access (Spec 004)

**Date**: 2026-04-22 · **Style**: REST + JSON, ADR-003 vertical-slice endpoints. All routes are versioned under `/v1/`.

Two surfaces with **separate base prefixes** to make SC-011 (customer/admin token non-interchangeability) structurally visible:

- Customer: `/v1/customer/identity/*`
- Admin: `/v1/admin/identity/*`

Every response carries the correlation-id from spec 003's middleware as `X-Correlation-Id`. Every error response conforms to RFC 7807 `application/problem+json` with an extra `reasonCode` field (e.g. `identity.lockout.active`). AR/EN variants of the `title`/`detail` fields are chosen via `Accept-Language`.

---

## Common types

```jsonc
// ProblemDetails (errors)
{
  "type": "https://errors.dental-commerce/identity/<reasonCode>",
  "title": "Account is temporarily locked",        // localized
  "status": 423,
  "detail": "Too many failed sign-in attempts. Try again at 2026-04-22T11:15:00Z.",
  "instance": "/v1/customer/identity/sign-in",
  "reasonCode": "identity.lockout.active",
  "lockedUntil": "2026-04-22T11:15:00Z"             // reason-specific extensions
}

// AuthSession (success envelope for sign-in / refresh / step-up)
{
  "accessToken": "eyJhbGciOi...",                   // ES256 JWT
  "accessTokenExpiresAt": "2026-04-22T11:15:00Z",
  "refreshToken": "<opaque-256-bit-base64url>",
  "refreshTokenExpiresAt": "2026-05-22T11:00:00Z",
  "session": {
    "id": "01HZ...",
    "surface": "customer",
    "marketCode": "ksa"
  },
  "account": {
    "id": "01HZ...",
    "emailDisplay": "Noura@example.com",
    "locale": "ar",
    "roles": ["customer.standard"]
  }
}
```

---

## Customer endpoints (18)

### POST /v1/customer/identity/register
Request:
```json
{
  "email": "noura@example.com",
  "phone": "+966501234567",
  "password": "…",
  "marketCode": "ksa",           // user-selected (Clarification Q5)
  "locale": "ar",
  "displayName": "Noura"
}
```
Responses:
- `202 Accepted` — always, regardless of email-already-taken (FR-030; enumeration resistance). Body is an informational envelope, not a sign-in.
- `400` — validation (`identity.register.invalid_email` / `…invalid_phone` / `…password_too_weak` / `…market_required`).
- `400` — `identity.phone.market_mismatch` when phone country-code disagrees with selected market (Edge Case #1).
- `429` — `identity.register.rate_limited`.

State: Account.`pending_email_verification`; EmailVerificationChallenge.`pending`; if the email was already taken, an `account-exists` notice is sent instead of a verify link.

---

### POST /v1/customer/identity/email/confirm
Request: `{ "token": "…" }`
- `200 OK` → Account → `active`.
- `410 Gone` — `identity.email_verification.expired`.
- `409` — `identity.email_verification.consumed`.

---

### POST /v1/customer/identity/otp/request
Request: `{ "phone": "+966501234567", "purpose": "signin_customer" | "registration_phone" | "password_reset_confirm" }`
- `202 Accepted` — uniform regardless of existence. OTP dispatched via `IOtpChallengeDispatcher`.
- `429` — `identity.otp.rate_limited`.

---

### POST /v1/customer/identity/otp/verify
Request: `{ "challengeId": "…", "code": "123456" }`
- `200 OK` with `AuthSession` if purpose = `signin_customer`.
- `200 OK` without session body if purpose = `registration_phone` (marks phone verified).
- `400` — `identity.otp.invalid`.
- `410 Gone` — `identity.otp.expired`.
- `429` — `identity.otp.exhausted` (attempts maxed).

---

### POST /v1/customer/identity/sign-in
Request: `{ "email": "…", "password": "…" }`
- `200 OK` → `AuthSession`.
- `400` — `identity.sign_in.invalid_credentials` (uniform copy regardless of which field was wrong).
- `423` — `identity.lockout.active` with `lockedUntil`.
- `428 Precondition Required` — `identity.sign_in.email_not_verified`.

Constant-time behavior: branches on email-not-found still run a dummy Argon2id verify.

---

### POST /v1/customer/identity/session/refresh
Request: `{ "refreshToken": "…" }`
- `200 OK` → new `AuthSession` (old refresh token marked `consumed`).
- `401` — `identity.refresh.invalid` (token unknown, revoked, or reused — reuse triggers session revocation).
- `410 Gone` — `identity.refresh.expired`.

---

### POST /v1/customer/identity/sign-out
Auth: Bearer (customer). Request: `{ "refreshToken": "…" }` optional.
- `204 No Content` — session moves to `revoked`.

---

### POST /v1/customer/identity/password/reset-request
Request: `{ "email": "…" }`
- `202 Accepted` — uniform (anti-enumeration, FR-030).

---

### POST /v1/customer/identity/password/reset-complete
Request: `{ "token": "…", "newPassword": "…" }`
- `200 OK` — all active sessions for account revoked (FR-015).
- `400` — `identity.password_reset.invalid` / `…password_too_weak`.
- `410 Gone` — `identity.password_reset.expired`.

---

### POST /v1/customer/identity/password/change
Auth: Bearer (customer). Request: `{ "currentPassword": "…", "newPassword": "…" }`
- `200 OK` — all *other* sessions revoked; current session retained (FR-015).
- `400` — `identity.password_change.invalid_current` / `…too_weak`.

---

### GET /v1/customer/identity/sessions
Auth: Bearer (customer).
Response: `{ "sessions": [{ "id", "createdAt", "lastSeenAt", "clientAgent", "isCurrent" }, ...] }`.

---

### DELETE /v1/customer/identity/sessions/{sessionId}
Auth: Bearer (customer). Revokes a non-current session.
- `204 No Content`.
- `403` — `identity.session.revoke_current_forbidden` (use sign-out instead).

---

### GET /v1/customer/identity/me
Auth: Bearer (customer). Returns `account` profile (email, phone verification state, locale, roles).

---

### PATCH /v1/customer/identity/locale
Auth: Bearer (customer). Request: `{ "locale": "ar" | "en" }`.

---

## Admin endpoints (17)

### POST /v1/admin/identity/invitation/accept
Request: `{ "token": "…", "newPassword": "…" }` → `202 Accepted`, admin still needs TOTP enrollment.

### POST /v1/admin/identity/mfa/totp/enroll
Auth: partial-auth token from `invitation/accept`. Response: `{ "factorId", "otpauthUri", "recoveryCodes": [...] }`. `recoveryCodes` shown exactly once.

### POST /v1/admin/identity/mfa/totp/confirm
Request: `{ "factorId", "code" }` → `200 OK`, factor transitions `pending_confirmation → active`.

### POST /v1/admin/identity/sign-in
Request: `{ "email", "password" }`. Response: if the account requires MFA → `{ "mfaChallenge": { "kind": "totp" | "otp", "challengeId": "…" } }` without an AuthSession; else full `AuthSession`.
- `423` — `identity.lockout.active`.
- `400` — `identity.sign_in.invalid_credentials`.

### POST /v1/admin/identity/mfa/challenge
Request: `{ "challengeId", "kind": "totp" | "otp", "code" }` → full `AuthSession` with admin-scope claims. Replay guard rejects reused TOTP window (`409 identity.mfa.replay`).

### POST /v1/admin/identity/mfa/step-up
Auth: Bearer (admin). Request: `{ "purpose": "…" }`. Triggers OTP send for sensitive operations. Response: `{ "challengeId" }`.

### POST /v1/admin/identity/mfa/step-up/confirm
Auth: Bearer (admin). Request: `{ "challengeId", "code" }`. On success, current access token is annotated with `step_up_valid_until` (short-lived, default 10 minutes) that downstream admin handlers check.

### POST /v1/admin/identity/session/refresh
Same shape as customer; admin TTLs (5 min access / 8 h idle refresh).

### POST /v1/admin/identity/sign-out
`204 No Content`.

### POST /v1/admin/identity/password/reset-request
`202 Accepted` — uniform.

### POST /v1/admin/identity/password/reset-complete
Same shape as customer. All admin sessions revoked on success.

### POST /v1/admin/identity/password/change
Same shape as customer.

### POST /v1/admin/identity/invitations (invite a new admin — requires super-admin + step-up)
Request: `{ "email", "roleCode": "platform.finance" | "platform.support" | ... }` → `202 Accepted`.

### DELETE /v1/admin/identity/invitations/{id} (super-admin)
`204 No Content`.

### GET /v1/admin/identity/accounts/{id}/sessions (super-admin)
Response: list of sessions for a target admin.

### DELETE /v1/admin/identity/accounts/{id}/sessions/{sessionId} (super-admin + step-up)
`204 No Content`. Audit-emits `identity.admin.session.revoked`.

### PATCH /v1/admin/identity/accounts/{id}/role (super-admin + step-up)
Request: `{ "roleCode": "…", "marketCode": "…" }` → `204`. Audit emits before/after.

### POST /v1/admin/identity/accounts/{id}/mfa/reset (super-admin + step-up)
Revokes target's MFA factors. Target must re-enroll on next sign-in. `204`.

### GET /v1/admin/identity/me
Admin profile including MFA state, allowed permissions, market scope.

---

## Authorization model

- Bearer JWT on every authed endpoint. `aud` = surface (`customer.api` | `admin.api`); token validator rejects cross-audience tokens at the framework layer (SC-011).
- Handler-level authorization via `[RequirePermission("permission.code")]` attribute that reads the authenticated account's merged role→permission set from the session JWT claims.
- For admin endpoints marked "requires step-up," a `[RequireStepUp]` attribute additionally checks the `step_up_valid_until` claim; if absent or expired, returns `412 Precondition Failed` with `identity.step_up.required`.
- Every authorization decision emits a row to `authorization_audit` (denies always; allows sampled 1 %) — SC-007.

---

## Rate limits (per R7 in research.md)

Enforced by `System.Threading.RateLimiting`. `X-RateLimit-*` headers on responses. `429` carries `retryAfterSeconds` extension.

---

## Idempotency

- `POST /…/sign-in`, `…/refresh`, `…/sign-out`: NOT idempotent (each call creates a distinct session/refresh revision).
- `POST /…/otp/request`, `…/password/reset-request`, `…/register`: idempotent within the rate-limit window (same scope-key + recent call yields 202 without dispatch duplication).

---

## Open admin surfaces handed to later specs

- Finance role–specific endpoints: spec 018.
- Verification reviewer role endpoints: spec 020.
- Company-admin account management: spec 021.
