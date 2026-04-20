# Data Model — Identity and Access (004)

**Feature**: `specs/phase-1B/004-identity-and-access/spec.md`
**Plan**: `./plan.md`
**Date**: 2026-04-20

All tables live in schema `identity`. Every tenant-owned table carries `market_code` per ADR-010. `created_at`, `updated_at` present on every row unless noted. Soft-delete via `deleted_at` query filter only where indicated.

---

## 1. `customers`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default `gen_random_uuid()` | Stable internal id; retained after anonymization. |
| `email` | `citext` | NULL allowed | Lower-cased + trimmed on write. |
| `phone_e164` | `text` | NULL allowed | Normalized to E.164. |
| `password_hash` | `text` | NOT NULL until `status='anonymized'` | Argon2id (m=19MiB, t=2, p=1). |
| `status` | `customer_status` enum | NOT NULL, default `'pending-verification'` | See §1a. |
| `market_code` | `text` | NOT NULL, CHECK `market_code IN ('EG','KSA')` | ADR-010. |
| `preferred_locale` | `text` | NOT NULL, CHECK `preferred_locale IN ('ar','en')` | |
| `last_login_at` | `timestamptz` | NULL | |
| `lock_tier` | `smallint` | NOT NULL, default 0 | 0, 1 (time), 2 (proof-required) — see `FR-008a`. |
| `locked_until` | `timestamptz` | NULL | Populated when `lock_tier = 1`. |
| `lock_history_window_start` | `timestamptz` | NULL | Start of the rolling-window for lockout escalation. |
| `deletion_requested_at` | `timestamptz` | NULL | FR-030. |
| `deletion_requested_by` | `uuid` | FK `admins(id)` nullable | Admin who recorded the customer request. |
| `scheduled_anonymization_at` | `timestamptz` | NULL | Grace-period end. |
| `anonymized_at` | `timestamptz` | NULL | |
| `created_at` | `timestamptz` | NOT NULL, default `now()` | |
| `updated_at` | `timestamptz` | NOT NULL, default `now()` | |

**Check**: `email IS NOT NULL OR phone_e164 IS NOT NULL` whenever `status <> 'anonymized'`.

**Indexes**:
- Partial unique: `UNIQUE (lower(email)) WHERE email IS NOT NULL AND status <> 'anonymized'` — FR-006a.
- Partial unique: `UNIQUE (phone_e164) WHERE phone_e164 IS NOT NULL AND status <> 'anonymized'` — FR-006a.
- `(market_code, status)` — admin filters.
- `(status, scheduled_anonymization_at)` — grace-period sweeper.

### 1a. `customer_status` enum

```text
pending-verification  -- created, contact not yet verified
active                -- verified, usable
locked                -- progressive lockout (details in lock_tier)
deletion-requested    -- erasure requested; sessions revoked; grace period running
anonymized            -- PII cleared; id retained for 011/012 referential integrity
disabled              -- admin-set flag; parallel to the above
```

State transitions (FR-008a, FR-030, FR-031):

```text
pending-verification → active            (successful contact verification)
pending-verification → disabled          (admin action)
active                ⇄ locked            (failed-login threshold / time unlock / proof unlock)
active                → deletion-requested (admin on customer request)
deletion-requested    → anonymized        (scheduler after grace period)
any                   → disabled          (admin action)
disabled              → active            (admin action)
```

---

## 2. `admins`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `email` | `citext` | NOT NULL, UNIQUE | |
| `password_hash` | `text` | NOT NULL | |
| `status` | `admin_status` enum | NOT NULL, default `'active'` | `active`, `disabled`. |
| `display_name` | `text` | NOT NULL | |
| `preferred_locale` | `text` | NOT NULL | `ar` or `en`. |
| `last_login_at` | `timestamptz` | NULL | |
| `last_stepup_at` | `timestamptz` | NULL | |
| `created_by` | `uuid` | FK `admins(id)` | Bootstrap admin seeded via migration has NULL. |
| `created_at` | `timestamptz` | NOT NULL, default `now()` | |
| `updated_at` | `timestamptz` | NOT NULL, default `now()` | |

**Indexes**: Unique on `email`.

---

