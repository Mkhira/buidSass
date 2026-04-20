---
description: "Task list for spec 004 · identity-and-access (Phase 1B)"
---

# Tasks: Identity and Access

**Input**: Design documents from `/specs/phase-1B/004-identity-and-access/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/identity.openapi.yaml, contracts/events.md, quickstart.md

**Tests**: INCLUDED. Research §15 mandates TDD coverage (unit, integration via Testcontainers Postgres, contract via OpenAPI diff, property-based over role × permission, k6 smoke for SC-002, Argon2id benchmark for SC-006, audit-coverage).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. User stories map to spec.md: US1 Registration + OTP, US2 Login + Session, US3 Password Reset, US4 Admin Surface, US5 RBAC Framework, US6 OTP Provider Abstraction.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps task to US1–US6 from spec.md
- Paths below follow plan.md's project structure (`services/backend_api/Features/Identity/...`).

## Path Conventions

- Backend source: `services/backend_api/Features/Identity/`
- Tests: `services/backend_api/Tests/Identity.{Unit,Integration,Contract}/`
- Shared contracts output: `packages/shared_contracts/identity/`
- Perf / bench tools: `tests/perf/identity/`, `services/backend_api/Tools/Bench/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wire the module into the monorepo and the existing backend solution.

- [ ] T001 Create feature folder skeleton at `services/backend_api/Features/Identity/` with empty sub-folders `Customers/`, `Admins/`, `Rbac/`, `Otp/`, `Tokens/`, `Authorization/`, `Persistence/`, `Events/`, `Contracts/` per plan.md Project Structure.
- [ ] T002 [P] Add `services/backend_api/Features/Identity/Identity.csproj` referencing the backend solution, MediatR, FluentValidation, EF Core 9 (Npgsql), `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Identity`, `Konscious.Security.Cryptography.Argon2`, Serilog, OpenTelemetry; register in `services/backend_api/backend_api.sln`.
- [ ] T003 [P] Create test projects `services/backend_api/Tests/Identity.Unit/Identity.Unit.csproj`, `services/backend_api/Tests/Identity.Integration/Identity.Integration.csproj` (xUnit + FluentAssertions + Testcontainers.PostgreSql), and `services/backend_api/Tests/Identity.Contract/Identity.Contract.csproj`.
- [ ] T004 [P] Add lint/format config entries for the new project to `.editorconfig` and the solution-level `.stylecop.json` so `lint-format` CI from spec 001 covers it.
- [ ] T005 [P] Register the `identity` OpenAPI source under `specs/phase-1B/004-identity-and-access/contracts/identity.openapi.yaml` in the `packages/shared_contracts` generator config so generated Dart + TS clients land in `packages/shared_contracts/identity/`.
- [ ] T006 [P] Create `scripts/identity/seed-roles.sh` shell wrapper calling `dotnet run --project services/backend_api/Tools/SeedRoles` (stub target created in T036).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure every story consumes: schema + migrations, DI wiring, token + session plumbing, OTP abstraction baseline, error envelope + localization reuse from spec 003, and the audit-log event bridge. Most of US6 lives here because every other story depends on the OTP abstraction.

**⚠️ CRITICAL**: No user story work may begin until this phase is complete.

