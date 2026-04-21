# Implementation Plan: Identity and Access

**Branch**: `004-identity-and-access` | **Date**: 2026-04-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1B/004-identity-and-access/spec.md`

## Summary

Deliver the tiered identity-and-access layer that every subsequent Phase 1B/1C/1D spec consumes: customer and admin registration/sign-in, OTP and email verification, TOTP-backed admin MFA (super-admin + finance), server-revocable rotating JWT sessions with separate customer/admin surfaces, password reset, RBAC primitives, audit-logged authorization, and a bootstrap path that is safe in Dev (seeder) *and* in Staging/Prod (operator CLI one-shot). No UI — shipping clinical HTTP surfaces + database migrations + module seam ready for spec 014 (customer app shell) and 015 (admin foundation) to consume.

The module exposes **9 state machines** required by Principle 24: Session, RefreshToken, OtpChallenge, EmailVerificationChallenge, PasswordResetToken, AdminInvitation, AdminMfaFactor, IdentityLockoutState, Account. Every transition is audited through spec 003's `IAuditEventPublisher`. Market is assigned at registration (FR-001a) and carried as `market_code` on every customer-owned identity entity per Principle 5/ADR-010; admins carry the `platform` sentinel.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16
**Primary Dependencies**:
- `Konscious.Security.Cryptography.Argon2` v1.3.x — Argon2id password hashing (customer memory=64 MiB/iterations=3/parallelism=2; admin memory=96 MiB/iterations=4/parallelism=2)
- `Microsoft.AspNetCore.Authentication.JwtBearer` v9.x — JWT bearer validation with custom `ISecurityTokenValidator` hooking the revocation store
- `Otp.NET` v1.4.x — RFC 6238 TOTP for admin MFA (±1 window, 30 s period, SHA-1 per RFC compat)
- `libphonenumber-csharp` v8.x — E.164 normalization + country-code inference for market pre-fill
- `MediatR` v12.x + `FluentValidation` v11.x (ADR-003 per-feature handlers)
- `Microsoft.EntityFrameworkCore` v9.x (ADR-004; EF Core code-first migrations)
- `System.Threading.RateLimiting` (built-in) — sliding-window limiter wrappers, tiered per surface
- Offline HIBP top-100k breach list (static file, updated via separate CLI task; blocks compromised passwords per FR-008)
- Spec 003 consumables: `IAuditEventPublisher`, `MessageFormat.NET` (ICU AR/EN), Serilog, `CorrelationIdMiddleware`, `SaveChangesInterceptor` audit hook

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). 17 new tables across identity / session / OTP / admin-provisioning / MFA / RBAC / rate-limit domains. Reuses spec 003's `audit_log_entries`. In-process bloom filter mirrors the refresh-token revocation set to cut DB chatter on the hot path.

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers for Postgres (no SQLite shortcut — spec 003 rejected it). Contract tests assert HTTP shape parity between `spec.md` Acceptance Scenarios and live handlers. Property tests for Argon2id tuning (cost budget), TOTP window (clock skew tolerance), and rate-limit correctness.

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — spec 014/015 take those.

**Project Type**: .NET vertical-slice module under the modular monolith (ADR-023).

**Performance Goals**: Customer sign-in p95 ≤ 300 ms (excludes Argon2id cost, ~120 ms at tuned parameters) — SC-002. Admin sign-in p95 ≤ 400 ms (excludes Argon2id + TOTP) — SC-002a. Token revocation propagates ≤ 60 s across all instances — SC-004.

**Constraints**:
- Zero plaintext secrets at rest or in logs (FR-016, FR-017) — enforced via `SaveChangesInterceptor` + Serilog destructuring filter
- Anti-enumeration constant-time response paths for registration + sign-in + password reset + email verification (SC-010, Edge Case #2)
- No session cookies (JWT-only); refresh-token rotation is mandatory on every use (FR-012)
- Dev-only OTP/email sink MUST NOT compile into Staging/Prod binaries (compile-time guard + startup assertion)
- Super-admin bootstrap MUST be idempotent in every environment (Dev seeder re-runnable; CLI `seed-admin` one-shot with explicit `--force` reset)

**Scale/Scope**: 35 HTTP endpoints (18 customer, 17 admin), ~42 FRs, 13 SCs, 9 key entities, 9 state machines, 17 tables. Target: 200 concurrent sign-ins per surface, 50k daily OTP challenges per market at peak.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Unauth browse/search/view remains; auth required for checkout. This spec delivers the auth wall only, no page-visibility shifts. | PASS |
| P4 Arabic/RTL editorial | Every user-facing message (email subject, OTP SMS, error reason) is ICU-keyed with AR+EN variants via spec 003's MessageFormat.NET. No English-only copy ships. | PASS |
| P5 Market Configuration | `market_code` on customer identities (FR-001a); admin carries `platform`. Password policy, OTP length, rate limits, session TTLs all driven by surface+market config, not hardcoded in handlers. | PASS |
| P6 Multi-vendor-ready | RBAC scoped by `role_scope ∈ {platform, market, vendor}`; vendor scope unused in launch but schema reserves it. No single-vendor wording in role names. | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9. No deviation. | PASS |
| P23 Architecture | Vertical slice under `Modules/Identity/`; no premature service extraction; seam for future OTP/SSO providers behind `IOtpChallengeDispatcher` and `IAdminFederatedIdentityProvider` (null implementations for launch). | PASS |
| P24 State Machines | 9 explicit state machines enumerated below. Each: states, transition rules, triggers, actors, failure handling, retries. Documented in `data-model.md`. | PASS |
| P25 Data & Audit | Every admin action + every verification/MFA/revocation/role-change transition emits an audit event via spec 003's `IAuditEventPublisher`. Actor, timestamp, correlation-id, before/after captured. | PASS |
| P27 UX Quality | No UI here, but error *payloads* carry structured reason codes (`identity.lockout.active`, `identity.otp.expired`, `identity.mfa.required`) that spec 014/015 consume for copy + state rendering. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request/response/errors; no vague "standard auth flow" language. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1B Milestone 2. No scope creep into Phase 1C UI. | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/Identity/`. No new repo. | PASS |
| ADR-003 Vertical slice | One folder per feature slice under `Modules/Identity/Customer/` and `Modules/Identity/Admin/`, each with Request/Handler/Validator/Endpoint. | PASS |
| ADR-004 EF Core 9 | Code-first migrations in `Modules/Identity/Persistence/Migrations/`. Soft-delete query filter on `AccountStatus = Deleted`. Audit via `SaveChangesInterceptor` (shared from spec 003). | PASS |
| ADR-009 OTP providers | ADR is **Proposed**, not Accepted. Launch ships `ConsoleOtpDispatcher` (Dev) and `UnifonicOtpDispatcher` / `SesEmailDispatcher` behind `IOtpChallengeDispatcher`. Provider selection deferred to Phase 1D spec 025 per spec §Out of Scope. No hard coupling. | PASS |
| ADR-010 KSA residency | All tables live in the KSA-region Postgres; no cross-region replication introduced here. | PASS |