## 3. `roles`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `key` | `text` | NOT NULL, UNIQUE | Stable, e.g. `catalog-editor`. |
| `display_name_ar` | `text` | NOT NULL | |
| `display_name_en` | `text` | NOT NULL | |
| `scope` | `role_scope` enum | NOT NULL, default `'global'` | `global` at launch; `vendor` reserved for Phase 2 (FR-028). |
| `scope_ref_id` | `uuid` | NULL | Null for `global`; future vendor id for `vendor`-scoped. |
| `is_system` | `boolean` | NOT NULL, default false | Seeded system roles cannot be deleted. |
| `created_at`, `updated_at` | `timestamptz` | | |

**Indexes**: `UNIQUE (key)`; `(scope, scope_ref_id)`.

### Seed roles (FR-021)

`super-admin`, `catalog-editor`, `inventory-editor`, `orders-ops`, `customers-ops`, `verification-reviewer`, `support-agent`, `finance-viewer`.

---

## 4. `permissions`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `key` | `text` | NOT NULL, UNIQUE | e.g. `catalog.read`, `orders.refund.initiate`. |
| `display_label_ar` | `text` | NOT NULL | |
| `display_label_en` | `text` | NOT NULL | |
| `domain` | `text` | NOT NULL | e.g. `catalog`, `orders`, `identity`. |
| `created_at`, `updated_at` | `timestamptz` | | |

**Indexes**: `UNIQUE (key)`; `(domain)`.

### Seed permission catalog (abridged)

`identity.admin.create`, `identity.admin.enable`, `identity.admin.disable`, `identity.role.manage`, `identity.permission.manage`, `catalog.read`, `catalog.write`, `catalog.publish`, `inventory.read`, `inventory.adjust`, `orders.read`, `orders.refund.initiate`, `customers.read`, `customers.anonymize`, `verification.review`, `support.read`, `support.reply`, `finance.export`.

---

## 5. `role_permissions`

Junction table.

| Column | Type | Constraints |
|---|---|---|
| `role_id` | `uuid` | PK part, FK `roles(id)` ON DELETE CASCADE |
| `permission_id` | `uuid` | PK part, FK `permissions(id)` |
| `granted_at` | `timestamptz` | NOT NULL, default `now()` |
| `granted_by` | `uuid` | FK `admins(id)` nullable (nullable for seed) |

---

## 6. `admin_role_assignments`

| Column | Type | Constraints |
|---|---|---|
| `admin_id` | `uuid` | PK part, FK `admins(id)` ON DELETE CASCADE |
| `role_id` | `uuid` | PK part, FK `roles(id)` |
| `assigned_at` | `timestamptz` | NOT NULL |
| `assigned_by` | `uuid` | FK `admins(id)` |
| `reason` | `text` | NULL |

On change, `admins.perm_ver` (cached in `admin_perm_version` table) increments.

---

## 7. `admin_perm_version`

| Column | Type | Constraints |
|---|---|---|
| `admin_id` | `uuid` | PK, FK `admins(id)` |
| `perm_ver` | `bigint` | NOT NULL, default 1 |
| `updated_at` | `timestamptz` | NOT NULL |

Incremented on any change to `admin_role_assignments` for the admin or any change to `role_permissions` for a role the admin holds. Carried into admin access tokens (research §8).

---

## 8. `sessions`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `surface` | `surface_enum` | NOT NULL — `customer` or `admin` |
| `subject_id` | `uuid` | NOT NULL — customer or admin id depending on surface |
| `device_summary` | `text` | NULL |
| `user_agent` | `text` | NULL |
| `ip_hash` | `bytea` | NULL — SHA-256 of IP + per-env pepper |
| `created_at` | `timestamptz` | NOT NULL |
| `last_refreshed_at` | `timestamptz` | NOT NULL |
| `revoked_at` | `timestamptz` | NULL |
| `revoke_reason` | `text` | NULL |

**Indexes**:
- `(surface, subject_id, revoked_at)` — session-list lookup.
- `(surface, subject_id) WHERE revoked_at IS NULL` — admin single-active enforcement.

**Rule (FR-011b)**: On successful admin login, mark all prior `revoked_at IS NULL` sessions for that admin `revoked_at = now(), revoke_reason = 'superseded-by-new-login'` within the same transaction that creates the new session.

---

