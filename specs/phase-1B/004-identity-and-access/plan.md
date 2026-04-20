# Implementation Plan: Identity and Access

**Branch**: `phase_1B_creating_specs` (spec-creation branch; implementation branch will be spun off per PR) | **Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1B/004-identity-and-access/spec.md`

## Summary

Deliver the identity and access module for the dental commerce platform in Phase 1B: customer + admin authentication, email/phone registration with OTP, password reset, short-lived access token + rotating refresh token session model, and a fully declarative RBAC framework. Customer and admin auth run on two clearly separated surfaces (distinct issuer, audience, cookie scope, idle timeout, concurrency policy). An OTP provider abstraction is introduced now with a deterministic test provider; the real SMS/email providers are wired in Phase 1E spec 025. A deletion-request / anonymization lifecycle is scaffolded on the customer entity to honor right-to-erasure without breaking immutability of downstream orders (spec 011) and tax invoices (spec 012). All constitution principles 3, 4, 6, 8, 20, 24, 25 are tested, and ADR-010 single-region residency is enforced.

**Technical approach**: .NET 9 vertical-slice + MediatR (per ADR-003) living at `services/backend_api/Features/Identity/`, EF Core 9 code-first migrations (ADR-004) with soft-delete query filters and `SaveChangesInterceptor`-driven audit hooks, ASP.NET Core Identity primitives selectively adopted for password hashing + user store — JWTs issued/validated by custom services so we control issuer/audience per surface. Contracts published via `packages/shared_contracts` pipeline (from spec 003). OTP abstraction lands behind `IOtpDeliveryProvider` with a `TestOtpProvider` for dev/CI. Audit events flow through the spec-003 audit-log module. Localization + error envelope come from the spec-003 shared kernel.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend); TypeScript 5.x (Next.js admin app context, but implementation is spec 015); Dart 3.x (Flutter customer app context, but implementation is spec 014)
**Primary Dependencies**: ASP.NET Core 9, MediatR, FluentValidation, EF Core 9 (Npgsql), `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Identity` (for password hasher + user manager primitives only), `Konscious.Security.Cryptography.Argon2` for Argon2id password hashing (per constitution §4 Data & Audit baseline), Serilog for structured logging, OpenTelemetry for correlation-id propagation
**Storage**: PostgreSQL 16 single instance in Azure Saudi Arabia Central (ADR-010), schema `identity` inside the shared database; append-only `audit_events` table (owned by spec 003, consumed here); `market_code` column stamped on every tenant-owned table per ADR-010
**Testing**: xUnit + FluentAssertions; WebApplicationFactory-based integration tests; Testcontainers for throwaway Postgres; property-based testing via FsCheck for the role × permission matrix and session state machine; k6 smoke script for login/refresh latency budget
**Target Platform**: Linux container (Azure App Service / AKS) in Azure Saudi Arabia Central; .NET 9 runtime; HTTPS terminated at ingress; cookies `Secure; HttpOnly; SameSite=Strict` on customer surface, `SameSite=Lax` on admin surface
**Project Type**: web-service (backend API) with Phase 1C consumers (customer Flutter app + Next.js admin app)
**Performance Goals**: Login p95 ≤ 1 s (SC-002); refresh p95 ≤ 300 ms; Argon2id verify ≥ 100 ms of server CPU (SC-006); OTP send stub ≤ 100 ms; role × endpoint authorization check ≤ 5 ms in-process
**Constraints**: Single-region per ADR-010 — no cross-region replication for identity in Phase 1; no self-service admin registration; customer surface unlimited concurrent sessions, admin surface single active session; permission claim staleness ≤ session's next token refresh (SC-008); password hash verify cost fixed to stay ≥ 100 ms at the baseline vCPU profile documented in research.md
**Scale/Scope**: Launch target 50 k registered customers across EG + KSA, 200 concurrent authenticated sessions at peak, 50 admin users across ≤ 10 roles; OTP send budget ≤ 10 k/day pre-launch; audit events budget ≤ 500 k/month in Phase 1B

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| 3 — Auth gate before checkout | FR-003 blocks non-public actions until verified; spec 009/010 consume the gate via authenticated-actor + verification eligibility hook. | PASS |
| 4 — AR + EN editorial | FR-026 mandates AR + EN copy with editorial sign-off as DoD gate (SC-005). | PASS |
| 6 — Multi-vendor-ready | FR-028 + Role.scope field (`global` at launch, reserves `vendor`) carried in migrations. | PASS |
| 8 — Restricted-product eligibility hook | Out of this spec's API surface but the verification state hook is exposed as an authorization policy named `customer.verified-professional` for specs 005/009/010 to consume. | PASS |
| 20 — Admin app separation | FR-016 — two distinct JWT issuers + audiences + cookie scopes; Q2 admin single-session policy codified (FR-011b). | PASS |
| 24 — State machines | Customer-account state machine (pending-verification → active → locked → deletion-requested → anonymized → disabled) and session lifecycle are explicit and tested. | PASS |
| 25 — Audit on critical actions | FR-022 enumerates every auditable event; FR-008a mandates lockout/unlock audit. | PASS |
| 28 — AI-build standard | Vertical-slice per MediatR handler (ADR-003) maps 1:1 to FRs. | PASS |
| ADR-010 — Data residency | FR-029 explicitly locks single-region Azure Saudi Arabia Central for EG + KSA identity data. | PASS |

**No violations. No entries in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1B/004-identity-and-access/
├── plan.md                 # This file
├── spec.md                 # /speckit-specify output
├── research.md             # /speckit-plan Phase 0 output
├── data-model.md           # /speckit-plan Phase 1 output
├── quickstart.md           # /speckit-plan Phase 1 output
├── contracts/              # /speckit-plan Phase 1 output
│   ├── identity.openapi.yaml
│   └── events.md
├── checklists/
│   └── requirements.md     # /speckit-specify checklist
└── tasks.md                # /speckit-tasks output (NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
services/
└── backend_api/
    ├── Features/
    │   └── Identity/
    │       ├── Customers/
    │       │   ├── Register/                  # CreateCustomer command + handler + validator + endpoint
    │       │   ├── VerifyContact/             # OTP verify command + handler + endpoint
    │       │   ├── Login/                     # Login command + handler + endpoint
    │       │   ├── Refresh/                   # Refresh command + handler + endpoint
    │       │   ├── Logout/                    # Logout command + handler + endpoint
    │       │   ├── RequestPasswordReset/
    │       │   ├── CompletePasswordReset/
    │       │   ├── ListSessions/              # Customer session list + revoke-by-id
    │       │   ├── RequestDeletion/           # Scaffold per FR-030/FR-031 (admin-triggered at launch)
    │       │   └── Anonymize/                 # Irreversible PII clear (admin-triggered)
    │       ├── Admins/
    │       │   ├── Login/                     # Admin login with single-session policy
    │       │   ├── Refresh/
    │       │   ├── Logout/
    │       │   ├── Create/                    # Admin-to-admin creation (no self-service)
    │       │   ├── Enable/                    # audited
    │       │   ├── Disable/                   # audited, forces session revoke
    │       │   └── StepUp/                    # step-up re-auth for sensitive actions
    │       ├── Rbac/
    │       │   ├── Roles/                     # Create/Update/Delete role (audited)
    │       │   ├── Permissions/               # Seeded catalog; CRUD on role-to-permission grants
    │       │   ├── Assignments/               # Assign/revoke role to admin (audited)
    │       │   └── Seed/                      # Bootstrap seed-migration entrypoint
    │       ├── Otp/
    │       │   ├── Abstractions/              # IOtpDeliveryProvider, OtpPurpose enum
    │       │   ├── Providers/
    │       │   │   └── TestOtpProvider.cs     # deterministic dev/CI provider
    │       │   ├── Send/                      # OtpSendService + rate-limit enforcement
    │       │   └── Verify/                    # OtpVerifyService + replay/attempt guards
    │       ├── Tokens/
    │       │   ├── AccessTokenFactory.cs      # per-surface issuer/audience
    │       │   ├── RefreshTokenStore.cs       # rotation + revocation
    │       │   └── SessionService.cs          # surface-aware concurrency policy
    │       ├── Authorization/
    │       │   ├── PermissionRequirement.cs
    │       │   ├── PermissionHandler.cs
    │       │   ├── SurfaceRequirement.cs      # customer vs admin endpoint gate
    │       │   └── VerifiedProfessionalPolicy.cs
    │       ├── Persistence/
    │       │   ├── IdentityDbContext.cs       # or extends shared DbContext with module aggregate
    │       │   ├── Migrations/
    │       │   └── Configurations/            # Fluent API per entity
    │       ├── Events/                        # domain events published to audit-log module
    │       │   ├── CustomerRegistered.cs
    │       │   ├── LoginSucceeded.cs
    │       │   ├── LoginFailed.cs
    │       │   ├── AccountLocked.cs
    │       │   ├── AccountUnlocked.cs
    │       │   ├── SessionRevoked.cs
    │       │   ├── RoleAssigned.cs
    │       │   ├── RoleRevoked.cs
    │       │   ├── PasswordResetRequested.cs
    │       │   ├── PasswordResetCompleted.cs
    │       │   ├── DeletionRequested.cs
    │       │   └── CustomerAnonymized.cs
    │       └── Contracts/                     # DTOs generated into packages/shared_contracts
    └── Tests/
        ├── Identity.Unit/
        ├── Identity.Integration/
        └── Identity.Contract/

packages/
└── shared_contracts/
    └── identity/                              # OpenAPI-generated clients (Dart + TS) consumed by 014 / 015

scripts/
└── identity/
    └── seed-roles.sh                          # wrapper around Rbac/Seed entrypoint
```