**No violations** → Complexity Tracking section below documents *design choices that are intentionally non-obvious*, not violations.

## Project Structure

### Documentation (this feature)

```text
specs/phase-1B/004-identity-and-access/
├── plan.md              # This file
├── research.md          # Phase 0 — library selection, Argon2id tuning, revocation-store design
├── data-model.md        # Phase 1 — 17 tables, 9 state machines, ERD
├── contracts/           # Phase 1 — HTTP surface for customer + admin
│   └── identity-and-access-contract.md
├── quickstart.md        # Phase 1 — implementer walkthrough
├── checklists/
│   └── requirements.md  # quality gate (all 20 items pass)
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   └── Identity/
│       ├── Primitives/                # cross-cutting types consumed by Customer/ + Admin/
│       │   ├── SurfaceKind.cs                    # { Customer, Admin }
│       │   ├── MarketCode.cs                     # { eg, ksa, platform }
│       │   ├── PasswordHasher.cs                 # Argon2id wrapper, surface-tiered params
│       │   ├── BreachListChecker.cs              # offline HIBP top-100k
│       │   ├── JwtIssuer.cs                      # separate signing keys per surface
│       │   ├── RefreshTokenRevocationStore.cs    # Postgres + in-proc bloom filter
│       │   ├── ITokenRevocationCache.cs
│       │   ├── IOtpChallengeDispatcher.cs        # seam for ADR-009 provider plug-in
│       │   ├── ConsoleOtpDispatcher.cs           # Dev-only, compile-time guarded
│       │   ├── RateLimitPolicies.cs              # tiered per surface+endpoint
│       │   └── StateMachines/                    # 9 state machine definitions
│       │       ├── SessionStateMachine.cs
│       │       ├── RefreshTokenStateMachine.cs
│       │       ├── OtpChallengeStateMachine.cs
│       │       ├── EmailVerificationStateMachine.cs
│       │       ├── PasswordResetStateMachine.cs
│       │       ├── AdminInvitationStateMachine.cs
│       │       ├── AdminMfaFactorStateMachine.cs
│       │       ├── IdentityLockoutStateMachine.cs
│       │       └── AccountStateMachine.cs
│       ├── Customer/
│       │   ├── Register/                         # slice: Request/Validator/Handler/Endpoint/Tests
│       │   ├── ConfirmEmail/
│       │   ├── RequestOtp/
│       │   ├── VerifyOtp/
│       │   ├── SignIn/
│       │   ├── RefreshSession/
│       │   ├── SignOut/
│       │   ├── RequestPasswordReset/
│       │   ├── CompletePasswordReset/
│       │   ├── ChangePassword/
│       │   ├── ListSessions/
│       │   └── RevokeSession/
│       ├── Admin/
│       │   ├── AcceptInvitation/
│       │   ├── SignIn/
│       │   ├── CompleteMfaChallenge/
│       │   ├── EnrollTotp/
│       │   ├── RotateTotp/
│       │   ├── StepUpOtp/
│       │   ├── RefreshSession/
│       │   ├── SignOut/
│       │   ├── InviteAdmin/                      # super-admin only
│       │   ├── RevokeAdminSession/
│       │   ├── ChangeAdminRole/
│       │   ├── ListAdminMfaFactors/
│       │   └── ResetAdminMfa/
│       ├── Authorization/                        # RBAC: Role, Permission, PolicyEvaluator, IAuthorizationAuditEmitter
│       ├── Entities/                             # EF entities for all 17 tables
│       ├── Persistence/
│       │   ├── IdentityDbContext.cs
│       │   ├── Configurations/                   # IEntityTypeConfiguration per entity
│       │   └── Migrations/
│       └── Seeding/
│           ├── IdentityReferenceDataSeeder.cs    # roles, permissions, role→permission matrix (Dev+Staging+Prod)
│           ├── IdentityDevDataSeeder.cs          # Dev super-admin + sample customers (Dev only, SeedGuard)
│           └── SeedAdminCliCommand.cs            # operator one-shot for Staging/Prod bootstrap (FR-024pre)
└── tests/
    └── Identity.Tests/
        ├── Unit/                                 # Argon2id tuning, TOTP skew, state machine transitions
        ├── Integration/                          # WebApplicationFactory + Testcontainers Postgres
        └── Contract/                             # asserts every Acceptance Scenario from spec.md
```