## 9. `refresh_tokens`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `session_id` | `uuid` | FK `sessions(id)` ON DELETE CASCADE |
| `token_hash` | `bytea` | NOT NULL, UNIQUE — SHA-256 of the raw token |
| `issued_at` | `timestamptz` | NOT NULL |
| `expires_at` | `timestamptz` | NOT NULL |
| `used_at` | `timestamptz` | NULL |
| `revoked_at` | `timestamptz` | NULL |
| `replaced_by` | `uuid` | FK `refresh_tokens(id)` NULL |

**Rule**: Refresh rotates — on use, the row's `used_at` is set and a new row is created with `replaced_by` threading the chain. Re-use of a `used_at`-marked token is a security event (FR-010): revoke the entire chain and emit `SessionRevoked {reason: refresh-reuse-detected}`.

---

## 10. `otp_records`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `channel` | `otp_channel` enum | NOT NULL — `email`, `phone` |
| `target_value` | `text` | NOT NULL — normalized |
| `subject_id` | `uuid` | NULL until after verification binds the OTP to a specific customer or admin |
| `purpose` | `otp_purpose` enum | NOT NULL — `registration-verify`, `password-reset`, `lockout-unlock-stepup`, `admin-stepup`, `verification-resend` |
| `code_hash` | `bytea` | NOT NULL — HMAC-SHA256(code, per-env pepper) |
| `provider_key` | `text` | NOT NULL — e.g. `test`, `twilio` |
| `attempt_count` | `smallint` | NOT NULL, default 0 |
| `max_attempts` | `smallint` | NOT NULL, default 3 |
| `expires_at` | `timestamptz` | NOT NULL |
| `consumed_at` | `timestamptz` | NULL |
| `invalidated_at` | `timestamptz` | NULL |
| `market_code` | `text` | NOT NULL |
| `created_at` | `timestamptz` | NOT NULL |

**Indexes**: `(channel, target_value, consumed_at, invalidated_at, expires_at)` for hot verify path.

---

## 11. `password_reset_tokens`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `subject_id` | `uuid` | NOT NULL |
| `surface` | `surface_enum` | NOT NULL |
| `token_hash` | `bytea` | NOT NULL, UNIQUE |
| `issued_at` | `timestamptz` | NOT NULL |
| `expires_at` | `timestamptz` | NOT NULL |
| `consumed_at` | `timestamptz` | NULL |

**Rule (FR-015)**: On successful consume, all sessions for `subject_id` on `surface` are revoked in the same transaction.

---

## 12. `stepup_assertions`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `admin_id` | `uuid` | FK `admins(id)` |
| `issued_at` | `timestamptz` | NOT NULL |
| `expires_at` | `timestamptz` | NOT NULL — default `issued_at + 5 min` |
| `consumed_at` | `timestamptz` | NULL |

Presented via `X-StepUp-Token` header for `[SensitiveAdminAction]` endpoints (research §14).

---

## 13. `identity_rate_limits`

| Column | Type | Constraints |
|---|---|---|
| `bucket_key` | `text` | PK part — e.g. `otp:phone:+9665...` |
| `action` | `text` | PK part — `otp-send`, `login-attempt`, `reset-request` |
| `window_start` | `timestamptz` | PK part |
| `count` | `int` | NOT NULL |
| `window_seconds` | `int` | NOT NULL |

A nightly job prunes rows whose `window_start + window_seconds < now() - 1d`.

---

## 14. Domain events emitted to audit-log (owned by spec 003)

Enumerated in [contracts/events.md](./contracts/events.md). Identity publishes; the audit-log module persists. `actor_id`, `target_id`, `action_key`, `reason`, `before`, `after`, `correlation_id`, `market_code`, `occurred_at`.

---

## 15. Referential integrity after anonymization

- `orders` (spec 011) and `tax_invoices` (spec 012) reference `customers.id` as a plain FK; `status='anonymized'` does not delete the row, so the FK stays intact (Clarification Q5 + FR-030).
- `audit_events.actor_id` and `target_id` continue to point at the stable id; before/after PII snapshots are replaced with `"[anonymized]"` strings at anonymization time. This is handled by the `CustomerAnonymized` handler in the audit-log module.

---

## 16. Multi-vendor readiness hooks (Principle 6, FR-028)

- `roles.scope` and `roles.scope_ref_id` already carry the shape for vendor-scoped roles with no schema migration needed in Phase 2.
- No other identity table needs a `vendor_id` column in Phase 1B; customers and admins remain platform-scoped and vendor assignment is a later role-scope association.
