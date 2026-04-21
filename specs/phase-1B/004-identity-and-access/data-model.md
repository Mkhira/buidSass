# Data Model — Identity and Access (Spec 004)

**Date**: 2026-04-22 · **Source**: spec.md Key Entities + Requirements + Clarifications.

Every table lands in the `identity` Postgres schema. Audit emission uses spec 003's shared `audit_log_entries` (not redefined here). `market_code` is `citext` matching the A1 / spec 003 convention.

---

## Tables (17)

### 1. `identity.accounts`
One row per authenticated principal, customer or admin.

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | v7 (time-ordered) |
| `surface` | `citext` NOT NULL | `customer` \| `admin` |
| `market_code` | `citext` NOT NULL | `eg` \| `ksa` for customer; `platform` for admin (FR-001a) |
| `email_normalized` | `citext` NOT NULL | lowercased + trimmed |
| `email_display` | `text` NOT NULL | original-case, preserved for emails |
| `phone_e164` | `text` NULL | customer only; E.164 via libphonenumber |
| `phone_market_code` | `citext` NULL | inferred from phone country-code |
| `password_hash` | `text` NOT NULL | `$argon2id$…` encoded, includes cost params |
| `password_hash_version` | `smallint` NOT NULL | 1 today; increment when cost floor rises |
| `status` | `citext` NOT NULL | see Account state machine |
| `email_verified_at` | `timestamptz` NULL | |
| `phone_verified_at` | `timestamptz` NULL | customer only |
| `locale` | `citext` NOT NULL DEFAULT `ar` | `ar` \| `en` |
| `display_name` | `text` NULL | |
| `created_at` | `timestamptz` NOT NULL DEFAULT now() | |
| `updated_at` | `timestamptz` NOT NULL DEFAULT now() | maintained by interceptor |
| `deleted_at` | `timestamptz` NULL | soft-delete (ADR-004 query filter) |

**Indexes**:
- UNIQUE `(surface, email_normalized)` WHERE `deleted_at IS NULL` — allows same email on different surface (customer + admin share the same natural person edge case).
- UNIQUE `(surface, phone_e164)` WHERE `phone_e164 IS NOT NULL AND deleted_at IS NULL`.
- Index `(market_code, created_at DESC)`.

**Validation**:
- `email_normalized` must pass RFC 5322 + contain `@` + length ≤ 254.
- `phone_e164` when present must parse by libphonenumber; `phone_market_code` computed, not user-submitted.
- `locale ∈ {ar, en}`.
- `status` must be a terminal or active state from Account machine.

---

### 2. `identity.sessions`
Active session envelope; 1 refresh-token row per session revision.

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `account_id` | `uuid` NOT NULL FK → accounts | |
| `surface` | `citext` NOT NULL | denormalized for query speed |
| `created_at` | `timestamptz` NOT NULL | |
| `last_seen_at` | `timestamptz` NOT NULL | |
| `client_agent` | `text` NULL | UA string (truncated to 512 chars) |
| `client_ip_hash` | `bytea` NOT NULL | SHA-256(ip + per-env pepper) — no raw IP stored |
| `status` | `citext` NOT NULL | see Session state machine |
| `revoked_at` | `timestamptz` NULL | |
| `revoked_reason` | `citext` NULL | `user_signout`, `admin_revoke`, `password_change`, `idle_expiry`, `absolute_expiry`, `security_incident` |

Index `(account_id, status, last_seen_at DESC)`.

---

### 3. `identity.refresh_tokens`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `session_id` | `uuid` NOT NULL FK → sessions | cascade on session delete |
| `token_hash` | `bytea` NOT NULL | SHA-256(token + per-row salt) |
| `token_salt` | `bytea` NOT NULL | 16 B random |
| `issued_at` | `timestamptz` NOT NULL | |
| `expires_at` | `timestamptz` NOT NULL | idle window (15 min/30 d customer; 5 min/8 h admin relative to access TTL — refresh expiry is the absolute sliding idle) |
| `status` | `citext` NOT NULL | `active` \| `consumed` \| `revoked` |
| `consumed_at` | `timestamptz` NULL | |
| `superseded_by` | `uuid` NULL FK → refresh_tokens | |

