# Phase 0 Research — Identity and Access (004)

**Feature**: `specs/phase-1B/004-identity-and-access/spec.md`
**Plan**: `./plan.md`
**Date**: 2026-04-20

Each Technical Context item is resolved here. No `NEEDS CLARIFICATION` markers remain.

---

## 1. Password hashing algorithm and cost

- **Decision**: Argon2id with `m = 19 MiB`, `t = 2`, `p = 1`, 16-byte random salt, 32-byte output.
- **Rationale**: Meets OWASP Password Storage Cheat Sheet 2025 guidance; matches spec's SC-006 (≥ 100 ms verify at baseline vCPU). Argon2id is memory-hard, resistant to GPU/ASIC attacks, and the constitution §4 already treats it as baseline (inherited from spec 003 shared foundations). Konscious.Security.Cryptography.Argon2 is the actively maintained .NET implementation compatible with .NET 9.
- **Alternatives considered**: PBKDF2-SHA512 (weaker memory-hardness; rejected), bcrypt (72-char password limit, no memory-hardness; rejected), scrypt (less adoption on .NET 9; higher maintenance risk).

## 2. OTP code length and validity window

- **Decision**: 6-digit numeric OTP, 5-minute validity, 3 attempt cap per OTP record, max 3 OTP sends per identifier per 15-minute window, max 10 OTP sends per identifier per 24-hour window.
- **Rationale**: 6-digit balances user-copy UX with 10⁶ brute-force space; attempts capped at 3 so space remaining after caps is < 10⁵. Validity window matches SMS/email delivery jitter for EG+KSA carriers. Rate-limit windows drawn from industry baseline (Auth0, Twilio Verify).
- **Alternatives considered**: 4-digit (too small), 8-digit (UX friction), 2-minute validity (too aggressive for international SMS delivery), sliding windows (adds complexity — fixed-window is sufficient for Phase 1).

## 3. Access token TTL and refresh token TTL per surface

- **Decision**:
  - Customer surface: access token 15 min, refresh token 30 days (rolling — rotation extends from each refresh).
  - Admin surface: access token 10 min, refresh token 12 h; idle timeout 30 min (re-auth required after 30 min of inactivity regardless of refresh TTL).
- **Rationale**: Customer values session longevity on mobile; admin values tighter blast radius per Principle 20. Idle-timeout is enforced server-side against `last_refreshed_at` on admin sessions. Meets FR-009 requirement that customer and admin token lifetimes differ.
- **Alternatives considered**: Unified 15 min / 14 day for both (rejected: violates Principle 20 spirit), customer 1 h access (rejected: too much re-auth on mobile), admin 1 h access (rejected: too much drift between role change and effective enforcement — SC-008 staleness budget wants shorter windows).

## 4. Lockout thresholds and progressive escalation (Clarification Q2)

- **Decision**: 5 consecutive failed login attempts on a given identifier within 15 min triggers a tier-1 lockout for 15 min (auto-unlock by time). A second lockout on the same account within a 24-h rolling window escalates to tier-2: account remains locked until owner completes either a password reset or a fresh OTP step-up on a verified contact channel. Every lockout and every unlock is audited (FR-008a) and eligible to trigger a suspicious-activity notification via spec 025.
- **Rationale**: Tier-1 is friendly enough for legitimate fat-fingers; tier-2 defeats credential-stuffing that times its replay. Audit + notification trigger aligns with SC-004.
- **Alternatives considered**: 3 attempts / 30 min (too aggressive), 10 attempts / 5 min (too lax), IP-based lockout (rejected: mobile carrier NATs would lock out legitimate users across carriers).

## 5. Concurrent-session policy (Clarification Q4)

- **Decision**: Customer surface — unlimited concurrent sessions, every session enumerated in `GET /customers/me/sessions` with device summary, `last_refreshed_at`, ip-hash; revocable individually. Admin surface — a successful admin login revokes all prior active sessions for that admin (newest-wins), writing `SessionRevoked {reason: new-login-supersedes}` to the audit log.
- **Rationale**: Matches Clarifications §4. Admin newest-wins reduces credential-sharing risk; customer unlimited-with-list matches commerce UX.
- **Alternatives considered**: Cap customer at N sessions (rejected: inconsistent with "Continue shopping on any device" UX), single-session on both (rejected: breaks mobile-plus-web commerce), IP-pinned sessions (rejected: mobile IP churn).