- [ ] T007 Create `services/backend_api/Features/Identity/Persistence/IdentityDbContext.cs` extending the shared DbContext pattern from spec 003; wire `SaveChangesInterceptor` audit hook so every change to identity aggregates emits a domain event.
- [ ] T008 [P] Add EF Core configurations in `services/backend_api/Features/Identity/Persistence/Configurations/` for `customers`, `admins`, `roles`, `permissions`, `role_permissions`, `admin_role_assignments`, `admin_perm_version`, `sessions`, `refresh_tokens`, `otp_records`, `password_reset_tokens`, `stepup_assertions`, `identity_rate_limits` — one file per table matching data-model.md.
- [ ] T009 Generate initial migration `services/backend_api/Features/Identity/Persistence/Migrations/20260420_Identity_Init.cs` creating all 13 tables + enums + partial unique indexes (`lower(email)` and `phone_e164` gated by `status <> 'anonymized'`); verify with `dotnet ef migrations script`.
- [ ] T010 [P] Add `services/backend_api/Features/Identity/Events/` event record types listed in `contracts/events.md` as MediatR `INotification` records; no handlers yet.
- [ ] T011 [P] Implement `services/backend_api/Features/Identity/Events/AuditLogBridge.cs` — one generic MediatR notification handler that forwards identity domain events to the spec-003 audit-log module with redaction rules from `contracts/events.md`.
- [ ] T012 [P] Implement `services/backend_api/Features/Identity/Tokens/AccessTokenFactory.cs` issuing RS256 JWTs; load surface-scoped signing keys from Azure Key Vault binding (Dev: file-backed keys); carry `perm_ver` claim per research §8.
- [ ] T013 [P] Implement `services/backend_api/Features/Identity/Tokens/RefreshTokenStore.cs` with rotation, reuse-detection (`used_at` + chain revoke), and expiry enforcement.
- [ ] T014 Implement `services/backend_api/Features/Identity/Tokens/SessionService.cs` — surface-aware concurrency policy (customer unlimited, admin newest-wins) transactional with `sessions` + `refresh_tokens` writes.
- [ ] T015 [P] Implement `services/backend_api/Features/Identity/Authorization/PermissionRequirement.cs`, `PermissionHandler.cs`, `SurfaceRequirement.cs`, and `VerifiedProfessionalPolicy.cs` (policy stub — resolved by spec 020 later).
- [ ] T016 [P] Register `AddIdentity(...)` extension in `services/backend_api/Features/Identity/IdentityServiceCollectionExtensions.cs` wiring DbContext, MediatR handlers, JWT authentication schemes (two: `CustomerBearer`, `AdminBearer`), authorization policies, token/session services, rate-limit service, OTP provider from configuration.
- [ ] T017 [P] Add `services/backend_api/Features/Identity/RateLimits/IdentityRateLimiter.cs` Postgres-backed sliding-window counter per research §12; thresholds from `appsettings.Identity.json`.
- [ ] T018 [P] [US6] Implement `services/backend_api/Features/Identity/Otp/Abstractions/IOtpDeliveryProvider.cs`, `OtpSendRequest`, `OtpSendResult`, `OtpPurpose`, `OtpChannel` per research §9.
- [ ] T019 [P] [US6] Implement `services/backend_api/Features/Identity/Otp/Providers/TestOtpProvider.cs` with an in-memory deterministic buffer readable by integration tests.
- [ ] T020 [US6] Implement `services/backend_api/Features/Identity/Otp/Send/OtpSendService.cs` + `Verify/OtpVerifyService.cs` enforcing rate limits (FR-024), attempt cap, replay protection (FR-025); hash codes via HMAC-SHA256 with per-env pepper.
- [ ] T021 [P] Implement `services/backend_api/Features/Identity/Passwords/Argon2PasswordHasher.cs` with parameters from research §1 (m=19MiB, t=2, p=1); implements the project's `IPasswordHasher<T>` contract.
- [ ] T022 [P] Add localization resource files `services/backend_api/Features/Identity/Localization/Identity.en.resx` and `Identity.ar.resx` with editorial-grade AR copy stubs for every message key referenced in FR-026 (success, validation, rate-limit, lockout, reset copy, uniform-conflict, uniform-auth-failure).
- [ ] T023 Add seed data for permissions + system roles in `services/backend_api/Features/Identity/Rbac/Seed/SeedData.cs` covering the catalog in data-model.md §3 + §4 (super-admin, catalog-editor, inventory-editor, orders-ops, customers-ops, verification-reviewer, support-agent, finance-viewer + seed permissions).
- [ ] T024 [P] Integration test harness `services/backend_api/Tests/Identity.Integration/IdentityTestHost.cs` using `WebApplicationFactory` + Testcontainers Postgres + `TestOtpProvider` wired by default.

**Checkpoint**: Foundation ready — user story implementation can begin.

---

## Phase 3: User Story 1 — Customer Registration with Verified Contact (Priority: P1) 🎯 MVP

**Goal**: A fresh visitor can register with email or phone + password + market + locale, receive an OTP (via `TestOtpProvider`), verify it, and receive an authenticated session. Activates the `pending-verification → active` state transition and enforces uniform identifier-in-use response.