Partial unique index `(session_id)` WHERE `status = 'active'` — at most one active refresh per session.

---

### 4. `identity.revoked_refresh_tokens` (revocation ledger)
Append-only. Powers the bloom-filter cache for SC-004.

| column | type | notes |
|---|---|---|
| `token_hash` | `bytea` PK | matches `refresh_tokens.token_hash` |
| `revoked_at` | `timestamptz` NOT NULL | |
| `reason` | `citext` NOT NULL | |
| `actor_id` | `uuid` NULL | admin who revoked, if applicable |

---

### 5. `identity.otp_challenges`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `account_id` | `uuid` NULL FK → accounts | null during registration before account row exists |
| `purpose` | `citext` NOT NULL | `registration_phone`, `signin_customer`, `signin_admin_step_up`, `password_reset_confirm` |
| `surface` | `citext` NOT NULL | |
| `channel` | `citext` NOT NULL | `sms`, `email` |
| `destination_hash` | `bytea` NOT NULL | SHA-256(phone_or_email + env pepper) — avoids plaintext dest in logs |
| `code_hash` | `bytea` NOT NULL | Argon2id-light (`m=16 MiB, t=1, p=1`) of the 6-/8-digit code |
| `code_length` | `smallint` NOT NULL | 6 for customer, 8 for admin step-up (FR-020) |
| `created_at` | `timestamptz` NOT NULL | |
| `expires_at` | `timestamptz` NOT NULL | 10 min customer, 5 min admin |
| `max_attempts` | `smallint` NOT NULL | 5 |
| `attempts` | `smallint` NOT NULL DEFAULT 0 | |
| `status` | `citext` NOT NULL | see OtpChallenge machine |
| `completed_at` | `timestamptz` NULL | |

Index `(account_id, purpose, created_at DESC)`, partial WHERE `status = 'pending'`.

---

### 6. `identity.email_verification_challenges`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `account_id` | `uuid` NOT NULL FK → accounts | |
| `token_hash` | `bytea` NOT NULL | SHA-256 of issued token |
| `created_at` | `timestamptz` NOT NULL | |
| `expires_at` | `timestamptz` NOT NULL | 24 h |
| `status` | `citext` NOT NULL | see EmailVerification machine |
| `completed_at` | `timestamptz` NULL | |

---

### 7. `identity.password_reset_tokens`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `account_id` | `uuid` NOT NULL FK → accounts | |
| `token_hash` | `bytea` NOT NULL | SHA-256 |
| `created_at` | `timestamptz` NOT NULL | |
| `expires_at` | `timestamptz` NOT NULL | 30 min customer, 15 min admin |
| `status` | `citext` NOT NULL | see PasswordReset machine |
| `completed_at` | `timestamptz` NULL | |

---

### 8. `identity.admin_invitations`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `email_normalized` | `citext` NOT NULL | |
| `invited_by_account_id` | `uuid` NOT NULL FK → accounts | |
| `invited_role_id` | `uuid` NOT NULL FK → roles | |
| `token_hash` | `bytea` NOT NULL | |
| `created_at` | `timestamptz` NOT NULL | |
| `expires_at` | `timestamptz` NOT NULL | 72 h |
| `status` | `citext` NOT NULL | see AdminInvitation machine |
| `accepted_account_id` | `uuid` NULL FK → accounts | set on acceptance |
| `accepted_at` | `timestamptz` NULL | |

---

### 9. `identity.admin_mfa_factors`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `account_id` | `uuid` NOT NULL FK → accounts | admin only |
| `kind` | `citext` NOT NULL | `totp` (launch); `webauthn` reserved |
| `secret_encrypted` | `bytea` NOT NULL | `IDataProtectionProvider` |
| `confirmed_at` | `timestamptz` NULL | null until enrollment TOTP echo verifies |
| `created_at` | `timestamptz` NOT NULL | |
| `revoked_at` | `timestamptz` NULL | |
| `last_used_at` | `timestamptz` NULL | |
| `recovery_codes_hash` | `jsonb` NOT NULL | array of `{ hash, used_at? }` |