## 6. Data residency for identity records (Clarification Q3)

- **Decision**: All identity tables + OTP records + sessions + refresh tokens + emitted audit events reside in the ADR-010 single region (Azure Saudi Arabia Central) and are partitioned logically by `market_code` column on every row. No cross-region replication in Phase 1.
- **Rationale**: Matches ADR-010 (Accepted) and Clarifications §3. Reopening residency at the spec level would violate Principle 31 (constitution supremacy).
- **Alternatives considered**: Dual-region EG + KSA (deferred to Phase 2 at earliest — not in the roadmap), field-level encryption with per-market KMS (over-engineered at launch scale; revisit in 029 hardening if legal requires).

## 7. JWT signing algorithm and key management

- **Decision**: RS256 (RSA-SHA256) with 2048-bit keys per surface (distinct key pairs for customer issuer and admin issuer); keys stored in Azure Key Vault; `kid` header carried in every JWT; rotation every 90 days with 30-day overlap window for key carry-over.
- **Rationale**: RS256 supports distributed verification (Phase 1C Flutter + Next.js can verify without a shared secret); Key Vault meets residency requirement; kid + overlap gives zero-downtime rotation.
- **Alternatives considered**: HS256 with shared secret (rejected: secret sprawl, can't verify out-of-process cleanly), EdDSA (rejected: slightly less tooling maturity on .NET 9 and Flutter Dart jose libs at Phase 1B timeline).

## 8. Permission claim carriage and staleness bound (SC-008)

- **Decision**: Access tokens carry a `perm_ver` integer per user (bumped on any role/permission change affecting the user), plus a compact `perms` array of the user's currently-granted permission keys. Authorization middleware checks required permission against `perms` only; the RBAC service re-issues a refresh token with the new `perm_ver` + refreshed `perms` on the next token refresh. Maximum staleness = access-token TTL (15 min customer / 10 min admin). On a demonstrated emergency revoke, a fast-path admin action can force-invalidate all refresh tokens for a target user (FR-011 revocation).
- **Rationale**: Gives SC-008's "next refresh" guarantee with a measurable upper bound; avoids an auth-server round-trip on every request.
- **Alternatives considered**: Opaque tokens with introspection per request (rejected: fails latency budget), no claim carriage (rejected: requires DB read per request), long-lived permission cache (rejected: exceeds SC-008 staleness).

## 9. OTP provider abstraction contract (FR-023)

- **Decision**: `interface IOtpDeliveryProvider { Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct); string ProviderKey { get; } }` where `OtpSendRequest` carries target channel (email | phone), target value, OTP code, purpose, locale, market. Implementations: `TestOtpProvider` (records sends in a test-visible buffer; synthetic success), stubs for `EmailOtpProvider` and `SmsOtpProvider` returning `NotImplemented` until spec 025. The abstraction is registered via `.AddIdentityOtp(...)` with the provider key taken from configuration; callers never reference concrete providers.
- **Rationale**: Satisfies SC-007 swap test and FR-023. Provider key in config lets tests swap to a second dummy provider without a code change.
- **Alternatives considered**: Strategy pattern per channel (overkill at this scale), mediator-only (couples transport to MediatR and would block reuse from non-MediatR callers later).

## 10. Deletion-request / anonymization operation (Clarification Q5, FR-030/031)

- **Decision**: Account lifecycle states `pending-verification → active → locked → deletion-requested → anonymized`; `disabled` is a parallel admin-set flag. On `deletion-requested`, sessions are revoked and a configurable grace period (default 30 days, zero for admin-immediate flag) elapses before `anonymized`. `anonymized` transition: email + phone + name cleared; password hash + reset tokens + OTP records deleted; session rows deleted; audit events retained but `actor_id` remains the stable internal id — PII in `before`/`after` snapshots replaced with `"[anonymized]"`. Orders (spec 011) and tax invoices (spec 012) keep the stable internal id reference — no schema change required when 011/012 land.
- **Rationale**: Matches Clarifications §5; retains order/invoice integrity; opens the path for Phase 1.5 customer-facing UX without schema migration.
- **Alternatives considered**: Hard delete (breaks 011/012 immutability), mark-only deletion (fails erasure intent), tombstone row separate from customer (adds a join with no benefit).

## 11. Uniqueness enforcement (Clarification Q1, FR-006a)

- **Decision**: Partial unique index on `customers(lower(email))` where `status <> 'anonymized'` and partial unique index on `customers(phone_e164)` where `status <> 'anonymized'`. Both indexes independent of `market_code`. Normalization: email is lower-cased + trimmed; phone is normalized to E.164 before storage.
- **Rationale**: Partial index frees identifiers after anonymization (right-to-erasure). `lower()` enforces case-insensitive uniqueness without requiring client-side normalization. E.164 normalization avoids false positives on `+966 5...` vs `+9665...`.
- **Alternatives considered**: Full unique index (blocks re-registration forever after anonymization — violates erasure intent), application-level uniqueness check (race-prone).

## 12. Rate-limit storage

- **Decision**: Postgres-backed sliding-window counters keyed by `(identifier, action, window_start)`; a lightweight `identity_rate_limits` table with a TTL job that prunes rows older than the longest configured window. No Redis in Phase 1B.
- **Rationale**: Keeps Phase 1 infrastructure minimal (per ADR-001 single monorepo spirit); scale target of 200 concurrent sessions is well under Postgres's capacity for this pattern.
- **Alternatives considered**: In-memory (loses state on restart, doesn't scale horizontally), Redis (new dependency; deferred), cloud-provider WAF rate limit (doesn't key by identifier semantics we need).

## 13. Audit event shape and dispatch

- **Decision**: Identity emits domain events (records enumerated in `contracts/events.md`) via the MediatR notification pipeline; the spec-003 audit-log module subscribes and writes to the append-only `audit_events` table, capturing `actor_id`, `target_id`, `action_key`, `reason`, `before`, `after`, `correlation_id`, `occurred_at`, `market_code`. Identity does not write `audit_events` directly.
- **Rationale**: Honors spec 003 ownership. Decoupling keeps audit-log schema changes from rippling into identity handlers.
- **Alternatives considered**: Direct writes from handlers (couples audit shape to identity handlers), outbox pattern with external bus (over-engineered at launch).

## 14. Step-up re-authentication for admin sensitive actions (FR-017)

- **Decision**: Admin endpoints tagged `[SensitiveAdminAction]` require a `X-StepUp-Token` header carrying a short-lived (5-min) step-up assertion minted by `POST /admins/stepup` upon password re-prompt + optional admin-OTP. Sensitive launch set: role CRUD, role assignment, admin enable/disable, admin password reset on behalf of another user.
- **Rationale**: Clean, declarative, testable; matches FR-017 required re-auth.
- **Alternatives considered**: Re-prompt on every sensitive action inline (UX friction, harder to audit), SMS-only step-up (single-factor not meaningfully stronger than the live session).

## 15. Testing strategy

- **Decision**: Unit tests per handler + validator; integration tests via `WebApplicationFactory` against Testcontainers Postgres; contract tests snapshotting generated OpenAPI against the prior published artifact (gated by `contract-diff` CI from spec 002); property-based tests over the role × endpoint matrix covering every seeded permission × every seeded role; golden-file tests for AR + EN message copy; k6 smoke for login/refresh latency; fuzz test for OTP-verify replay and brute-force.
- **Rationale**: Matches Principle 28 AI-build standard and spec 001 DoD.
- **Alternatives considered**: Mocking the DB (rejected — spec-level memory established this as non-compliant in prior phases).

---

**All NEEDS CLARIFICATION resolved.** Proceed to Phase 1.