**Independent Test**: Run integration test `CustomerRegistrationFlowTests.RegisterVerifyLogin_HappyPath` end-to-end; confirm `POST /customers/register`, OTP fetch from test buffer, `POST /customers/verify-contact` returns a `SessionResponse`, and the customer can call `GET /customers/me`. Negative twin: second registration with same email returns uniform 409 (FR-006, AS-5).

### Tests for User Story 1 (TDD — write FIRST, ensure FAIL before implementation)

- [ ] T025 [P] [US1] Contract test `services/backend_api/Tests/Identity.Contract/RegistrationContractTests.cs` snapshotting `/customers/register`, `/customers/verify-contact`, `/customers/resend-otp` shapes against `contracts/identity.openapi.yaml`.
- [ ] T026 [P] [US1] Integration test `services/backend_api/Tests/Identity.Integration/Customers/RegistrationFlowTests.cs` covering AS-1 (email), AS-2 (phone), AS-3 (verify), AS-4 (OTP attempt cap), AS-5 (uniform conflict) and the uniqueness rule from Clarification Q1 (FR-006a).
- [ ] T027 [P] [US1] Unit test `services/backend_api/Tests/Identity.Unit/Customers/RegisterValidatorTests.cs` for the FluentValidation validator (at-least-one-of email/phone, password policy, market whitelist, locale whitelist, phone E.164 normalization).
- [ ] T028 [P] [US1] Audit-coverage test `services/backend_api/Tests/Identity.Integration/AuditCoverageTests.Registration.cs` asserting `identity.customer.registered` and `identity.customer.verified` events reach the audit sink.

### Implementation for User Story 1

- [ ] T029 [P] [US1] `services/backend_api/Features/Identity/Customers/Register/RegisterCustomerCommand.cs` + `RegisterCustomerValidator.cs` + `RegisterCustomerHandler.cs` + endpoint mapping in `RegisterCustomerEndpoint.cs`; emits `CustomerRegistered` notification; calls `OtpSendService`.
- [ ] T030 [P] [US1] `services/backend_api/Features/Identity/Customers/VerifyContact/VerifyContactCommand.cs` + validator + handler + endpoint; consumes OTP, transitions `pending-verification → active`, issues a first session via `SessionService`.
- [ ] T031 [P] [US1] `services/backend_api/Features/Identity/Customers/ResendOtp/ResendOtpCommand.cs` + handler + endpoint; rate-limited per T017/T020.
- [ ] T032 [US1] Uniform-conflict handler in `services/backend_api/Features/Identity/Customers/Register/UniformConflictFilter.cs` shaping 409 responses so they do not leak identifier existence (FR-006).
- [ ] T033 [US1] Wire endpoints in `services/backend_api/Features/Identity/IdentityEndpointRouter.cs` under `/customers/*`; attach `AllowAnonymous` and rate-limit filters.

**Checkpoint**: US1 fully functional. MVP demonstrable against `quickstart.md §3.1–3.2`.

---

## Phase 4: User Story 2 — Returning Customer Login and Session Management (Priority: P1)

**Goal**: Verified customers can login, refresh rotating tokens, enumerate and revoke sessions, and are subject to the progressive lockout policy (Clarification Q2).

**Independent Test**: `LoginSessionFlowTests.LoginRefreshRevoke_HappyPath` plus `LockoutEscalationTests.Tier1ThenTier2` using the seeded customer from T026. Verifies AS 2.1–2.5 and FR-008a/FR-011a.

### Tests for User Story 2

- [ ] T034 [P] [US2] Contract test `services/backend_api/Tests/Identity.Contract/LoginContractTests.cs` for `/customers/login`, `/customers/refresh`, `/customers/logout`, `/customers/me/sessions`.
- [ ] T035 [P] [US2] Integration test `services/backend_api/Tests/Identity.Integration/Customers/LoginSessionFlowTests.cs` covering AS 2.1–2.5.
- [ ] T036 [P] [US2] Integration test `services/backend_api/Tests/Identity.Integration/Customers/LockoutEscalationTests.cs` covering tier-1 time-unlock and tier-2 proof-of-control (FR-008a).
- [ ] T037 [P] [US2] Property-based test `services/backend_api/Tests/Identity.Unit/Tokens/RefreshRotationProperties.cs` (FsCheck): rotating N times yields exactly N active refresh tokens in the chain; reusing a `used_at` token revokes the chain.
- [ ] T038 [P] [US2] Audit-coverage test `AuditCoverageTests.LoginSession.cs` for `identity.customer.login.succeeded`, `.failed`, `.account.locked`, `.account.unlocked`, `.session.revoked`.