Unique `(account_id, kind)` WHERE `revoked_at IS NULL`.

---

### 10. `identity.admin_mfa_replay_guard`

| column | type | notes |
|---|---|---|
| `factor_id` | `uuid` NOT NULL | |
| `window_counter` | `bigint` NOT NULL | `unix_time / 30` |
| `observed_at` | `timestamptz` NOT NULL | for TTL sweep |

PK `(factor_id, window_counter)`. Rows older than 5 minutes swept by scheduled job.

---

### 11. `identity.lockout_state`
One row per `(account_id, reason)` pair; tracks failed-attempt counters for lockout per FR-018.

| column | type | notes |
|---|---|---|
| `account_id` | `uuid` NOT NULL FK → accounts | |
| `reason` | `citext` NOT NULL | `signin`, `otp`, `mfa`, `password_reset` |
| `failed_count` | `int` NOT NULL DEFAULT 0 | |
| `first_failed_at` | `timestamptz` NULL | |
| `locked_until` | `timestamptz` NULL | |
| `updated_at` | `timestamptz` NOT NULL | |

PK `(account_id, reason)`. Thresholds tiered per FR-018 (customer 5 fails / 15 min window → 15 min lock; admin 3 fails / 15 min → 30 min lock).

---

### 12. `identity.roles`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `code` | `citext` UNIQUE NOT NULL | e.g. `platform.super_admin`, `platform.finance`, `platform.support`, `customer.standard`, `customer.company_owner` |
| `name_ar` | `text` NOT NULL | |
| `name_en` | `text` NOT NULL | |
| `scope` | `citext` NOT NULL | `platform` \| `market` \| `vendor` (reserved, unused launch) |
| `system` | `boolean` NOT NULL DEFAULT false | seeded roles can't be deleted |

---

### 13. `identity.permissions`

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `code` | `citext` UNIQUE NOT NULL | e.g. `orders.read`, `orders.refund`, `catalog.write`, `verification.decide` |
| `description` | `text` NOT NULL | |

---

### 14. `identity.role_permissions`

| column | type | notes |
|---|---|---|
| `role_id` | `uuid` NOT NULL FK → roles | |
| `permission_id` | `uuid` NOT NULL FK → permissions | |

PK `(role_id, permission_id)`.

---

### 15. `identity.account_roles`

| column | type | notes |
|---|---|---|
| `account_id` | `uuid` NOT NULL FK → accounts | |
| `role_id` | `uuid` NOT NULL FK → roles | |
| `market_code` | `citext` NOT NULL | role's effective market; `platform` for platform scope |
| `granted_by_account_id` | `uuid` NULL FK → accounts | null for bootstrap |
| `granted_at` | `timestamptz` NOT NULL | |

PK `(account_id, role_id, market_code)`.

---

### 16. `identity.authorization_audit`
Every permission check that *denied* emits a row (allow-hits sampled at 1 % to keep volume reasonable). Consumed by SC-007 ("100 % of permission denials observable").

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `occurred_at` | `timestamptz` NOT NULL | |
| `account_id` | `uuid` NULL | null for anonymous |
| `surface` | `citext` NOT NULL | |
| `permission_code` | `citext` NOT NULL | |
| `decision` | `citext` NOT NULL | `allow` \| `deny` |
| `reason_code` | `citext` NOT NULL | `ok`, `role_missing`, `scope_mismatch`, `market_mismatch`, `mfa_not_satisfied` |
| `correlation_id` | `uuid` NOT NULL | from spec 003's `CorrelationIdMiddleware` |

---

### 17. `identity.rate_limit_events` (observability — not state)
Append-only; powers alerts on abuse patterns per FR-019 / Edge Case #11. State for actual rate limiting lives in-process.

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | |
| `policy_code` | `citext` NOT NULL | |
| `scope_key_hash` | `bytea` NOT NULL | |
| `blocked_at` | `timestamptz` NOT NULL | |
| `surface` | `citext` NOT NULL | |

Purged after 30 days.

---

## State Machines (9)

Each block lists: **States**, **Transitions** `(from → to, trigger, actor, failure, retry)`.