**Structure Decision**: Vertical-slice module under the modular monolith (ADR-003 / ADR-023). Customer and Admin surfaces share `Primitives/` but keep separate slice folders so that the blast-radius separation asserted in SC-011 ("customer token never accepted by admin surface and vice versa") is visible in the directory tree, not just in the token validator. `Seeding/` reuses the A1 `ISeeder` + `SeedGuard` contracts and adds the `seed-admin` CLI one-shot for the tiered bootstrap per FR-024pre / Clarification Q3.

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `SurfaceKind`, `MarketCode`, Argon2id wrapper, breach list, JWT issuer, revocation store + bloom cache, rate-limit policies, 9 state machine definitions | Foundation for all slices |
| B. Persistence + migrations | 17 entities, `IdentityDbContext`, EF configurations, initial migration, soft-delete filter wiring | Unblocks all slices |
| C. Customer slices | register → confirm-email → request-otp → verify-otp → sign-in → refresh → sign-out → password-reset → change-password → list-sessions → revoke-session | FR-001…FR-015, FR-018..FR-020 |
| D. Admin slices + TOTP | accept-invitation → TOTP enroll → sign-in → mfa-challenge → step-up → refresh → sign-out → invite-admin → revoke-admin-session → change-role → list-mfa-factors → reset-mfa | FR-022..FR-024e |
| E. RBAC | Role, Permission, role→permission seed, PolicyEvaluator, `[RequirePermission]` attribute, authorization audit emitter | FR-025..FR-028 |
| F. Bootstrap + seeder | IdentityReferenceDataSeeder (all envs), IdentityDevDataSeeder (Dev-gated), SeedAdminCliCommand (Staging/Prod) | FR-024pre |
| G. Rate-limit + revocation cache | Sliding-window limiter policies wired per endpoint, revocation bloom filter refresh loop | FR-018, FR-019, SC-004 |
| H. Contracts | Regenerate OpenAPI, assert contract test suite green | Guardrail #2 |
| I. AR/EN editorial | Every user-facing reason code resolved through MessageFormat.NET; AR strings reviewed editorially (needs-ar-editorial-review flag on PR) | P4 |
| J. Integration / DoD | Full Testcontainers run, fingerprint, DoD checklist, audit-log spot-check script | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Argon2id parameters tiered by surface (customer 64 MiB×3×2 / admin 96 MiB×4×2) | Admin is a blast-radius multiplier (Clarification Q4); justifies ~1.5× cost budget | Single parameter set would either under-protect admin or over-tax customer sign-in latency (SC-002) |
| Server-revocable JWT refresh store + in-proc bloom filter | FR-012, SC-004 require revocation in ≤ 60 s across instances; stateless JWT alone cannot satisfy this | Pure stateless JWT fails revocation SLA; pure opaque tokens forfeit JWT tooling customer SDK will want in spec 014 |
| Constant-time anti-enumeration branches on register/sign-in/reset/verify | SC-010 ("enumeration success ≤ 5 %"); Edge Case #2 | Fast-fail on unknown email leaks existence; measurable via side-channel timing tests |
| TOTP ±1 window (30 s period) | Absorbs device clock skew without materially weakening factor | ±0 rejects legitimate users on skewed devices; ±2+ extends attacker window beyond RFC 6238 guidance |
| Dev OTP/email sinks compile-time guarded by `#if DEBUG_SEED || DEV_ENV` and startup-asserted out in Staging/Prod | FR-017 zero-plaintext + FR-024pre tiered bootstrap safety | Single runtime flag risks accidental Prod enablement via env-var drift; compile-time guard + startup assertion is two-factor safety |
| Separate JWT signing keys + issuer/audience per surface | SC-011 requires customer↔admin token non-interchangeability; even with a single key, header rejection is the defensive posture | Shared key with claim-based surface check works but collapses to one mistake in claim validation; separate keys make cross-surface acceptance a cryptographic impossibility |
| `seed-admin` as a standalone CLI (not an HTTP endpoint) in Staging/Prod | Bootstrap must exist before any admin HTTP route is authenticatable; endpoint-based bootstrap is a chicken-and-egg security hole | HTTP bootstrap endpoint either leaks a backdoor or requires a pre-existing admin — circular |
| `market_code` required on customer identity rows; admins use `platform` sentinel | P5 + ADR-010 per-market logical partitioning applies to *customer* data; admin is platform-global | Making admins per-market complicates super-admin bootstrap and role-scope evaluation |
| Reserve `role_scope = vendor` enum value, never populate in launch | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2 | Omitting the value now forces a disruptive enum migration when vendor roles land |
| Primitives split into surface-agnostic `Primitives/` vs. surface-specific slices | ADR-003 per-feature slices remain clean; shared security code doesn't duplicate | Duplicating Argon2id/JWT/rate-limit logic across Customer/ and Admin/ invites drift — the more dangerous failure mode than a shared helper |