### Implementation for User Story 2

- [ ] T039 [P] [US2] `services/backend_api/Features/Identity/Customers/Login/LoginCommand.cs` + validator + handler + endpoint; enforces uniform unauthorized response (FR-008 no-existence-disclosure).
- [ ] T040 [P] [US2] `services/backend_api/Features/Identity/Customers/Refresh/RefreshCommand.cs` + handler + endpoint; uses `RefreshTokenStore` rotation + reuse detection.
- [ ] T041 [P] [US2] `services/backend_api/Features/Identity/Customers/Logout/LogoutCommand.cs` + handler + endpoint.
- [ ] T042 [P] [US2] `services/backend_api/Features/Identity/Customers/ListSessions/ListSessionsQuery.cs` + handler + endpoint returning `SessionSummary[]`.
- [ ] T043 [US2] `services/backend_api/Features/Identity/Customers/ListSessions/RevokeSessionCommand.cs` + handler + endpoint at `DELETE /customers/me/sessions/{sessionId}`.
- [ ] T044 [US2] `services/backend_api/Features/Identity/Lockout/LockoutService.cs` implementing progressive tiers (FR-008a) with time-unlock for tier-1 and proof-of-control gate for tier-2; emits `account.locked`/`account.unlocked`.
- [ ] T045 [US2] Wire all US2 endpoints + a custom 423 `LockedResponse` formatter consistent with the OpenAPI contract.

**Checkpoint**: US2 fully functional alongside US1.

---

## Phase 5: User Story 4 — Admin Authentication on a Separate Surface (Priority: P1)

**Goal**: Admins authenticate on a distinct surface with a separate issuer/audience, shorter idle timeout, single-active-session policy (newest wins), and step-up re-auth for sensitive actions. Customer tokens rejected on admin endpoints and vice versa.

**Independent Test**: `AdminSurfaceIsolationTests` proving cross-surface token rejection + `AdminSingleSessionTests` proving newest-wins + `AdminIdleTimeoutTests` proving FR-017 idle expiry.

### Tests for User Story 4

- [ ] T046 [P] [US4] Contract test `services/backend_api/Tests/Identity.Contract/AdminContractTests.cs` for `/admins/login`, `/admins/refresh`, `/admins/logout`, `/admins/stepup`, `/admins/{adminId}/enable`, `/admins/{adminId}/disable`, `/admins` (create).
- [ ] T047 [P] [US4] Integration test `services/backend_api/Tests/Identity.Integration/Admins/AdminSurfaceIsolationTests.cs` covering AS 4.1, 4.3 (cross-surface rejection logged as security event).
- [ ] T048 [P] [US4] Integration test `services/backend_api/Tests/Identity.Integration/Admins/AdminSingleSessionTests.cs` verifying FR-011b (newest-wins) with audit event `admin.session.revoked {reason: superseded-by-new-login}`.
- [ ] T049 [P] [US4] Integration test `services/backend_api/Tests/Identity.Integration/Admins/AdminIdleTimeoutTests.cs` covering AS 4.2 (FR-017).
- [ ] T050 [P] [US4] Integration test `services/backend_api/Tests/Identity.Integration/Admins/AdminDisableFlowTests.cs` covering AS 4.4 and forced revoke (FR-011).
- [ ] T051 [P] [US4] Audit-coverage test `AuditCoverageTests.AdminAuth.cs` for `admin.login.*`, `admin.session.revoked`, `admin.enabled`, `admin.disabled`, `admin.stepup.issued`.

### Implementation for User Story 4

