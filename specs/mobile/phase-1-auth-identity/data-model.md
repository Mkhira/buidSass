# Data Model — Phase 1: Auth & Identity

> Trimmed request/response DTOs as consumed by Phase 1 screens. Source of truth: [`services/backend_api/openapi.identity.json`](../../../services/backend_api/openapi.identity.json). Only fields the mobile app reads or sends are documented here.

## Conventions

- `string?` → optional.
- `iso8601` → UTC ISO-8601 string.
- `enum` values are listed inline.
- `Failure` payload structure is shared — see `core/error/failure.dart`.

## Shared types

### `Profile` (returned by `me`, `sign-in`, `register`-after-otp, `otp/verify`)
```json
{
  "accountId": "uuid",
  "displayName": "string",
  "email": "string?",
  "phone": "string?",              // E.164
  "locale": "ar | en",
  "marketCode": "SA | EG",
  "emailConfirmed": true,
  "phoneConfirmed": true,
  "roles": ["customer"]
}
```

### `SessionTokens`
```json
{
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900
}
```

## Endpoints

### POST `/v1/customer/identity/register`
Request:
```json
{
  "displayName": "string",
  "email": "string?",      // at least one of email or phone required
  "phone": "string?",
  "password": "string",
  "locale": "ar | en",
  "marketCode": "SA | EG",
  "termsAccepted": true
}
```
Response (201):
```json
{
  "accountId": "uuid",
  "otpChallengeId": "uuid",
  "otpDestination": "string",       // masked
  "otpExpiresInSeconds": 300
}
```

### POST `/v1/customer/identity/sign-in`
Request:
```json
{
  "identifier": "string",   // email or phone (E.164)
  "password": "string"
}
```
Response (200):
```json
{
  "accountId": "uuid",
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900,
  "mfaRequired": false,
  "mfaChallengeId": "uuid?",
  "profile": { /* Profile */ }
}
```

### POST `/v1/customer/identity/sign-out`
Request: no body.
Response: 204.

### POST `/v1/customer/identity/session/refresh`
Request:
```json
{ "refreshToken": "jwt" }
```
Response (200):
```json
{
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900
}
```

### GET `/v1/customer/identity/me`
Request: no body, bearer required.
Response (200): `Profile`.

### PATCH `/v1/customer/identity/locale`
Request:
```json
{ "locale": "ar | en", "marketCode": "SA | EG" }
```
Response (200): `{ "locale": "...", "marketCode": "..." }`.

### POST `/v1/customer/identity/otp/request`
Request:
```json
{
  "challengeId": "uuid?",       // present in step-up; absent in fresh request
  "identifier": "string?",      // present when no challengeId (used for "resend" before challenge exists)
  "channel": "sms | email | auto"
}
```
Response (200):
```json
{
  "otpChallengeId": "uuid",
  "expiresInSeconds": 300,
  "resendAvailableInSeconds": 30
}
```

### POST `/v1/customer/identity/otp/verify`
Request:
```json
{
  "challengeId": "uuid",
  "code": "123456",
  "intent": "register | mfa | step-up"
}
```
Response (200, when `intent=register` or `intent=mfa`):
```json
{
  "accessToken": "jwt",
  "refreshToken": "jwt",
  "expiresInSeconds": 900,
  "profile": { /* Profile */ }
}
```
Response (200, when `intent=step-up`): step-up token (used by Phase 7 verification submit etc. — out of Phase 1 scope; documented for completeness).

### POST `/v1/customer/identity/password/reset-request`
Request:
```json
{ "identifier": "string", "channel": "sms | email | auto" }
```
Response (200):
```json
{
  "resetChallengeId": "uuid",
  "expiresInSeconds": 600,
  "destination": "string"       // masked
}
```

### POST `/v1/customer/identity/password/reset-complete`
Request:
```json
{
  "resetChallengeId": "uuid",
  "code": "123456",
  "newPassword": "string"
}
```
Response (200):
```json
{ "accountId": "uuid", "passwordChangedAt": "iso8601" }
```

### POST `/v1/customer/identity/password/change`
Request:
```json
{ "currentPassword": "string", "newPassword": "string" }
```
Response: 204. Server invalidates other sessions; current session keeps its token.

### POST `/v1/customer/identity/email/confirm`
Request:
```json
{ "token": "string" }
```
Response (200):
```json
{ "emailConfirmed": true, "accountId": "uuid" }
```

### GET `/v1/customer/identity/sessions`
Response (200):
```json
[
  {
    "sessionId": "uuid",
    "isCurrent": true,
    "deviceLabel": "string",
    "ipCity": "string",
    "lastActiveAt": "iso8601",
    "userAgent": "string"
  }
]
```

### DELETE `/v1/customer/identity/sessions/{sessionId}`
Response: 204. Revoking the current session also invalidates `accessToken`; client treats next protected call's 401 as forced sign-out.

## Error contract (all endpoints)

Status codes returned and their semantic mapping in mobile:

| Status | Failure mapping | When |
|---|---|---|
| 400 | `ValidationFailure` | malformed body |
| 401 | `AuthFailure` (or refresh trigger) | invalid/expired credentials or token |
| 403 | `ForbiddenFailure` | account locked, not allowed |
| 404 | `NotFoundFailure` | session/identifier not found (rare on these endpoints) |
| 409 | `ConflictFailure` | duplicate registration, conflicting state |
| 410 | `ValidationFailure` (subkind=`expired`) | OTP/reset/email-confirm token expired |
| 422 | `ValidationFailure(fields)` | per-field validation errors |
| 429 | `ValidationFailure(subkind=cooldown, retryAfterSeconds)` | rate-limited |
| 500-599 | `ServerFailure` | retry banner |
| network/timeout/cancel | `NetworkFailure` / `OfflineFailure` | retry CTA |

Error body shape:
```json
{
  "error": {
    "code": "STRING_CODE",
    "message": "localized string",
    "correlationId": "uuid",
    "details": {
      "fields": [{ "path": "password", "message": "Too short", "code": "password_min_length" }],
      "retryAfterSeconds": 30
    }
  }
}
```

## Local persistence schema (`SessionStore`)

| Key (secure storage) | Type | Notes |
|---|---|---|
| `session.access_token` | string | cleared on sign-out / forced sign-out |
| `session.refresh_token` | string | cleared on sign-out |
| `session.expires_at` | iso8601 | informational; the server is the source of truth |
| `session.profile_json` | JSON | snapshot of `Profile` for offline boot UI hints |
| `session.locale` | enum `ar`/`en` | mirrored to `MaterialApp.locale` |
| `session.market_code` | enum `SA`/`EG` | mirrored to `X-Market-Code` |