### SM-1 · Account
- States: `pending_email_verification`, `active`, `disabled`, `deleted`.
- `pending_email_verification → active` · trigger `email_verified` · actor `self` · failure: token expired → stay; retry by re-requesting verification.
- `active → disabled` · trigger `admin_disable` · actor `admin:platform.super_admin` · failure: not authorized → reject; no retry.
- `disabled → active` · trigger `admin_enable` · same auth · no retry.
- `active → deleted` / `disabled → deleted` · trigger `account_delete` · actor `admin:platform.super_admin` · soft-delete; no retry.

### SM-2 · Session
- States: `active`, `idle_expired`, `absolute_expired`, `revoked`.
- `active → revoked` · trigger `sign_out | admin_revoke | password_change | security_incident` · actor `self | admin | system`.
- `active → idle_expired` · trigger `idle_tick` · actor `system` · automatic.
- `active → absolute_expired` · trigger `absolute_tick` · actor `system` · customer 30 d / admin 8 h.
- No reverse transitions.

### SM-3 · RefreshToken
- States: `active`, `consumed`, `revoked`.
- `active → consumed` · trigger `refresh_used` · actor `self` · failure: reuse attempt → revokes *parent session* + emits security audit; no retry.
- `active → revoked` · trigger `session_revoke` · actor `self | admin | system` · cascade from session.

### SM-4 · OtpChallenge
- States: `pending`, `verified`, `expired`, `exhausted`, `cancelled`.
- `pending → verified` · trigger `code_match` · actor `self`.
- `pending → exhausted` · trigger `attempts_maxed (5)` · actor `system` · no retry; new challenge required.
- `pending → expired` · trigger `expiry_tick` · actor `system`.
- `pending → cancelled` · trigger `superseded` · actor `self` when requesting a new challenge.

### SM-5 · EmailVerificationChallenge
- States: `pending`, `verified`, `expired`, `cancelled`.
- Transitions analogous to OTP, without attempts counter (token-only).

### SM-6 · PasswordResetToken
- States: `pending`, `consumed`, `expired`, `cancelled`.
- `pending → consumed` · trigger `reset_complete` · actor `self` · triggers revocation of all active sessions for the account (FR-015).

### SM-7 · AdminInvitation
- States: `pending`, `accepted`, `expired`, `revoked`.
- `pending → accepted` · trigger `invitation_accept` · actor `invited_self`.
- `pending → revoked` · trigger `admin_revoke_invitation` · actor `admin:platform.super_admin`.

### SM-8 · AdminMfaFactor
- States: `pending_confirmation`, `active`, `revoked`.
- `pending_confirmation → active` · trigger `totp_echo_verified` · actor `self`.
- `active → revoked` · trigger `user_rotate | admin_reset | security_incident` · retry: new factor may be enrolled.

### SM-9 · IdentityLockoutState (per `(account_id, reason)`)
- States: `clear`, `tracking`, `locked`.
- `clear → tracking` · trigger `first_failure` · actor `system`.
- `tracking → tracking` · trigger `subsequent_failure_within_window` · actor `system`.
- `tracking → clear` · trigger `window_expired_without_threshold` · actor `system`.
- `tracking → locked` · trigger `threshold_crossed` · actor `system`.
- `locked → clear` · trigger `lockout_window_expired | admin_unlock` · actor `system | admin`.

Each transition emits an `audit_log_entries` row with the machine name, before/after state, trigger, actor, correlation-id.

---

## ERD (logical, abbreviated)

```
accounts 1─* sessions 1─* refresh_tokens
accounts 1─* otp_challenges
accounts 1─* email_verification_challenges
accounts 1─* password_reset_tokens
accounts 1─* admin_mfa_factors 1─* admin_mfa_replay_guard
accounts 1─* account_roles *─1 roles *─* permissions (via role_permissions)
accounts 1─* lockout_state
accounts 1─* authorization_audit
admin_invitations *─1 roles
refresh_tokens  ←  revoked_refresh_tokens (via token_hash)
```

Full ERD export is generated by the Phase 1A ERD pipeline in the `/speckit-tasks` pass.