- [ ] T052 [P] [US4] `services/backend_api/Features/Identity/Admins/Login/AdminLoginCommand.cs` + handler + endpoint — wires to `SessionService` with admin concurrency policy (single active).
- [ ] T053 [P] [US4] `services/backend_api/Features/Identity/Admins/Refresh/AdminRefreshCommand.cs` + handler + endpoint; enforces admin idle-timeout against `last_refreshed_at` per research §3.
- [ ] T054 [P] [US4] `services/backend_api/Features/Identity/Admins/Logout/AdminLogoutCommand.cs` + handler + endpoint.
- [ ] T055 [P] [US4] `services/backend_api/Features/Identity/Admins/StepUp/StepUpCommand.cs` + handler + endpoint; issues `stepup_assertions` row with 5-min TTL per research §14.
- [ ] T056 [P] [US4] `services/backend_api/Features/Identity/Admins/Create/CreateAdminCommand.cs` + validator + handler + endpoint guarded by `identity.admin.create` + step-up; blocks self-service.
- [ ] T057 [P] [US4] `services/backend_api/Features/Identity/Admins/Enable/EnableAdminCommand.cs` + handler + endpoint (audited).
- [ ] T058 [P] [US4] `services/backend_api/Features/Identity/Admins/Disable/DisableAdminCommand.cs` + handler + endpoint; revokes all sessions for the target admin transactionally.
- [ ] T059 [US4] `services/backend_api/Features/Identity/Authorization/SensitiveAdminActionAttribute.cs` + middleware that validates `X-StepUp-Token` header against `stepup_assertions`; return 401 + audit `admin.authorization.denied` on failure.
- [ ] T060 [US4] Configure two JWT bearer schemes in `IdentityServiceCollectionExtensions.cs` with distinct issuer, audience, signing-key kid; reject cross-surface tokens at middleware before handler dispatch.

**Checkpoint**: US4 fully functional. Admin surface isolated from customer surface.

---

## Phase 6: User Story 5 — Role-Based Access Control Framework (Priority: P1)

**Goal**: Roles, permissions, role-assignments, and endpoint-declared permission gates with exhaustive role × permission matrix coverage (SC-003). Role/permission changes propagate within the token-refresh staleness budget (SC-008).

**Independent Test**: Property-based `RolePermissionMatrix` test exercising every seeded role against every protected fixture endpoint; + `RoleChangePropagationTests` verifying staleness bound.

### Tests for User Story 5

- [ ] T061 [P] [US5] Contract test `services/backend_api/Tests/Identity.Contract/RbacContractTests.cs` for `/rbac/roles`, `/rbac/roles/{id}`, `/rbac/permissions`, `/rbac/admins/{id}/roles`, `/internal/authorize`.
- [ ] T062 [P] [US5] Property-based test `services/backend_api/Tests/Identity.Integration/Rbac/RolePermissionMatrixTests.cs` (FsCheck) covering every seeded role × every permission across synthetic fixture endpoints registered in `services/backend_api/Tests/Fixtures/ProtectedFixtureEndpoints.cs` (SC-003).
- [ ] T063 [P] [US5] Integration test `services/backend_api/Tests/Identity.Integration/Rbac/RoleChangePropagationTests.cs` asserting a role change takes effect no later than next token refresh (SC-008) via `perm_ver` bump.
- [ ] T064 [P] [US5] Integration test `services/backend_api/Tests/Identity.Integration/Rbac/SystemRoleProtectionTests.cs` — seeded system roles cannot be deleted.
- [ ] T065 [P] [US5] Audit-coverage test `AuditCoverageTests.Rbac.cs` for `role.created`, `role.updated`, `role.deleted`, `role.assigned`, `role.revoked`, `permission.seeded`, `admin.authorization.denied`.

### Implementation for User Story 5

- [ ] T066 [P] [US5] `services/backend_api/Features/Identity/Rbac/Roles/ListRolesQuery.cs` + handler + endpoint.
- [ ] T067 [P] [US5] `services/backend_api/Features/Identity/Rbac/Roles/CreateRoleCommand.cs` + validator + handler + endpoint (step-up required).
- [ ] T068 [P] [US5] `services/backend_api/Features/Identity/Rbac/Roles/UpdateRoleCommand.cs` + validator + handler + endpoint.
- [ ] T069 [P] [US5] `services/backend_api/Features/Identity/Rbac/Roles/DeleteRoleCommand.cs` + handler + endpoint (blocks `is_system=true`).
- [ ] T070 [P] [US5] `services/backend_api/Features/Identity/Rbac/Permissions/ListPermissionsQuery.cs` + handler + endpoint.
- [ ] T071 [P] [US5] `services/backend_api/Features/Identity/Rbac/Assignments/UpdateAdminRolesCommand.cs` + validator + handler + endpoint; bumps `admin_perm_version.perm_ver` transactionally.
- [ ] T072 [P] [US5] `services/backend_api/Features/Identity/Rbac/Authorize/AuthorizeCheckEndpoint.cs` implementing `POST /internal/authorize` used by future specs (verified-professional hook + arbitrary permission check).
- [ ] T073 [US5] `services/backend_api/Features/Identity/Rbac/Seed/SeedRolesTool.cs` console entrypoint callable by `scripts/identity/seed-roles.sh`.
- [ ] T074 [US5] Declarative permission decoration helper `services/backend_api/Features/Identity/Authorization/RequirePermissionAttribute.cs` so every protected endpoint declares its required permission (FR-020 default-deny).