**Structure Decision**: Vertical-slice per MediatR handler (ADR-003). Customer and admin flows live under parallel sub-folders inside `Features/Identity/` so the surface separation is visible in the file system, not only in middleware. `Otp`, `Tokens`, `Authorization`, `Persistence`, `Events`, and `Contracts` are shared inside the module only. Contracts emitted to `packages/shared_contracts/identity/` are consumed by Phase 1C specs 014 (Flutter) and 015 (Next.js admin) without re-implementation.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. All constitution gates pass.

---

## Phase 0 — Research

See [research.md](./research.md). Every NEEDS CLARIFICATION slot in Technical Context above is resolved there; findings feed Phase 1.

## Phase 1 — Design & Contracts

- [data-model.md](./data-model.md) — entities, fields, relationships, state machines, uniqueness, indexes, soft-delete policy.
- [contracts/identity.openapi.yaml](./contracts/identity.openapi.yaml) — public REST surface for customer + admin flows.
- [contracts/events.md](./contracts/events.md) — domain events emitted to the audit-log module.
- [quickstart.md](./quickstart.md) — how to run the module locally (test OTP provider wired, seed migration applied, sample login) and verify acceptance scenarios.

## Agent context update

Not applicable via `update-agent-context.sh` on this branch (branch-name check fails on spec-creation branches). Instead, the Phase 1 artifacts are the authoritative agent context for the implementing branch; `scripts/update-agent-context.sh claude` will be run on the implementation branch once it is cut with a conforming name.
