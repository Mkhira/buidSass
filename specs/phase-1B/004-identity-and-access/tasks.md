---
description: "Dependency-ordered task list for spec 004 — identity-and-access"
---

# Tasks: Identity and Access

**Input**: Design documents from `/specs/phase-1B/004-identity-and-access/`
**Prerequisites**: spec.md (42 FRs, 13 SCs, 6 user stories), plan.md, research.md, data-model.md (17 tables, 9 state machines), contracts/identity-and-access-contract.md (35 endpoints), quickstart.md

**Tests**: The spec's Acceptance Scenarios and Success Criteria are enforceable — every user story phase below includes contract + integration tests per spec 003's convention. Tests are NOT optional here; they are how the DoD's "42 FRs × contract test" rule is satisfied.

**Organization**: Tasks grouped by user story. Each story is independently implementable *after Foundational phase (Phase 2)*. MVP = US1 + US2 + US3 + US4 (all P1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: Which user story this task belongs to (US1..US6). Setup/Foundational/Polish phases carry no story label.
- All paths are absolute-from-repo-root; the module lives under `services/backend_api/Modules/Identity/` with tests under `services/backend_api/tests/Identity.Tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding that all downstream work depends on; runs once.

- [X] T001 Create module directory tree under `services/backend_api/Modules/Identity/{Primitives,Customer,Admin,Authorization,Entities,Persistence/{Configurations,Migrations},Seeding,Messages}` and the test root `services/backend_api/tests/Identity.Tests/{Unit,Integration,Contract}`
- [X] T002 Add `IdentityModule.cs` at `services/backend_api/Modules/Identity/IdentityModule.cs` exposing `AddIdentityModule(IServiceCollection, IConfiguration)` and register it from `services/backend_api/Program.cs` (behind `app.UseIdentityModuleEndpoints()`)
- [X] T003 [P] Add NuGet references to `services/backend_api/backend_api.csproj`: `Konscious.Security.Cryptography.Argon2 1.3.*`, `Microsoft.AspNetCore.Authentication.JwtBearer 9.*`, `Otp.NET 1.4.*`, `libphonenumber-csharp 8.*`, `MediatR 12.*`, `FluentValidation 11.*`, `FluentValidation.DependencyInjectionExtensions 11.*`
- [X] T004 [P] Add test-project references to `services/backend_api/tests/Identity.Tests/Identity.Tests.csproj`: `xunit`, `FluentAssertions`, `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing`, `FsCheck.Xunit`
- [X] T005 [P] Wire the A1 lint/format toolchain for the module path — add `Modules/Identity/**` to the format includes in `services/backend_api/.editorconfig` and verify `dotnet format services/backend_api/` is clean on the empty tree

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Primitives, persistence, RBAC, and state-machine scaffolding that every user story phase consumes.

**⚠️ CRITICAL**: No user-story work begins until all of Phase 2 is complete.

### Primitives

- [X] T006 [P] Implement `SurfaceKind` enum and `MarketCode` record in `services/backend_api/Modules/Identity/Primitives/SurfaceKind.cs` and `MarketCode.cs`
- [X] T007 [P] Implement `Argon2idHasher` (customer + admin tiered params, encoded-string parse/verify, lazy rehash on stale cost) in `services/backend_api/Modules/Identity/Primitives/Argon2idHasher.cs`
- [X] T008 [P] Implement `BreachListChecker` (embedded top-100k resource, bloom filter + exact match) in `services/backend_api/Modules/Identity/Primitives/BreachListChecker.cs`; embed `Primitives/Resources/hibp-top-100k.txt.gz`
- [X] T009 [P] Implement `PhoneNormalizer` wrapping libphonenumber with `(E164, inferredMarketCode)` output in `services/backend_api/Modules/Identity/Primitives/PhoneNormalizer.cs`
- [X] T010 [P] Implement `JwtIssuer` (ES256, separate signing keys per surface, issuer/audience per surface, JWKS publishable) in `services/backend_api/Modules/Identity/Primitives/JwtIssuer.cs`
- [X] T011 [P] Implement `RefreshTokenRevocationStore` + `ITokenRevocationCache` (Postgres-backed ledger + in-proc bloom filter, 15 s refresh loop) in `services/backend_api/Modules/Identity/Primitives/RefreshTokenRevocationStore.cs` and `.../ITokenRevocationCache.cs`
- [X] T012 [P] Implement `RateLimitPolicies.RegisterAll(builder)` registering the 5 policies from research.md R7 in `services/backend_api/Modules/Identity/Primitives/RateLimitPolicies.cs`
- [X] T013 [P] Define `IOtpChallengeDispatcher` seam + `ConsoleOtpDispatcher` (Dev, compile-time guarded by `DEV_OTP_SINK`) + `NotConfiguredOtpDispatcher` (Staging/Prod default until spec 025) in `services/backend_api/Modules/Identity/Primitives/IOtpChallengeDispatcher.cs` and sibling files
- [X] T014 [P] Define `IAuthorizationAuditEmitter` consuming spec 003's `IAuditEventPublisher` in `services/backend_api/Modules/Identity/Authorization/IAuthorizationAuditEmitter.cs`

### State machines

- [X] T015 [P] Implement `AccountStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/AccountStateMachine.cs`
- [X] T016 [P] Implement `SessionStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/SessionStateMachine.cs`
- [X] T017 [P] Implement `RefreshTokenStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/RefreshTokenStateMachine.cs`
- [X] T018 [P] Implement `OtpChallengeStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/OtpChallengeStateMachine.cs`
- [X] T019 [P] Implement `EmailVerificationStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/EmailVerificationStateMachine.cs`
- [X] T020 [P] Implement `PasswordResetStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/PasswordResetStateMachine.cs`
- [X] T021 [P] Implement `AdminInvitationStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/AdminInvitationStateMachine.cs`
- [X] T022 [P] Implement `AdminMfaFactorStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/AdminMfaFactorStateMachine.cs`
- [X] T023 [P] Implement `IdentityLockoutStateMachine` in `services/backend_api/Modules/Identity/Primitives/StateMachines/IdentityLockoutStateMachine.cs`
- [X] T024 [P] Unit tests for every transition of each state machine (9 files) in `services/backend_api/tests/Identity.Tests/Unit/StateMachines/*Tests.cs`

### Persistence

- [X] T025 Create 17 EF entity classes in `services/backend_api/Modules/Identity/Entities/*.cs` matching data-model.md (Account, Session, RefreshToken, RevokedRefreshToken, OtpChallenge, EmailVerificationChallenge, PasswordResetToken, AdminInvitation, AdminMfaFactor, AdminMfaReplayGuard, LockoutState, Role, Permission, RolePermission, AccountRole, AuthorizationAudit, RateLimitEvent)
- [X] T026 Create 17 `IEntityTypeConfiguration<T>` classes in `services/backend_api/Modules/Identity/Persistence/Configurations/*Configuration.cs` with indexes, unique constraints, and soft-delete query filters as specified
- [X] T027 Create `IdentityDbContext` at `services/backend_api/Modules/Identity/Persistence/IdentityDbContext.cs` hooking spec 003's shared `SaveChangesInterceptor` for audit + `updated_at`
- [X] T028 Generate initial EF migration `Identity_Initial` at `services/backend_api/Modules/Identity/Persistence/Migrations/` and verify `dotnet ef database update` applies cleanly against A1 local Postgres
- [X] T029 Add `IdentityReferenceDataSeeder : ISeeder` in `services/backend_api/Modules/Identity/Seeding/IdentityReferenceDataSeeder.cs` — seeds roles (`platform.super_admin`, `platform.finance`, `platform.support`, `customer.standard`, `customer.company_owner`), permissions, role→permission matrix. Idempotent across Dev/Staging/Prod.
- [X] T030 Add `IdentityDevDataSeeder : ISeeder` in `services/backend_api/Modules/Identity/Seeding/IdentityDevDataSeeder.cs` — Dev-only (`SeedGuard`), creates default super-admin + sample customers per market, idempotent
- [X] T031 Add `SeedAdminCliCommand` entry point in `services/backend_api/Modules/Identity/Seeding/SeedAdminCliCommand.cs` + wire `Program.cs` to route `dotnet run -- seed-admin` here *without* starting the HTTP host; emits `identity.admin.bootstrap` audit event

### Authorization

- [X] T032 Implement `PolicyEvaluator` (reads roles+permissions from JWT claims, evaluates `[RequirePermission]` attribute) in `services/backend_api/Modules/Identity/Authorization/PolicyEvaluator.cs`
- [X] T033 Implement `[RequirePermission]` + `[RequireStepUp]` action filters in `services/backend_api/Modules/Identity/Authorization/Filters/*.cs` with deny-reason emission (`role_missing`, `scope_mismatch`, `market_mismatch`, `mfa_not_satisfied`)
- [X] T034 Implement `AuthorizationAuditEmitter` (100 % deny rows + 1 % allow sample) in `services/backend_api/Modules/Identity/Authorization/AuthorizationAuditEmitter.cs`
- [X] T035 Register JWT bearer authentication with two schemes (`CustomerJwt`, `AdminJwt`) — separate ES256 keys, issuer, audience — in `IdentityModule.AddIdentityModule`; assert cross-audience rejection in an integration test stub

### AR/EN messaging scaffold

- [X] T036 [P] Create `Modules/Identity/Messages/identity.ar.icu` and `identity.en.icu` message bundles (populated per-slice as slices land); add `IdentityMessagesCompletenessTests` at `tests/Identity.Tests/Unit/IdentityMessagesCompletenessTests.cs` asserting AR/EN key parity

### Revocation cache worker

- [X] T037 Implement `RefreshRevocationCacheWorker : BackgroundService` refreshing the bloom filter every 15 s + on write; register in `IdentityModule` in `services/backend_api/Modules/Identity/Primitives/RefreshRevocationCacheWorker.cs`

### Test harness

- [X] T038 Create `IdentityTestFactory : WebApplicationFactory<Program>` with Testcontainers Postgres fixture + env overrides (customer+admin JWT keys, disabled real OTP dispatch) in `services/backend_api/tests/Identity.Tests/Infrastructure/IdentityTestFactory.cs`
- [X] T039 Add shared test builders (`AccountBuilder`, `RoleBuilder`) in `services/backend_api/tests/Identity.Tests/Infrastructure/Builders/*.cs`

**Checkpoint**: Foundation ready — user story phases can proceed in parallel.

---

## Phase 3: User Story 1 — Customer Registration & Verification (Priority: P1) 🎯 MVP

**Goal**: A customer can register, verify email, verify phone via OTP, and arrive at an `active` account with a customer-surface session. Enumeration-resistant per SC-010.

**Independent test**: Seed an empty DB. Hit `POST /v1/customer/identity/register` → expect `202`. Confirm email via token → account `active`. Request phone OTP → verify → phone marked verified. Repeat registration with the same email → still `202` (no enumeration signal). Verify audit trail has `account.created`, `email.verified`, `phone.verified`.

### Contract + integration tests

- [X] T040 [P] [US1] Contract test `Register_EmitsAcceptedAndPendingVerification` in `tests/Identity.Tests/Contract/Customer/RegisterContractTests.cs`
- [X] T041 [P] [US1] Contract test `Register_DuplicateEmail_StillReturnsAccepted` (SC-010 / FR-030) in same file
- [X] T042 [P] [US1] Contract test `Register_PhoneMarketMismatch_Returns400` (Edge Case #1) in same file
- [X] T043 [P] [US1] Contract test `ConfirmEmail_ValidToken_ActivatesAccount` in `tests/Identity.Tests/Contract/Customer/ConfirmEmailContractTests.cs`
- [X] T044 [P] [US1] Contract test `ConfirmEmail_ExpiredToken_Returns410` in same file
- [X] T045 [P] [US1] Contract test `RequestOtp_RegistrationPhone_DispatchesChallenge` in `tests/Identity.Tests/Contract/Customer/RequestOtpContractTests.cs`
- [X] T046 [P] [US1] Contract test `VerifyOtp_ValidCode_MarksPhoneVerified` in `tests/Identity.Tests/Contract/Customer/VerifyOtpContractTests.cs`
- [X] T047 [P] [US1] Integration test `EnumerationTiming_RegistrationBranchesAreConstantTime` (p95 delta ≤ 10 ms) in `tests/Identity.Tests/Integration/EnumerationResistanceTests.cs`

### Slices (implementation)

- [X] T048 [US1] Implement `Customer/Register/{Request,Validator,Handler,Endpoint}.cs` — password policy per FR-008 tier, libphonenumber market check, always-202 response, dummy Argon2id on taken-email path
- [X] T049 [US1] Implement `Customer/ConfirmEmail/{Request,Validator,Handler,Endpoint}.cs` — EmailVerificationStateMachine transition
- [X] T050 [US1] Implement `Customer/RequestOtp/{Request,Validator,Handler,Endpoint}.cs` — dispatches via `IOtpChallengeDispatcher`, tiered rate-limit policy
- [X] T051 [US1] Implement `Customer/VerifyOtp/{Request,Validator,Handler,Endpoint}.cs` — OtpChallengeStateMachine transition, attempts counter, purpose-driven side effects
- [X] T052 [US1] Populate AR/EN message bundles for US1 reason codes (`identity.register.*`, `identity.email_verification.*`, `identity.otp.*`) in `Messages/identity.{ar,en}.icu`
- [X] T053 [US1] Audit emission hook-up: assert `account.created`, `email.verified`, `phone.verified` events fire with correlation-id in `tests/Identity.Tests/Integration/Customer/RegistrationAuditTests.cs`

**Checkpoint**: US1 independently testable — a net-new customer can register, verify email, verify phone.

---

## Phase 4: User Story 2 — Admin Sign-In with MFA & Authorization (Priority: P1)

**Goal**: A seeded admin can sign in, complete TOTP MFA (or OTP step-up for non-super/non-finance admin), and receive an admin-scope JWT that passes `[RequirePermission]` on a protected stub endpoint. Admin tokens MUST NOT unlock customer endpoints (SC-011).

**Independent test**: Seed a super-admin via `SeedAdminCliCommand` on a staging-mode DB. Sign-in → expect `mfaChallenge`. Submit TOTP → expect `AuthSession`. Hit a permission-gated test endpoint → expect 200. Reuse the admin JWT against a customer endpoint → expect 401.

### Contract + integration tests

- [X] T054 [P] [US2] Contract test `AdminSignIn_ValidCredentials_RequiresMfa` in `tests/Identity.Tests/Contract/Admin/SignInContractTests.cs`
- [X] T055 [P] [US2] Contract test `AdminSignIn_InvalidCredentials_Returns400UniformCopy` in same file
- [X] T056 [P] [US2] Contract test `AdminMfaChallenge_ValidTotp_IssuesAuthSession` in `tests/Identity.Tests/Contract/Admin/MfaChallengeContractTests.cs`
- [X] T057 [P] [US2] Contract test `AdminMfaChallenge_ReusedTotp_Returns409Replay` in same file
- [X] T058 [P] [US2] Contract test `AdminTotpEnroll_InvitationFlow_CompletesSetup` in `tests/Identity.Tests/Contract/Admin/TotpEnrollContractTests.cs`
- [X] T059 [P] [US2] Integration test `AdminToken_RejectedOnCustomerSurface` (SC-011) in `tests/Identity.Tests/Integration/CrossSurfaceIsolationTests.cs`
- [X] T060 [P] [US2] Integration test `AdminWithoutPermission_Returns403AndAuditRow` (SC-007) in `tests/Identity.Tests/Integration/AuthorizationAuditTests.cs`

### Slices

- [X] T061 [US2] Implement `Admin/AcceptInvitation/{Request,Validator,Handler,Endpoint}.cs` — AdminInvitation state transition, issues partial-auth token
- [X] T062 [US2] Implement `Admin/EnrollTotp/{Request,Validator,Handler,Endpoint}.cs` — generates shared secret, encrypts via `IDataProtectionProvider`, emits otpauth URI + 10 recovery codes (Argon2id-hashed)
- [X] T063 [US2] Implement `Admin/ConfirmTotp/{Request,Validator,Handler,Endpoint}.cs` — AdminMfaFactor `pending_confirmation → active`
- [X] T064 [US2] Implement `Admin/SignIn/{Request,Validator,Handler,Endpoint}.cs` — admin Argon2id tier, lockout tracking (3 fails / 15 min → 30 min lock), mfa-challenge envelope when factor active
- [X] T065 [US2] Implement `Admin/CompleteMfaChallenge/{Request,Validator,Handler,Endpoint}.cs` — TOTP verify (±1 window) + replay guard insert/reject
- [X] T066 [US2] Implement `Admin/RotateTotp/{Request,Validator,Handler,Endpoint}.cs` and `Admin/ResetAdminMfa/{…}.cs` (super-admin-only)
- [X] T067 [US2] Populate AR/EN message bundles for admin sign-in + MFA reason codes

**Checkpoint**: US2 independently testable.

---

## Phase 5: User Story 3 — Authorization Audit & Customer Sign-In (Priority: P1)

**Goal**: A verified customer can sign in and receive a customer-scope JWT; every authorization decision (customer or admin) is observable (SC-007). Rate limits, lockout, and enumeration resistance all hold.

**Independent test**: Seeded verified customer. Sign-in → `AuthSession`. Exceed sign-in rate limit → `429`. Repeat-wrong-password to hit lockout threshold → `423` with `lockedUntil`. Query `authorization_audit` — every deny was recorded with `reason_code`.

### Contract + integration tests

- [X] T068 [P] [US3] Contract test `CustomerSignIn_ValidCredentials_IssuesSession` in `tests/Identity.Tests/Contract/Customer/SignInContractTests.cs`
- [X] T069 [P] [US3] Contract test `CustomerSignIn_InvalidCredentials_UniformError` in same file
- [X] T070 [P] [US3] Contract test `CustomerSignIn_LockedAccount_Returns423WithLockedUntil` in same file
- [X] T071 [P] [US3] Integration test `RateLimit_CustomerSignIn_BlocksAfterThreshold` in `tests/Identity.Tests/Integration/RateLimitTests.cs`
- [X] T072 [P] [US3] Integration test `Lockout_AfterThresholdFailures_ReleasesAfterWindow` (FR-018) in same file
- [X] T073 [P] [US3] Integration test `AuthorizationAudit_EveryDenyHasRow` (SC-007) in `tests/Identity.Tests/Integration/AuthorizationAuditTests.cs`

### Slices

- [X] T074 [US3] Implement `Customer/SignIn/{Request,Validator,Handler,Endpoint}.cs` — customer Argon2id tier, lockout tracking (5 fails / 15 min → 15 min lock), JWT issuance, refresh-token creation
- [X] T075 [US3] Implement `Customer/RefreshSession/{Request,Validator,Handler,Endpoint}.cs` — rotation with reuse-detection (reuse → revoke parent session + emit security audit)
- [X] T076 [US3] Implement `Customer/SignOut/{Request,Validator,Handler,Endpoint}.cs` — Session + RefreshToken revoke
- [X] T077 [US3] Wire `AuthorizationAuditEmitter` at the deny path of `[RequirePermission]` filter; add the 1 % allow-sampling logic in `PolicyEvaluator.AllowSamplingStrategy`
- [X] T078 [US3] Populate AR/EN message bundles for sign-in / lockout / rate-limit reason codes

**Checkpoint**: US3 independently testable.

---

## Phase 6: User Story 4 — OTP Sign-In & Password Reset (Priority: P1)

**Goal**: Customer can sign in via phone OTP (passwordless) and can reset a forgotten password without revealing account existence.

**Independent test**: Seeded verified customer. `otp/request` + `otp/verify` with `purpose=signin_customer` → `AuthSession`. Unknown email: `password/reset-request` → `202` (no signal). Known email: `reset-request` → `202`, follow token → `reset-complete` → all sessions revoked (FR-015) + old password no longer works.

### Contract + integration tests

- [X] T079 [P] [US4] Contract test `OtpSignIn_VerifiedCode_IssuesSession` in `tests/Identity.Tests/Contract/Customer/OtpSignInContractTests.cs`
- [X] T080 [P] [US4] Contract test `PasswordResetRequest_UnknownEmail_StillReturns202` (FR-030) in `tests/Identity.Tests/Contract/Customer/PasswordResetContractTests.cs`
- [X] T081 [P] [US4] Contract test `PasswordResetComplete_ValidToken_RevokesAllSessions` (FR-015) in same file
- [X] T082 [P] [US4] Contract test `PasswordChange_AuthenticatedCustomer_RevokesOtherSessions` in `tests/Identity.Tests/Contract/Customer/PasswordChangeContractTests.cs`
- [X] T083 [P] [US4] Integration test `AdminStepUpOtp_RequiredForSensitiveOps` (FR-024 step-up tier) in `tests/Identity.Tests/Integration/AdminStepUpTests.cs`

### Slices

- [X] T084 [US4] Extend `Customer/VerifyOtp/Handler.cs` (from US1) to issue `AuthSession` when `purpose=signin_customer`
- [X] T085 [US4] Implement `Customer/RequestPasswordReset/{Request,Validator,Handler,Endpoint}.cs` — uniform 202, always-dispatch-or-silent-no-op
- [X] T086 [US4] Implement `Customer/CompletePasswordReset/{Request,Validator,Handler,Endpoint}.cs` — token consume, breach-list re-check, revoke all sessions
- [X] T087 [US4] Implement `Customer/ChangePassword/{Request,Validator,Handler,Endpoint}.cs` — revoke other sessions, keep current
- [X] T088 [US4] Implement `Admin/StepUpOtp/{Request,Validator,Handler,Endpoint}.cs` + `Admin/CompleteStepUpOtp/{…}.cs` — attaches `step_up_valid_until` claim to re-issued access token
- [X] T089 [US4] Populate AR/EN message bundles for OTP sign-in + password reset/change reason codes

**Checkpoint**: US4 independently testable. MVP (US1–US4) now complete.

---

## Phase 7: User Story 5 — Session Control (Priority: P2)

**Goal**: A customer can list their own sessions and revoke a non-current session; a super-admin can list + revoke any admin session (SC-004 revocation ≤ 60 s).

**Independent test**: Sign in from two simulated clients. `GET /v1/customer/identity/sessions` returns both with `isCurrent` marker. `DELETE` the non-current one → that session's refresh-token no longer works within 60 s.

### Contract + integration tests

- [X] T090 [P] [US5] Contract test `ListSessions_AuthenticatedCustomer_ReturnsOwn` in `tests/Identity.Tests/Contract/Customer/SessionsContractTests.cs`
- [X] T091 [P] [US5] Contract test `RevokeSession_NonCurrent_ReturnsNoContent` in same file
- [X] T092 [P] [US5] Contract test `RevokeSession_Current_Returns403` in same file
- [X] T093 [P] [US5] Integration test `RevocationPropagates_UnderSixtySeconds` (SC-004) in `tests/Identity.Tests/Integration/RevocationPropagationTests.cs`
- [X] T094 [P] [US5] Contract test `AdminRevokeAdminSession_SuperAdminWithStepUp_Succeeds` in `tests/Identity.Tests/Contract/Admin/RevokeAdminSessionContractTests.cs`

### Slices

- [X] T095 [US5] Implement `Customer/ListSessions/{Request,Validator,Handler,Endpoint}.cs`
- [X] T096 [US5] Implement `Customer/RevokeSession/{Request,Validator,Handler,Endpoint}.cs`
- [X] T097 [US5] Implement `Admin/ListAdminSessions/{…}.cs` + `Admin/RevokeAdminSession/{…}.cs` (both gated by `[RequirePermission("identity.admin.session.manage")]` + `[RequireStepUp]`)
- [X] T098 [US5] Verify `RefreshRevocationCacheWorker` propagation timing under integration harness; tighten refresh interval if p95 > 45 s

**Checkpoint**: US5 independently testable.

---

## Phase 8: User Story 6 — Admin Provisioning & B2B Hooks (Priority: P2)

**Goal**: A super-admin invites a new admin; the invitee accepts, enrolls TOTP, and is usable. The role and permission model reserves `role_scope = vendor` for Phase 2 without requiring schema migration; `customer.company_owner` role is seeded and attachable (B2B hook, consumed by spec 021).

**Independent test**: Super-admin calls `POST /v1/admin/identity/invitations` with `roleCode=platform.finance` → `202`. Invite token lands in Dev OTP sink. Invitee runs `invitation/accept` → partial-auth token. Enroll TOTP → confirm → `AuthSession`. Assert `account_roles` row with `market_code=platform`. Separately, seed `customer.company_owner` assignment for a test account and assert `PolicyEvaluator` resolves its permission set.

### Contract + integration tests

- [X] T099 [P] [US6] Contract test `InviteAdmin_SuperAdminWithStepUp_Returns202` in `tests/Identity.Tests/Contract/Admin/InviteAdminContractTests.cs`
- [X] T100 [P] [US6] Contract test `InviteAdmin_NonSuperAdmin_Returns403` in same file
- [X] T101 [P] [US6] Contract test `RevokeInvitation_SuperAdmin_Succeeds` in same file
- [X] T102 [P] [US6] Contract test `ChangeAdminRole_SuperAdminWithStepUp_AuditsBeforeAndAfter` in `tests/Identity.Tests/Contract/Admin/ChangeRoleContractTests.cs`
- [X] T103 [P] [US6] Integration test `RoleScope_VendorReserved_AcceptsEnumButNotSeeded` (P6 multi-vendor-ready) in `tests/Identity.Tests/Integration/RoleScopeTests.cs`
- [X] T104 [P] [US6] Integration test `CustomerCompanyOwner_RolePermissionResolvesForB2B` in same file

### Slices

- [X] T105 [US6] Implement `Admin/InviteAdmin/{Request,Validator,Handler,Endpoint}.cs`
- [X] T106 [US6] Implement `Admin/RevokeInvitation/{Request,Validator,Handler,Endpoint}.cs`
- [X] T107 [US6] Implement `Admin/ChangeAdminRole/{Request,Validator,Handler,Endpoint}.cs` — audit emits both before and after role codes
- [X] T108 [US6] Implement `Admin/ListAdminMfaFactors/{…}.cs` + `Admin/Me/{…}.cs`
- [X] T109 [US6] Implement `Customer/Me/{…}.cs` + `Customer/SetLocale/{…}.cs`

**Checkpoint**: All 6 user stories independently functional. Full spec scope complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T110 [P] Run impeccable-brand overlay skip-check: this spec is backend-only (004), no UI; confirm CLAUDE.md D1 rule "Backend-only specs (001–013, 025–028) must not invoke impeccable" holds. Add a PR-description note.
- [X] T111 [P] Add `scripts/dev/scan-plaintext-secrets.sh` Serilog + EF log scanner (asserts no password/OTP/token plaintext in any log sink); wire into `Identity.Tests` integration baseline
- [X] T112 [P] Add `scripts/dev/identity-audit-spot-check.sh` asserting every state-machine transition in the integration suite produces an `audit_log_entries` row with matching correlation-id
- [X] T113 [P] Generate OpenAPI doc output at `services/backend_api/openapi.identity.json` and verify contract diff check (Guardrail #2) is green
- [X] T114 [P] Argon2id p95 microbench (tasks-level, advisory) at `tests/Identity.Tests/Unit/Argon2idBenchmarkTests.cs` — flags operator if tuning drifts > 30 % from target
- [X] T115 [P] Breach-list bloom false-positive bound property test at `tests/Identity.Tests/Unit/BreachListPropertyTests.cs`
- [X] T116 [P] AR editorial pass: ensure every reason code in `identity.ar.icu` has an editorial review (add `needs-ar-editorial-review: true` label on PR if any key lacks review sign-off)
- [X] T117 Run `./scripts/compute-fingerprint.sh` and paste output into the PR description (Guardrail #3)
- [X] T118 DoD walk-through per `docs/dod.md` v1.0 + spec-specific DoD in `quickstart.md`
- [X] T119 Run `services/backend_api/` full test suite (`dotnet test services/backend_api/ --filter Category!=Skip`) and attach contract-coverage report

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no deps.
- **Phase 2 (Foundational)**: depends on Phase 1. BLOCKS every user story.
- **Phases 3–8 (US1..US6)**: all depend only on Phase 2. Can proceed in parallel if staffed; natural priority order is US1 → US2/US3/US4 (parallel) → US5 → US6.
- **Phase 9 (Polish)**: depends on every user story needed for the target increment (MVP or full).

### Inter-story dependencies

- US3 (customer sign-in) consumes primitives; doesn't depend on US1 shipping first, but end-to-end scenarios assume a verified account — use seeded fixtures.
- US4 extends the `VerifyOtp` handler shipped in US1 — coordinate T084 with T051.
- US5 assumes at least one sign-in path is live (US1/US3).
- US6 assumes the invitation + MFA slices from US2 are live — US6 starts after US2.

### Parallel opportunities

- Phase 2: T006–T039 almost entirely `[P]` — the 9 state machines can ship in parallel, the 17 entities + configurations likewise.
- Per user-story phase: every contract test task marked `[P]` within a phase can run in parallel; slice implementation tasks touching different files are parallelizable; tasks within a single slice folder are sequential.
- Across phases: US1/US2/US3/US4 MVP can run in parallel by 2–4 engineers if coordinated on the shared message bundles (T052, T067, T078, T089).

---

## Task counts

| Phase | Tasks |
|---|---|
| 1 · Setup | 5 |
| 2 · Foundational | 34 |
| 3 · US1 (Register/Verify) | 14 |
| 4 · US2 (Admin Sign-In + MFA) | 14 |
| 5 · US3 (Customer Sign-In + Audit) | 11 |
| 6 · US4 (OTP Sign-In + Password Reset) | 11 |
| 7 · US5 (Session Control) | 9 |
| 8 · US6 (Admin Provisioning + B2B Hooks) | 11 |
| 9 · Polish | 10 |
| **Total** | **119** |

**MVP scope**: Phases 1 + 2 + 3 + 4 + 5 + 6 + 9 (subset) = US1..US4 + polish-lite. Full spec scope = all phases.

---

## Remediation Tracking (CODEX_FIX_PROMPT.md)

- [X] Remediation-0001 F-01 Register response token leak removed; verification token dispatch moved to `IIdentityEmailDispatcher`.
- [X] Remediation-0002 F-02 OTP request response code leak removed; challenge ID only is returned.
- [X] Remediation-0003 F-03 Password-reset and admin-invitation raw token delivery restored through `IIdentityEmailDispatcher`.
- [X] Remediation-0004 F-04 `seed-admin` hardened (no hardcoded password, no `--force`, role-join existence check, pending rotation status, pending MFA provisioning token record).
- [X] Remediation-0005 F-05 Admin MFA tier enforcement added (`412 identity.mfa.enrollment_required` for required-role accounts without confirmed factor).
- [X] Remediation-0006 F-06 `TotpSecretCodec` now fails closed with versioned payload header and `TotpSecretUnprotectFailed` handling.
- [X] Remediation-0007 F-07 JWT signing/validation now uses a unified hosted key provider, supports retired validation keys + per-surface JWKS, and uses dev file fallback under `infra/dev-keys`.
- [X] Remediation-0008 F-08 Duplicate register path no longer creates shadow accounts/challenges and emits duplicate-rejected audit metadata only.
- [X] Remediation-0009 F-09 Minimal API authorization converted to endpoint filters with permission + step-up enforcement on identity endpoints.
- [X] Remediation-0010 F-10 `AuthorizationAuditEmitter` now writes `identity.authorization_audit` rows in addition to cross-cutting audit publication.
- [X] Remediation-0011 F-11 Refresh token revocation persistence now retries and fails closed on DB errors (no cache-only success path).
- [X] Remediation-0012 F-12 Customer sign-in now accepts identifier-based auth (email or normalized phone).
- [X] Remediation-0013 F-13 Customer lockout now progresses across tiers (1m → 5m → 30m → admin-unlock state).
- [X] Remediation-0014 F-14 Admin default access-token TTL is now 5 minutes (customer remains 15).
- [X] Remediation-0015 F-15 Rate-limit thresholds aligned to spec and deny events now persist in `identity.rate_limit_events`.
- [X] Remediation-0016 F-16 OTP request now invalidates prior pending challenges and enforces per-identifier recent-count cap in handler.
- [X] Remediation-0017 F-17 OTP verify now requires identifier binding and rejects destination/account mismatches.
- [X] Remediation-0018 F-18 Refresh/session/email-reset token lookup now uses `token_id.token_secret` direct lookup with constant-time secret hash verification.
- [X] Remediation-0019 F-19 Email confirmation token flow now uses direct token-id lookup (no bounded recent-scan).
- [X] Remediation-0020 F-20 User-initiated revoke paths now flow through `IRefreshTokenRevocationStore` cache-aware persistence (no direct table bypass).
- [X] Remediation-0021 F-21 Admin invitation now validates admin role scope/system-flag, blocks duplicate active-pending invitations, and preserves secure dispatch behavior.
- [X] Remediation-0022 F-22 Sign-in/refresh now gate on account status and fail closed for inactive customer/admin accounts.
- [X] Remediation-0023 F-23 Refresh now checks session status + revocation cache + account status, with client fingerprint mismatch replay detection + chain invalidation + security audit emission.
- [X] Remediation-0024 F-24 `Account` now includes professional verification fields + optional company reference, and active-account `market_code` changes are blocked unless admin market-change scope is active.
- [X] Remediation-0025 F-25 OTP verify now applies purpose-specific side effects/audit actions, no longer increments attempts on success, and activates `pending_phone_verification` accounts once prerequisites are met.
- [X] Remediation-0026 F-26 Password-reset completion now uses transactional token consumption/update/revoke flow with optimistic consume-guard and emits `password.reset.completed` audit events.
- [X] Remediation-0028 F-28 `IdentitySaveChangesInterceptor` now handles `UpdatedAt` only and no longer emits generic actor-less audit noise.
- [X] Remediation-0027 F-27 OTP challenge default max-attempt cap set to 3, with admin step-up OTP issuing 8-digit/3-minute challenges.
- [X] Remediation-0029 F-29 Permission propagation now follows Option A: JWTs carry `permission_version` only, permissions resolve from DB per-request via short-lived cache, and role changes increment `accounts.permission_version`.
- [X] Remediation-0030 F-30 Endpoint authorization now uses endpoint-specific admin permission codes (`identity.admin.invitation.revoke`, `identity.admin.session.revoke`) and the admin guard no longer hardcodes `identity.admin.session.manage` or super-admin role DB checks.
- [X] Remediation-0031 F-31 Admin partial-auth + admin MFA challenge stores are now Postgres-backed (`admin_partial_auth_tokens`, `admin_mfa_challenges`) with challenge attempt caps/exhaustion and challenge consumption on successful MFA.
- [X] Remediation-0032 F-32 Anti-enumeration timing pads now use non-blocking `Task.Delay` only via `ConstantTimeOperation`, with ~500ms minimum applied to register, sign-in, OTP request, and password-reset request paths.
- [X] Remediation-0033 F-33 Audit coverage expanded for sign-in, lockout, MFA, step-up, permission change, admin session revoke, rate-limit reject, customer password-change (`password.changed`), and admin TOTP rotation (`admin.mfa.totp_rotated`) events; recovery-code consumption covered by F-43. Account-deactivation and recovery-code-regeneration endpoints are out of scope for spec 004 (deactivation is Phase 1D per spec.md; no regenerate endpoint was specified); their audit actions will land with those endpoints.
- [X] Remediation-0034 F-34 `ConstantTimeOperation.EqualizeAsync(...)` added and non-existent identifier branches now execute dummy Argon2id + dummy account read in sign-in/password-reset/OTP request handlers.
- [X] Remediation-0035 F-35 `AccountStateMachine` now models `pending_password_rotation` with explicit `Active → pending_password_rotation` and `pending_password_rotation → Active` transitions plus unit coverage.
- [X] Remediation-0036 F-36 Client/network-sensitive hashes now use peppered HMAC (`IdentityClientSecurityHasher`) for `client_ip_hash` and OTP `destination_hash` generation/verification.
- [X] Remediation-0037 F-37 Bloom-filter index derivation now uses unsigned modulus (`ToUInt32 % size`) in both revocation/breach flows, eliminating `Math.Abs(int.MinValue)` risk.
- [X] Remediation-0038 F-38 Permission-claim DB resolution now runs fully async under the F-29 Option A implementation (no sync DB permission-resolution path remains in session issuance).
- [X] Remediation-0039 F-39 Added periodic retention purge worker (`IdentityMaintenancePurgeWorker`) for `admin_mfa_replay_guard` (10m) and `revoked_refresh_tokens` (90d), plus supporting timestamp indexes.
- [X] Remediation-0040 F-40 Connection string resolution remains fail-fast outside Test via `ResolveRequiredDefaultConnectionString`, and `IdentityModule` uses a single DI-registered `NpgsqlDataSource` for both `IdentityDbContext` and `RefreshTokenRevocationStore`.
- [X] Remediation-0041 F-41 Token lifetimes updated: password-reset tokens now expire after 60 minutes and email-verification tokens after 24 hours, with contract coverage and documented research rationale.
- [X] Remediation-0042 F-42 `IdentityTestFactory` no longer mutates process-wide `Environment` connection-string variables; configuration overrides are in-memory only and `UseEnvironment(\"Test\")` is explicit.
- [X] Remediation-0043 F-43 Admin MFA completion now accepts recovery codes, verifies Argon2-hashed entries in constant-time across all stored codes, consumes matched codes once, and emits `admin.mfa.recovery_code_consumed`.
- [X] Remediation-0044 F-44 Message completeness coverage now asserts all emitted `identity.*` reason-code literals resolve in both AR and EN bundles, with missing keys added (`identity.authorization.decision`, `identity.refresh.fingerprint_mismatch`).
- [X] Remediation-0045 F-45 `ChangeAdminRole` now rejects non-platform / non-system roles (blocks privilege-scope escalation where a customer or vendor role could be attached to an admin account).
- [X] Remediation-0046 F-46 `AcceptInvitation` now enforces admin-tier password policy (12+ chars, 3 character classes, breach-list check) and emits an `admin.invitation.accepted` audit event.
- [X] Remediation-0047 F-47 `EnrollTotp` now revokes any prior pending/active TOTP factor for the account before creating the new one (prevents unique-index violation when the seed-admin CLI pre-provisioned a pending factor); authenticator issuer renamed from `BuidSass` to `Dental Commerce Platform`.
- [X] Remediation-0048 F-48 `AcceptInvitation` now does a direct `TokenHash` lookup instead of the top-256 FixedTimeEquals scan (parity with F-18 for refresh/email/reset tokens); invitation lookup is O(log n) and correct for any corpus size.
- [X] Remediation-0049 F-49 `RevokeInvitation` now emits `admin.invitation.revoked` audit with actor id, before/after state.
- [X] Remediation-0050 F-50 `CompleteStepUpOtp` no longer increments `challenge.Attempts` on a successful verify; dead `permission`-claim forwarding loop replaced with `permission_version` forwarding (aligns with F-29 Option A).
- [X] Remediation-0051 F-51 OTP numeric code generation now uses rejection sampling (reject bytes ≥ 250) to eliminate the ~2% modulo bias in `raw[i] % 10`; applied to both customer (6-digit) and admin step-up (8-digit) generators.
- [X] Remediation-0052 F-52 `CompleteMfaChallenge` now emits an `admin.mfa.verification_failed` audit event with reason `identity.mfa.secret_unprotect_failed` before returning 503 so operators can diagnose DataProtection key-ring loss.
- [X] Remediation-0053 F-53 DataProtection now has explicit persistence: `Identity:DataProtection:KeyRingPath` config overrides the path; Dev/Test falls back to `infra/dev-keys/dataprotection/`; Staging/Prod fails fast if unconfigured. Fixes TOTP-secret ephemerality across process restarts and horizontal scale.
- [X] Remediation-0054 F-54 AR/EN message bundles now include `identity.invitation.password_too_weak`.

---

## Implementation strategy

1. **Setup week**: Phase 1 + lay down Primitives in Phase 2 (T006–T014). One engineer.
2. **Foundational sprint** (~1 week): Complete Phase 2 (state machines, entities, migrations, seeder, RBAC, JWT, test harness). One engineer + parallel reviewer.
3. **MVP sprint** (~2 weeks): US1–US4 in parallel across 2–3 engineers. Daily message-bundle merge coordination.
4. **P2 extension sprint** (~1 week): US5 + US6. Can overlap with the start of spec 005 (catalog).
5. **Polish + DoD gate** (~2 days): Phase 9, fingerprint, AR editorial, PR.

Critical path: Phase 2 → US2 → US6 (admin provisioning feeds the entire admin web app that spec 015 expects).

---

## Format validation

All 119 tasks above conform to the required format: `- [ ] Tnnn [P?] [US?] Description with file path`. Checklist boxes present, sequential IDs, story labels on Phase 3–8 only, file paths on every implementation task, `[P]` only where file-set independence is genuine.