**Checkpoint**: US5 fully functional. Role × endpoint matrix covered.

---

## Phase 7: User Story 3 — Password Reset via Contact Channel (Priority: P2)

**Goal**: Customers (and admins via their surface) can request a single-use, time-boxed password reset; successful reset revokes all sessions (FR-015) and requires new-password policy compliance (FR-014).

**Independent Test**: `PasswordResetFlowTests.RequestVerifyCompleteLogin` and negative `ExpiredOrUsedTokenRejected`.

### Tests for User Story 3

- [ ] T075 [P] [US3] Contract test `services/backend_api/Tests/Identity.Contract/PasswordResetContractTests.cs` for `/customers/password-reset/request` and `/customers/password-reset/complete`.
- [ ] T076 [P] [US3] Integration test `services/backend_api/Tests/Identity.Integration/Customers/PasswordResetFlowTests.cs` covering AS 3.1–3.3 and the "all sessions revoked on reset" rule (FR-015).
- [ ] T077 [P] [US3] Unit test `services/backend_api/Tests/Identity.Unit/Customers/PasswordPolicyValidatorTests.cs` (FR-014).
- [ ] T078 [P] [US3] Audit-coverage test `AuditCoverageTests.PasswordReset.cs` for `password.reset.requested` (uniform) and `password.reset.completed`.

### Implementation for User Story 3

- [ ] T079 [P] [US3] `services/backend_api/Features/Identity/Customers/RequestPasswordReset/RequestPasswordResetCommand.cs` + handler + endpoint; always returns 202 regardless of identifier existence (FR-013 uniform).
- [ ] T080 [P] [US3] `services/backend_api/Features/Identity/Customers/CompletePasswordReset/CompletePasswordResetCommand.cs` + validator + handler + endpoint; transactionally updates password and revokes all sessions for the subject on the matching surface.
- [ ] T081 [P] [US3] `services/backend_api/Features/Identity/Passwords/PasswordPolicy.cs` enforcing FR-014 (length, character-class mix, last-N reuse).

**Checkpoint**: US3 fully functional.

---

## Phase 8: User Story 6 — OTP Provider Abstraction (Priority: P2) — swap verification

**Goal**: Most of the abstraction ships in Phase 2 (T018–T020). What remains for US6 is the swap-test dummy provider + the integration test proving SC-007 (zero-caller-change provider swap).

**Independent Test**: `OtpProviderSwapTests.SwapToAltDummy_NoCallerChange` runs the registration flow twice with two providers configured by key only.

### Tests for User Story 6

- [ ] T082 [P] [US6] Integration test `services/backend_api/Tests/Identity.Integration/Otp/OtpProviderSwapTests.cs` running Story 1 flow with `Identity:OtpProvider=test` and `=test-alt` — expect identical outcomes (SC-007).
- [ ] T083 [P] [US6] Unit test `services/backend_api/Tests/Identity.Unit/Otp/OtpRateLimitTests.cs` covering AS 6.2 (per-identifier and per-IP windows from research §2).
- [ ] T084 [P] [US6] Unit test `services/backend_api/Tests/Identity.Unit/Otp/OtpReplayTests.cs` covering AS 6.3 (consumed OTPs cannot re-verify).

### Implementation for User Story 6

- [ ] T085 [P] [US6] `services/backend_api/Features/Identity/Otp/Providers/TestAltOtpProvider.cs` — second deterministic dummy provider registered under key `test-alt`.
- [ ] T086 [US6] `services/backend_api/Features/Identity/Otp/Providers/OtpProviderRegistry.cs` — resolves the configured provider by key at request time (rather than singleton capture) so the swap is config-only.

**Checkpoint**: US6 fully functional. All six user stories independently demonstrable.

---

## Phase 9: Deletion lifecycle scaffolding (spec-driven, not story-driven)

**Purpose**: FR-030/031 — scaffold deletion-request and anonymization so 011/012 never see a migration when Phase 1.5 ships the customer-facing UX.

- [ ] T087 [P] `services/backend_api/Features/Identity/Customers/RequestDeletion/RequestDeletionCommand.cs` + handler + endpoint (admin-triggered at launch per FR-031); revokes sessions; emits `customer.deletion.requested`.
- [ ] T088 [P] `services/backend_api/Features/Identity/Customers/Anonymize/AnonymizeCustomerCommand.cs` + handler + endpoint; clears PII, deletes OTP/reset/session rows; emits `customer.anonymized`; leaves stable id intact.
- [ ] T089 `services/backend_api/Features/Identity/Customers/Anonymize/AnonymizationScheduler.cs` hosted service that, per configured interval, promotes `deletion-requested` rows whose `scheduled_anonymization_at <= now()` to `anonymized`.
- [ ] T090 Integration test `services/backend_api/Tests/Identity.Integration/Customers/DeletionLifecycleTests.cs` covering `active → deletion-requested → anonymized`; asserts partial-unique indexes free the identifier after anonymization (data-model §1 + research §11).
- [ ] T091 Audit-coverage test assertion in `AuditCoverageTests.DeletionLifecycle.cs` for `customer.deletion.requested` and `customer.anonymized`.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T092 [P] k6 smoke script `tests/perf/identity/login.k6.js` asserting SC-002 login p95 ≤ 1 s at baseline load; wire into the perf CI job.
- [ ] T093 [P] Argon2id benchmark tool `services/backend_api/Tools/Bench/Argon2idBench.cs` asserting SC-006 verify cost ≥ 100 ms on baseline vCPU.
- [ ] T094 [P] OpenAPI + shared-contracts regeneration: run `scripts/shared-contracts/generate.sh identity`; commit `packages/shared_contracts/identity/{dart,ts}/` output; ensure `contract-diff` CI (spec 002) passes.
- [ ] T095 [P] Structured-logging pass: ensure every handler emits one structured log line per accepted action with `correlation_id`, `surface`, `actor_id`, `action_key`, `market_code`.
- [ ] T096 [P] Editorial AR + EN review of `Identity.ar.resx` + `Identity.en.resx`; capture sign-off in `specs/phase-1B/004-identity-and-access/checklists/editorial-signoff.md` (SC-005).
- [ ] T097 [P] Security hardening: run OWASP ASVS L1 auth checklist against the identity module; capture results in `specs/phase-1B/004-identity-and-access/checklists/asvs-l1.md`; fix any gaps inline.
- [ ] T098 [P] Fuzz test `services/backend_api/Tests/Identity.Integration/Otp/OtpFuzzTests.cs` over OTP verify inputs (SQLi, unicode, oversized codes) to confirm replay + attempt-cap invariants.
- [ ] T099 Run `specs/phase-1B/004-identity-and-access/quickstart.md §6` DoD checklist; attach results + constitution/ADR fingerprint (`scripts/compute-fingerprint.sh`) to the PR body per spec 001.
- [ ] T100 Final constitution-compliance self-review against Principles 3, 4, 6, 8, 20, 24, 25 and ADR-010; capture review note in PR body.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. BLOCKS every user story phase.
- **User Stories**: All depend on Phase 2. Within Phase 1B they can run in parallel once Foundational is complete.
  - US1 → first MVP target.
  - US2 and US4 depend only on Foundational (session/token plumbing lives there).
  - US5 depends only on Foundational.
  - US3 depends on Foundational + the Customer aggregate from US1 (a seeded active customer for tests).
  - US6 swap test depends on Foundational (US6 Phase-2 tasks) + US1 registration flow for the end-to-end swap assertion.
- **Phase 9 deletion lifecycle**: Depends on Foundational + US1 (customer aggregate) + US2 (session revoke machinery).
- **Polish (Phase 10)**: Depends on all prior phases.

### User Story Dependencies

- **US1 (P1)**: Foundational only.
- **US2 (P1)**: Foundational only. Re-uses session + token services built in Foundational.
- **US4 (P1)**: Foundational only. Uses the same `SessionService` under admin policy.
- **US5 (P1)**: Foundational only. Plugs into authorization middleware already wired in Foundational.
- **US3 (P2)**: Needs US1 active customer fixture for test data; functional dependency only, not code.
- **US6 (P2)**: Most work is in Foundational. Swap verification reuses US1 flow.

### Parallel Opportunities

- All `[P]` Setup tasks (T002–T006) can run in parallel.
- Within Foundational, `[P]` tasks T008, T010–T013, T015–T019, T021, T022, T024 parallelize.
- Once Foundational clears, US1/US2/US4/US5 can run in parallel streams by separate developers/agents; US3 and US6 follow.
- Within each story, all `[P]` test tasks run in parallel before implementation begins, and all `[P]` implementation tasks run in parallel when they touch different files.

---

## Parallel Example: User Story 1 (MVP)

```bash
# Write all US1 tests first (TDD):
Task: "Contract test RegistrationContractTests.cs"
Task: "Integration test RegistrationFlowTests.cs"
Task: "Unit test RegisterValidatorTests.cs"
Task: "Audit-coverage test AuditCoverageTests.Registration.cs"

# Then implement US1 handlers in parallel (different files):
Task: "RegisterCustomerCommand + handler + endpoint"
Task: "VerifyContactCommand + handler + endpoint"
Task: "ResendOtpCommand + handler + endpoint"
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1 Setup.
2. Phase 2 Foundational (critical — blocks all stories).
3. Phase 3 US1 Registration + Verification.
4. **STOP and VALIDATE**: Run `quickstart.md §3.1–3.2`; confirm the register → OTP → verify → session flow and the uniform-conflict response.
5. Demo / deploy-preview.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → MVP deploy.
3. US2 (login + session) → deploy; customer flow now resilient across restarts.
4. US4 (admin surface) → deploy; unblocks Phase 1C spec 015 admin-foundation.
5. US5 (RBAC) → deploy; unblocks every later admin spec.
6. US3 (password reset) → deploy; identity is feature-complete for launch.
7. US6 swap verification + Phase 9 deletion scaffolding → deploy.
8. Phase 10 polish.

### Parallel Team Strategy

- Lane A developer/agent 1: US1 + US3 (customer flows).
- Lane A developer/agent 2: US2 (session + lockout).
- Lane A developer/agent 3: US4 (admin surface).
- Lane A developer/agent 4: US5 (RBAC) + US6 swap + Phase 9 deletion scaffolding.
- All converge on Phase 10 polish together.

---

## Notes

- `[P]` tasks = different files, no in-phase dependencies.
- `[Story]` label maps every story-phase task to US1–US6 from spec.md for DoD traceability.
- Every user story is independently completable and testable; skipping US3 or US6 still yields a working identity module for MVP validation.
- TDD: every story's `Tests` tasks precede its implementation tasks; integration tests use Testcontainers Postgres, not mocks (per saved project feedback).
- Commit after each task or logical group; include constitution/ADR fingerprint per spec 001 in every PR body.
- Checkpoint gates (end of each story phase) are explicit so an agent/human can stop, validate, and hand off without reading ahead.

---

## Amendment A1 — Environments, Docker, Seeding

**Source**: [`docs/missing-env-docker-plan.md`](../../../docs/missing-env-docker-plan.md)

**Hard dependency**: PR A1 (scaffolding) must merge before this spec's implementation PR opens. A1 provides `appsettings.Staging.json` / `appsettings.Production.json`, the `Seeding/` framework, `SeedGuard`, and compose-based local dev.

### New tasks

- [ ] T101 [US1] Implement `services/backend_api/Features/Seeding/Seeders/_004_IdentitySeeder.cs` (`ISeeder`, `Name="identity-v1"`, `Version=1`, `DependsOn=[]`). Seeds 1 admin, 2 staff (catalog.manage, inventory.adjust), 5 customers, 2 professionals (1 verified, 1 pending) + roles/permissions. Emails `@example.com`; phones in reserved ranges `+96650000000` / `+20100000000`. Register in DI.
- [ ] T102 [US1] Integration test `Tests/Identity.Integration/Seeding/IdentitySeederTests.cs`: fresh-apply populates expected rows; second apply is a no-op (idempotency); `SeedGuard` blocks under `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] T103 Verify `seed-pii-guard` CI job green against this seeder.
