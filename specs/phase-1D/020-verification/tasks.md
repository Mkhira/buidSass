---
description: "Phase-1D Spec 020 — Professional Verification: dependency-ordered task list"
---

# Tasks: Professional Verification (Spec 020)

**Input**: Design documents from `/specs/phase-1D/020-verification/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/verification-contract.md](./contracts/verification-contract.md), [quickstart.md](./quickstart.md)

**Tests**: Tests are included throughout. Spec 003 / spec 004 established the project-wide xUnit + Testcontainers + WebApplicationFactory pattern; the `Verification.Tests` project is a hard DoD requirement (see [plan.md §Implementation Phases · M](./plan.md) and [quickstart.md §6](./quickstart.md)).

**Organization**: Tasks are grouped by user story to enable independent implementation. The three P1 stories (US1, US2, US3) form the launch MVP; US1's independent test from `spec.md` deliberately spans all three, so they're a coupled MVP slice. US4 (P2), US5 (P2), and US6 (P3) layer on cleanly.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file, no dependency on incomplete tasks → can run in parallel.
- **[Story]**: `US1`–`US6` map to user stories in `spec.md`. Setup, Foundational, and Polish phases carry no story label.
- File paths are absolute relative to the repo root.

## Path Conventions (per [plan.md §Project Structure](./plan.md))

- Module code: `services/backend_api/Modules/Verification/**`
- Cross-module hooks: `services/backend_api/Modules/Shared/**` (project-memory rule)
- Tests: `services/backend_api/tests/Verification.Tests/**`
- ICU localization: `services/backend_api/Modules/Verification/Messages/**`
- OpenAPI artifact: `services/backend_api/openapi.verification.json`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Spin up the empty `Modules/Verification/` module and its sibling test project so subsequent phases land in a building tree.

- [X] T001 Create directory skeleton `services/backend_api/Modules/Verification/{Primitives,Customer,Admin,Eligibility,Workers,Authorization,Hooks,Entities,Persistence/Configurations,Persistence/Migrations,Messages,Seeding}` per [plan.md §Project Structure](./plan.md)
- [X] T002 Create `services/backend_api/Modules/Verification/VerificationModule.cs` with `AddVerificationModule(IServiceCollection, IConfiguration)` extension; register `AddDbContext<VerificationDbContext>` suppressing `RelationalEventId.ManyServiceProvidersCreatedWarning` (project-memory rule); leave service registrations empty for now
- [X] T003 Wire `AddVerificationModule` into `services/backend_api/Program.cs` and add `app.MapVerificationEndpoints()` placeholder so the module is composed at startup
- [X] T004 [P] Create test-project skeleton `services/backend_api/tests/Verification.Tests/{Unit,Integration,Contract,Benchmarks}` with `Verification.Tests.csproj` referencing `backend_api.csproj`, xUnit, FluentAssertions, `WebApplicationFactory<Program>`, Testcontainers.PostgreSql, `Microsoft.Extensions.TimeProvider.Testing`
- [X] T005 [P] Create `services/backend_api/Modules/Verification/Messages/verification.en.icu` and `verification.ar.icu` as empty ICU bundles (keys added per slice); create `services/backend_api/Modules/Verification/Messages/AR_EDITORIAL_REVIEW.md` per spec 008 pattern (tracks AR keys pending editorial sign-off)
- [X] T006 [P] Add `services/backend_api/openapi.verification.json` placeholder file so the OpenAPI emitter writes here (matches spec 004/008 convention)

**Checkpoint**: `dotnet build services/backend_api` is green; `dotnet test services/backend_api/tests/Verification.Tests` runs (zero tests).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Primitives + persistence + reference data + cross-module seams that every user story phase consumes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Primitives

- [X] T007 [P] Create `services/backend_api/Modules/Verification/Primitives/VerificationState.cs` enum: `Submitted, InReview, Approved, Rejected, InfoRequested, Expired, Revoked, Superseded, Void` (per [data-model.md §3.1](./data-model.md))
- [X] T008 [P] Create `services/backend_api/Modules/Verification/Primitives/VerificationActorKind.cs` enum: `Customer, Reviewer, System`
- [X] T009 [P] Create `services/backend_api/Modules/Verification/Primitives/VerificationReasonCode.cs` enum + ICU-key mapper for every customer-visible reason (full enum listed in [contracts/verification-contract.md §7](./contracts/verification-contract.md))
- [X] T010 [P] Create `services/backend_api/Modules/Verification/Primitives/EligibilityReasonCode.cs` enum + `EligibilityClass` enum + `EligibilityResult` record (per [data-model.md §4](./data-model.md) and [contracts/verification-contract.md §4.1](./contracts/verification-contract.md))
- [X] T011 [P] Create `services/backend_api/Modules/Verification/Primitives/VerificationStateMachine.cs`: `CanTransition(from, to, actorKind)` predicate enforcing every allowed transition + every forbidden transition listed in [data-model.md §3.2](./data-model.md); single-method API; pure, no DI
- [X] T012 [P] Create `services/backend_api/Modules/Verification/Primitives/BusinessDayCalculator.cs`: `AddBusinessDays(start, businessDays, weekendDays, holidaysList)` pure function; weekend defaulted to Sun–Thu working week (research.md §R2)
- [X] T013 [P] Create `services/backend_api/Modules/Verification/Primitives/VerificationMarketPolicy.cs` value-object resolved from a `VerificationMarketSchema` row (retention, cooldown, expiry, reminder windows, SLA bounds, holidays, allowed document types, required-fields schema)

#### Foundational unit tests

- [X] T014 [P] Create `services/backend_api/tests/Verification.Tests/Unit/VerificationStateMachineTests.cs`: every allowed transition returns true; every forbidden transition (terminal→non-terminal, any→Submitted, InfoRequested→Approved/Rejected/Revoked direct) returns false
- [X] T015 [P] Create `services/backend_api/tests/Verification.Tests/Unit/BusinessDayCalculatorTests.cs`: Sun–Thu week; spans across weekends; respects holidays list; SLA `T0+1` and `T0+2` arithmetic deterministic per [research.md §R2](./research.md)
- [X] T016 [P] Create `services/backend_api/tests/Verification.Tests/Unit/EligibilityReasonCodeIcuKeysTests.cs`: every `EligibilityReasonCode` enum value has an entry in both `verification.en.icu` and `verification.ar.icu`

### Persistence

- [X] T017 Create `services/backend_api/Modules/Verification/Entities/Verification.cs` with all columns from [data-model.md §2.1](./data-model.md) including `xmin` mapped via `IsRowVersion()`
- [X] T018 [P] Create `services/backend_api/Modules/Verification/Entities/VerificationDocument.cs` per [data-model.md §2.2](./data-model.md)
- [X] T019 [P] Create `services/backend_api/Modules/Verification/Entities/VerificationStateTransition.cs` per [data-model.md §2.3](./data-model.md) (append-only)
- [X] T020 [P] Create `services/backend_api/Modules/Verification/Entities/VerificationMarketSchema.cs` per [data-model.md §2.4](./data-model.md) (versioned)
- [X] T021 [P] Create `services/backend_api/Modules/Verification/Entities/VerificationReminder.cs` per [data-model.md §2.5](./data-model.md)
- [X] T022 [P] Create `services/backend_api/Modules/Verification/Entities/VerificationEligibilityCache.cs` projection per [data-model.md §2.6](./data-model.md)
- [X] T023 Create `services/backend_api/Modules/Verification/Persistence/VerificationDbContext.cs` registering all six entities + `OnConfiguring` warning suppression
- [X] T024 [P] Create `services/backend_api/Modules/Verification/Persistence/Configurations/VerificationConfiguration.cs` (table, indexes IX_verifications_customer_state_market / IX_verifications_state_market_submitted partial / IX_verifications_expires_at partial / IX_verifications_supersedes; xmin mapping)
- [X] T025 [P] Create `services/backend_api/Modules/Verification/Persistence/Configurations/VerificationDocumentConfiguration.cs` (constraints + indexes per data-model)
- [X] T026 [P] Create `services/backend_api/Modules/Verification/Persistence/Configurations/VerificationStateTransitionConfiguration.cs` plus EF migration that adds the `BEFORE UPDATE OR DELETE` Postgres trigger enforcing append-only semantics
- [X] T027 [P] Create `services/backend_api/Modules/Verification/Persistence/Configurations/VerificationMarketSchemaConfiguration.cs` (composite PK + unique partial index "one active per market")
- [X] T028 [P] Create `services/backend_api/Modules/Verification/Persistence/Configurations/VerificationReminderConfiguration.cs` (UNIQUE `(verification_id, window_days)`)
- [X] T029 [P] Create `services/backend_api/Modules/Verification/Persistence/Configurations/VerificationEligibilityCacheConfiguration.cs` (PK on `customer_id`)
- [X] T030 Generate initial EF migration `dotnet ef migrations add VerificationInit --context VerificationDbContext --output-dir Modules/Verification/Persistence/Migrations` and verify the migration creates 6 tables + the append-only trigger
- [X] T031 Add `IDbContextFactory<VerificationDbContext>` registration to `VerificationModule.cs` so background workers can construct scopes outside the request pipeline

### Cross-module hooks (live in `Modules/Shared/`)

- [X] T032 [P] Create `services/backend_api/Modules/Shared/ICustomerVerificationEligibilityQuery.cs` with `EvaluateAsync` + `EvaluateManyAsync` signatures from [contracts §4.1](./contracts/verification-contract.md); add XML doc with the SC-004 latency budget (p95 ≤ 5 ms)
- [X] T033 [P] Create `services/backend_api/Modules/Shared/ICustomerAccountLifecycleSubscriber.cs` + the three event records (`CustomerAccountLocked`, `CustomerAccountDeleted`, `CustomerMarketChanged`) per [contracts §4.2](./contracts/verification-contract.md)
- [X] T034 [P] Create `services/backend_api/Modules/Shared/IProductRestrictionPolicy.cs` + `ProductRestrictionPolicy` record per [contracts §4.3](./contracts/verification-contract.md). Implementation owned by spec 005 — declare here so 020 can consume without cycle (project-memory rule)
- [X] T035 [P] Create `services/backend_api/Modules/Shared/IRegulatorAssistLookup.cs` + `RegulatorAssistResult` record per [contracts §4.4](./contracts/verification-contract.md); add `NullRegulatorAssistLookup` returning `null` and register as default DI binding (FR-016a / FR-016b)
- [X] T036 [P] Create `services/backend_api/Modules/Shared/VerificationDomainEvents.cs` with all eight records from [data-model.md §6](./data-model.md): `VerificationApproved`, `VerificationRejected`, `VerificationInfoRequested`, `VerificationRevoked`, `VerificationExpired`, `VerificationReminderDue`, `VerificationSuperseded`, `VerificationVoided`
- [X] T037 Create `services/backend_api/Modules/Verification/Eligibility/EligibilityCacheInvalidator.cs` with `RebuildAsync(customerId, dbContext, ct)` reading authoritative state and UPSERTing `verification_eligibility_cache` (no I/O outside the passed `DbContext`); to be called inside every state-transition Tx

### Authorization + audit chokepoint

- [X] T038 [P] Create `services/backend_api/Modules/Verification/Authorization/VerificationPermissions.cs` with constants `verification.review`, `verification.revoke`, `verification.read_pii`, `verification.read_summary` (per [research.md §R9](./research.md))
- [X] T039 [P] Create `services/backend_api/Modules/Verification/Primitives/IPiiAccessRecorder.cs` + `PiiAccessRecorder` impl writing `verification.pii_access` events via `IAuditEventPublisher` per [research.md §R13](./research.md)

### Reference data seed

- [X] T040 Create `services/backend_api/Modules/Verification/Seeding/VerificationReferenceDataSeeder.cs` implementing the platform `ISeeder` interface; idempotent INSERT of the KSA + EG schema rows from [quickstart.md §2](./quickstart.md) (KSA retention=24mo, EG retention=36mo, both reminder_windows=[30,14,7,1], SLA decision=2/warning=1, allowed types=[pdf,jpeg,png,heic])
- [X] T041 Register `VerificationReferenceDataSeeder` in `VerificationModule.cs` so the platform `seed --mode=apply --tag=verification-reference` includes it

#### Foundational integration tests

- [X] T042 [P] Create `services/backend_api/tests/Verification.Tests/Integration/VerificationDbContextSmokeTests.cs`: spins up Testcontainers Postgres, applies migrations, runs the seeder, asserts `verification_market_schemas` has 2 rows (KSA v1 + EG v1) and the unique-active partial index rejects a second active row per market
- [X] T043 [P] Create `services/backend_api/tests/Verification.Tests/Integration/StateTransitionAppendOnlyTriggerTests.cs`: verify the append-only Postgres trigger raises on UPDATE / DELETE of `verification_state_transitions`

**Checkpoint**: Foundation ready. `dotnet test` passes T014–T016 + T042–T043. User story implementation can now begin.

---

## Phase 3: User Story 1 — Customer submits and is approved (Priority: P1) 🎯 MVP

**Goal**: A dental professional submits verification end-to-end (form → upload → submit → status), and receives the customer-side surface needed to track and renew. (US2 supplies the admin approval; US3 supplies the eligibility flip — together they form the launch MVP per spec.md.)

**Independent Test**: A KSA dentist on the local API submits a verification with one clean document; the row appears in state `submitted` with the correct `schema_version`; eligibility cache is `Ineligible: VerificationPending`; an audit event `verification.state_changed (__none__ → submitted, customer)` is written.

### Tests for User Story 1 (write first, ensure they FAIL before implementation)

- [ ] T044 [P] [US1] Create `services/backend_api/tests/Verification.Tests/Contract/SubmitVerificationContractTests.cs`: covers every error reason code in [contracts/verification-contract.md §2.1](./contracts/verification-contract.md) (`required_field_missing`, `regulator_identifier_invalid`, `documents_invalid`, `already_pending`, `cooldown_active`, `account_inactive`) plus the 201 happy path
- [ ] T045 [P] [US1] Create `services/backend_api/tests/Verification.Tests/Contract/AttachDocumentContractTests.cs` covering [contracts §2.5](./contracts/verification-contract.md): 201 happy path + `document_too_large`, `document_type_not_allowed`, `document_aggregate_exceeded`, `document_scan_failed`, `invalid_state_for_action`
- [ ] T046 [P] [US1] Create `services/backend_api/tests/Verification.Tests/Contract/ResubmitWithInfoContractTests.cs` covering [contracts §2.6](./contracts/verification-contract.md): state moves to `in-review` (not `submitted`), `submitted_at` preserved, `decided_at` reset; `no_changes_provided` rejected
- [ ] T047 [P] [US1] Create `services/backend_api/tests/Verification.Tests/Contract/GetMyVerificationContractTests.cs` covering [contracts §2.2 + §2.3 + §2.4](./contracts/verification-contract.md): owner-only access (404 for foreign id); list pagination; active endpoint returns null when no verification
- [ ] T048 [P] [US1] Create `services/backend_api/tests/Verification.Tests/Contract/RequestRenewalContractTests.cs` covering [contracts §2.7](./contracts/verification-contract.md): `renewal_window_not_open` outside window, `no_active_approval` if no approval, `renewal_already_pending` on duplicate; happy path creates row with `supersedes_id` set
- [ ] T049 [P] [US1] Create `services/backend_api/tests/Verification.Tests/Integration/CustomerSubmissionLocaleTests.cs` asserting both AR and EN error responses carry localized `title` + `detail` (FR-031 / FR-032 / FR-033)

### Implementation for User Story 1

- [X] T050 [US1] Create `services/backend_api/Modules/Verification/Customer/SubmitVerification/SubmitVerificationRequest.cs` (DTO with `profession`, `regulator_identifier`, `document_ids[]`)
- [X] T051 [US1] Create `services/backend_api/Modules/Verification/Customer/SubmitVerification/SubmitVerificationValidator.cs` (FluentValidation, resolves the customer's market's active schema and validates `required_fields` jsonb dynamically)
- [X] T052 [US1] Create `services/backend_api/Modules/Verification/Customer/SubmitVerification/SubmitVerificationHandler.cs` (MediatR; transactional: schema lookup → cool-down check → no-other-non-terminal check → snapshot `IProductRestrictionPolicy` → INSERT `Verification` + `VerificationStateTransition` + attach documents → `EligibilityCacheInvalidator.RebuildAsync` → publish `IAuditEventPublisher` event → publish in-process domain event)
- [X] T053 [US1] Create `services/backend_api/Modules/Verification/Customer/SubmitVerification/SubmitVerificationEndpoint.cs` mapping `POST /api/customer/verifications` requiring `Idempotency-Key`
- [ ] T054 [P] [US1] Create slice `services/backend_api/Modules/Verification/Customer/AttachDocument/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.5](./contracts/verification-contract.md) — wraps `IStorageService.UploadAsync` + `IVirusScanService.ScanAsync`, writes `VerificationDocument` row only when scan returns `clean`
- [ ] T055 [P] [US1] Create slice `services/backend_api/Modules/Verification/Customer/GetMyActiveVerification/{Request,Handler,Endpoint}.cs` returning the most recent non-terminal or active-approved verification with `renewal_open` + `next_action` derived per [contracts §2.2](./contracts/verification-contract.md)
- [ ] T056 [P] [US1] Create slice `services/backend_api/Modules/Verification/Customer/ListMyVerifications/{Request,Handler,Endpoint}.cs` per [contracts §2.3](./contracts/verification-contract.md)
- [ ] T057 [P] [US1] Create slice `services/backend_api/Modules/Verification/Customer/GetMyVerification/{Request,Handler,Endpoint}.cs` enforcing owner check, returning transitions + documents metadata per [contracts §2.4](./contracts/verification-contract.md)
- [ ] T058 [US1] Create slice `services/backend_api/Modules/Verification/Customer/ResubmitWithInfo/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.6](./contracts/verification-contract.md) — preserves original `submitted_at`, transitions `info-requested → in-review`, requires at least one change since the info-request
- [ ] T059 [US1] Create slice `services/backend_api/Modules/Verification/Customer/RequestRenewal/{Request,Validator,Handler,Endpoint}.cs` per [contracts §2.7](./contracts/verification-contract.md) — opens `RequestRenewal` only inside the earliest reminder window; sets `supersedes_id`; existing approval stays active until renewal decides
- [ ] T060 [US1] Add ICU keys for every customer-visible reason code touched by US1 to `Modules/Verification/Messages/verification.en.icu`
- [ ] T061 [US1] Add Arabic ICU keys to `verification.ar.icu` and append the keys to `AR_EDITORIAL_REVIEW.md` for editorial sign-off (Principle 4)
- [ ] T062 [US1] Re-emit `services/backend_api/openapi.verification.json` to reflect every customer endpoint added in this phase; intermediate regenerations across phases land at every story phase boundary (T078 / T103 / T109) and a final consolidated pass at T116 verifies Guardrail #2 contract diff

**Checkpoint**: Customer surface is fully functional in isolation — a customer can submit, list, view, attach documents, resubmit, and request renewal. Independent test (per spec.md US1) becomes possible once US2 lands the admin approval.

---

## Phase 4: User Story 2 — Admin reviewer queue and decisioning (Priority: P1) 🎯 MVP

**Goal**: An admin reviewer holding `verification.review` opens the queue, inspects a submission, and decides it (approve / reject / request-info / open historical document); each decision requires a reason; concurrency is guarded; every decision is audited.

**Independent Test**: With a customer submission already in `submitted` (from US1), a reviewer with `verification.review` lists the queue (filtered to KSA, oldest-first), opens detail, approves with a reason, and the verification transitions to `approved` with `expires_at` set; concurrent second reviewer sees `verification.already_decided`; audit log records actor + reason.

### Tests for User Story 2

- [X] T063 [P] [US2] Create `services/backend_api/tests/Verification.Tests/Contract/AdminQueueContractTests.cs` covering [contracts §3.1](./contracts/verification-contract.md): RBAC (403 without `verification.review`); market scope (no cross-market leak); default oldest-first; SLA signal `ok|warning|breach` rendered
- [X] T064 [P] [US2] Create `services/backend_api/tests/Verification.Tests/Contract/AdminDetailContractTests.cs` covering [contracts §3.2](./contracts/verification-contract.md): renders schema-as-submitted (FR-026), full transition history, document metadata only
- [X] T065 [P] [US2] Create `services/backend_api/tests/Verification.Tests/Contract/AdminApproveContractTests.cs` covering [contracts §3.3](./contracts/verification-contract.md): reason required, `expires_at` set, audit event written, `VerificationApproved` domain event published
- [ ] T066 [P] [US2] Create `services/backend_api/tests/Verification.Tests/Contract/AdminRejectContractTests.cs` covering [contracts §3.4](./contracts/verification-contract.md)
- [ ] T067 [P] [US2] Create `services/backend_api/tests/Verification.Tests/Contract/AdminRequestInfoContractTests.cs` covering [contracts §3.5](./contracts/verification-contract.md) including SLA-timer-pause assertion (FR-039)
- [ ] T068 [P] [US2] Create `services/backend_api/tests/Verification.Tests/Contract/OpenHistoricalDocumentContractTests.cs` covering [contracts §3.7](./contracts/verification-contract.md): non-terminal returns signed URL; terminal-state returns signed URL + writes a separate `verification.pii_access (DocumentBodyRead, surface=admin_review)` audit event; purged document returns `410 verification.document_purged`
- [X] T069 [US2] Create `services/backend_api/tests/Verification.Tests/Integration/AdminDecisionConcurrencyTests.cs`: 100 simulated parallel approve/reject calls on a single submission via `Parallel.ForEachAsync` → exactly one decision wins; loser receives `verification.already_decided`; no double audit event (SC-007)
- [ ] T069a [P] [US2] Create `services/backend_api/tests/Verification.Tests/Integration/ReviewerReasonLocaleTests.cs` (FR-033): empty `{}` reason → 400 `verification.reason_required`; reason with only `en` and customer locale `ar` → decision commits, customer-facing notification renders the EN reason with the "(reviewer left this in English)" notice in AR; reason with both locales → audit log preserves both, customer rendering uses preferred locale only
- [ ] T069b [P] [US2] Create `services/backend_api/tests/Verification.Tests/Integration/AdminQueueSlaBreachTests.cs` (FR-039 / spec edge case "all reviewers in a market are out of office"): seed 3 verifications submitted 3 business days ago with no decisions → `GET /api/admin/verifications` returns each row with `sla_signal=breach`; seed one submitted 1.5 business days ago → `sla_signal=warning`; verifications in `info-requested` for 3 business days while customer hasn't resubmitted → `sla_signal=ok` (timer paused)

### Implementation for User Story 2

- [X] T070 [US2] Create slice `services/backend_api/Modules/Verification/Admin/ListVerificationQueue/{Request,Handler,Endpoint}.cs` per [contracts §3.1](./contracts/verification-contract.md); compute SLA signal per row using `BusinessDayCalculator` against the snapshotted schema; apply market scope from reviewer's claims
- [X] T071 [US2] Create slice `services/backend_api/Modules/Verification/Admin/GetVerificationDetail/{Request,Handler,Endpoint}.cs` per [contracts §3.2](./contracts/verification-contract.md); resolve schema by `schema_version` for FR-026; include `customer_locale` field in the response (FR-033 — resolved from spec 004 identity context); call `IRegulatorAssistLookup.LookupAsync` and include the returned object in a `regulator_assist` response field when non-null (FR-016b — V1's `NullRegulatorAssistLookup` always returns null so the field is absent in V1; a future Phase 1.5 swap-in needs no contract change)
- [X] T072 [US2] Create slice `services/backend_api/Modules/Verification/Admin/DecideApprove/{Request,Validator,Handler,Endpoint}.cs` per [contracts §3.3](./contracts/verification-contract.md): request body accepts `{ reason: { en?, ar? } }` (FR-033) — validator requires at least one locale, rejects empty reason with `verification.reason_required`; uses xmin optimistic concurrency; sets `expires_at = now + market.expiry_days`; if `supersedes_id` not null, transitions prior approval to `superseded` in same Tx; rebuilds eligibility cache; publishes `VerificationApproved` and audit event with both locales preserved
- [ ] T073 [P] [US2] Create slice `services/backend_api/Modules/Verification/Admin/DecideReject/{Request,Validator,Handler,Endpoint}.cs` per [contracts §3.4](./contracts/verification-contract.md); same `{ reason: { en?, ar? } }` body shape as T072; writes `cooldown_until` to response payload
- [ ] T074 [P] [US2] Create slice `services/backend_api/Modules/Verification/Admin/DecideRequestInfo/{Request,Validator,Handler,Endpoint}.cs` per [contracts §3.5](./contracts/verification-contract.md); same `{ reason: { en?, ar? } }` body shape as T072; SLA-timer-pause logic captured by recording `paused_at` in transition metadata for the queue handler to honor
- [ ] T075 [US2] Create slice `services/backend_api/Modules/Verification/Admin/OpenHistoricalDocument/{Request,Handler,Endpoint}.cs` per [contracts §3.7](./contracts/verification-contract.md); call `IPiiAccessRecorder` on every read; return `410` when `purged_at IS NOT NULL`
- [ ] T076 [US2] Wire `[RequirePermission("verification.review")]` and (where appropriate) `[RequirePermission("verification.revoke")]` attributes on every admin endpoint; verify by integration test that omitting the permission returns 403
- [ ] T077 [US2] Add ICU keys + AR translations for every reviewer-facing string + every customer-visible decision reason summary; append AR keys to `AR_EDITORIAL_REVIEW.md`
- [ ] T078 [US2] Re-emit `openapi.verification.json` to include admin endpoints; verify contract diff

**Checkpoint**: US1 + US2 together complete the customer-and-reviewer round trip end-to-end. The MVP audit story (FR-028 / SC-003) is now testable: replay any verification's history from `audit_log_entries` alone.

---

## Phase 5: User Story 3 — Eligibility query consumed by catalog/cart/checkout (Priority: P1) 🎯 MVP

**Goal**: `ICustomerVerificationEligibilityQuery` is the single authoritative source of "may this customer purchase this restricted SKU?". Catalog (005), cart (009), and checkout (010) consume it without reimplementing the policy. Reason codes are stable. Latency budget locked.

**Independent Test**: A customer with `approved` verification + matching profession + matching market queries the hook for a restricted SKU → `Eligible`. Force-expire the verification → `Ineligible: VerificationExpired`. An unverified control customer → `Ineligible: VerificationRequired`. Unrestricted SKU regardless of state → `Eligible`. p95 latency ≤ 5 ms in the benchmark.

### Tests for User Story 3

- [ ] T079 [P] [US3] Create `services/backend_api/tests/Verification.Tests/Integration/EligibilityQueryMatrixTests.cs`: synthetic matrix `(verification_state × verification_market × verification_profession × product_restriction × customer_current_market) → expected EligibilityClass + EligibilityReasonCode`. **Required enumerated cases**: every value of `EligibilityReasonCode` is exercised at least once (SC-008); explicitly covers the cross-market edge case from `spec.md §Edge Cases`: `(verification_state=approved, verification_market=eg, customer_current_market=ksa, sku_restricted_in=[ksa])` → `Ineligible: MarketMismatch`; and the inverse `(verification_state=approved, verification_market=ksa, customer_current_market=ksa, sku_restricted_in=[eg])` → `Eligible` (restriction does not apply in customer's market)
- [ ] T080 [P] [US3] Create `services/backend_api/tests/Verification.Tests/Integration/EligibilityCacheInvalidationTests.cs`: every state transition (submit, approve, reject, info-request, revoke, expire, supersede, void) results in the cache row reflecting the new authoritative answer **inside the same transaction** (R1)
- [ ] T081 [P] [US3] Create `services/backend_api/tests/Verification.Tests/Integration/EligibilityBulkQueryTests.cs`: `EvaluateManyAsync` returns the same answer per SKU as N sequential `EvaluateAsync` calls, plus 1 catalog-list-page-shape fixture (50 SKUs, mix of restricted + unrestricted)
- [ ] T082 [P] [US3] Create `services/backend_api/tests/Verification.Tests/Benchmarks/EligibilityBench.cs` (BenchmarkDotNet): warm-cache p95 latency budget assertion ≤ 5 ms (asserts the lock from research.md §R1; CI baseline locked, may relax in CI environments per project convention)

### Implementation for User Story 3

- [ ] T083 [US3] Create `services/backend_api/Modules/Verification/Eligibility/CustomerVerificationEligibilityQuery.cs` implementing `ICustomerVerificationEligibilityQuery`: PK lookup on `verification_eligibility_cache` joined with `IProductRestrictionPolicy.GetForSkuAsync`; resolves `Eligible / Ineligible / Unrestricted` + `EligibilityReasonCode` per [data-model.md §4](./data-model.md)
- [ ] T084 [US3] Implement bulk variant `EvaluateManyAsync` issuing **one** cache lookup + **one** policy lookup per SKU (let spec 005's `IProductRestrictionPolicy` decide whether the bulk policy lookup is single-call); guarantee per-SKU determinism
- [ ] T085 [US3] **Verify** that every state-transition handler authored in US1, US2, and US6 (`SubmitVerificationHandler`, `ResubmitWithInfoHandler`, `RequestRenewalHandler`, `DecideApproveHandler`, `DecideRejectHandler`, `DecideRequestInfoHandler`, `DecideRevokeHandler`) invokes `EligibilityCacheInvalidator.RebuildAsync(customerId)` inside its existing transaction, with the Tx failing if the rebuild fails. Add or fix any missing call sites discovered. Add one integration test `EligibilityCacheTransactionalityTests.cs` that asserts: (a) every transition writes an updated cache row in the same Tx (not after); (b) a rebuild failure rolls back the parent transition (verification state stays unchanged)
- [ ] T086 [US3] Add a stub `IProductRestrictionPolicy` impl to `Modules/Verification/Eligibility/StubProductRestrictionPolicy.cs` registered **only in test fixtures** so spec 020 tests don't depend on spec 005; production binding waits for spec 005's PR
- [ ] T087 [US3] Register `CustomerVerificationEligibilityQuery` as `Scoped` in `VerificationModule.cs`
- [ ] T088 [US3] Add ICU keys for every `EligibilityReasonCode` in both `verification.en.icu` and `verification.ar.icu`; append AR keys to `AR_EDITORIAL_REVIEW.md`

**Checkpoint**: All three P1 stories are independently testable. The MVP launch loop (Principle 8 restricted-product gating) is functional end-to-end.

---

## Phase 6: User Story 4 — Expiry, renewal reminders, and document purge (Priority: P2)

**Goal**: Expired approvals automatically transition to `expired` and remove eligibility. Reminders fire on configured windows without duplicates. Documents purge after the per-market retention window without losing audit trail.

**Independent Test**: Insert an approved verification with `expires_at = now - 1h`; tick the clock with `FakeTimeProvider`; expiry worker transitions it to `expired`, audit event written, eligibility flips to `Ineligible: VerificationExpired`. Insert an approved verification with `expires_at = now + 14d`; reminder worker emits exactly one `VerificationReminderDue { window=14 }`; second tick emits nothing for window 14. Insert a terminal verification with `purge_after = now - 1h` and one document with a real storage blob; purge worker deletes the blob, sets `purged_at`, leaves the row.

### Tests for User Story 4

- [ ] T089 [P] [US4] Create `services/backend_api/tests/Verification.Tests/Integration/ExpiryWorkerTests.cs` driven by `FakeTimeProvider`: expiry transition + audit event + cache invalidation + `VerificationExpired` domain event published; idempotent on re-run (no double transition)
- [ ] T090 [P] [US4] Create `services/backend_api/tests/Verification.Tests/Integration/ReminderWorkerTests.cs`: each window fires exactly once per verification (UNIQUE-constraint guard); back-window outage scenario fires only the closest unfired window and writes `verification_reminders` rows with `skipped=true` for the others (R5); audit notes record the skip
- [ ] T091 [P] [US4] Create `services/backend_api/tests/Verification.Tests/Integration/DocumentPurgeWorkerTests.cs`: document body deleted via `IStorageService`, row preserved with `purged_at` + `storage_key=null`; `verification.document_purged` audit event written; subsequent `OpenHistoricalDocument` returns `410 verification.document_purged`
- [ ] T092 [P] [US4] Create `services/backend_api/tests/Verification.Tests/Integration/WorkerAdvisoryLockTests.cs`: two parallel worker instances → only one acquires `pg_try_advisory_lock`; the other no-ops cleanly (R12)

### Implementation for User Story 4

- [ ] T093 [US4] Create `services/backend_api/Modules/Verification/Workers/VerificationExpiryWorker.cs` (`BackgroundService` + `PeriodicTimer`); injected `TimeProvider`; advisory lock per R12; transitions every `approved` row with `expires_at <= now` to `expired`, calling cache invalidator + audit publisher + `VerificationExpired` domain event
- [ ] T094 [US4] Create `services/backend_api/Modules/Verification/Workers/VerificationReminderWorker.cs`: iterates approved verifications whose `expires_at` falls within an unfired reminder window; INSERT `verification_reminders { verification_id, window_days, emitted_at, skipped }` — UNIQUE constraint guarantees no duplicate; back-window logic (R5) fires closest-unfired only; publishes `VerificationReminderDue` for each fired window and `verification.reminder_emitted` audit events for fired + skipped
- [ ] T095 [US4] Create `services/backend_api/Modules/Verification/Workers/VerificationDocumentPurgeWorker.cs`: scan `verification_documents` where `purge_after <= now AND purged_at IS NULL`; call `IStorageService.DeleteAsync(storageKey)`; set `purged_at` and `storage_key = null`; emit `verification.document_purged` audit event
- [ ] T096 [US4] Update every transition handler that produces a terminal state (`DecideRejectHandler`, `DecideApproveHandler` for the `superseded` side-effect, `VerificationExpiryWorker`, future `DecideRevokeHandler` from US6, the lifecycle hook from Polish) to set `purge_after = now + market.retention_months` on every `VerificationDocument` belonging to the entered-terminal verification
- [ ] T097 [US4] Register all three workers as `IHostedService` in `VerificationModule.cs`; expose `appsettings.json` keys for both period and start-time-of-day per [research.md §R12](./research.md): `Verification:Workers:Expiry:{Period: "1.00:00:00", StartUtc: "03:00:00"}`, `Verification:Workers:Reminder:{Period: "1.00:00:00", StartUtc: "03:30:00"}`, `Verification:Workers:DocumentPurge:{Period: "1.00:00:00", StartUtc: "04:00:00"}`. Production / Staging use these defaults; `appsettings.Development.json` overrides Period to `00:01:00` and StartUtc to `00:00:00` for dev productivity (per [quickstart.md §5](./quickstart.md))

**Checkpoint**: Expiry + reminder + purge lifecycle complete. SC-005 (no-duplicate reminders), SC-009 (auto-expiry), and FR-006a (retention purge) are now testable end-to-end.

---

## Phase 7: User Story 5 — Market-aware required fields (Priority: P2)

**Goal**: KSA-required fields differ from EG-required fields; both are driven by the versioned `verification_market_schemas` table; operators can update the schema without a code deploy; reviewers see the schema that was applied at submission, not the current one.

**Independent Test**: Insert a KSA v2 schema row that adds a new `clinic_affiliation_code` required field; new KSA submissions must include it (validator rejects without); a KSA submission that landed under v1 still renders under v1's schema in the reviewer detail (FR-026).

### Tests for User Story 5

- [ ] T098 [P] [US5] Create `services/backend_api/tests/Verification.Tests/Integration/MarketSchemaVersioningTests.cs`: insert v2 schema for KSA; new KSA submission validates against v2; existing in-flight v1 submission's reviewer detail renders v1 fields and labels (SC-010)
- [ ] T099 [P] [US5] Create `services/backend_api/tests/Verification.Tests/Integration/MarketSchemaActiveConstraintTests.cs`: attempting to INSERT a second `effective_to IS NULL` row for the same market is rejected by the unique-partial-index

### Implementation for User Story 5

- [ ] T100 [US5] Create `services/backend_api/Modules/Verification/Customer/GetMarketSchema/{Request,Handler,Endpoint}.cs` exposing `GET /api/customer/verifications/schema` returning the active schema for the customer's market — used by the customer app to render the form dynamically
- [ ] T101 [US5] Update `SubmitVerificationValidator` from US1 (T051) to read `required_fields` jsonb from the active schema and validate dynamically (instead of hardcoded fields)
- [ ] T102 [US5] Update `GetVerificationDetail` (T071) to resolve the verification's `schema_version` and return the schema-as-submitted in the response payload so reviewers see the labels and types the customer saw
- [ ] T103 [US5] Document the schema-update procedure in `services/backend_api/Modules/Verification/Seeding/SCHEMA_UPDATE_RUNBOOK.md`: INSERT new version + UPDATE old `effective_to=now()` in one Tx; idempotency rules; testing checklist. Also re-emit `services/backend_api/openapi.verification.json` to include the new `GET /api/customer/verifications/schema` endpoint (T100)

**Checkpoint**: Per-market schema versioning works without a code deploy; reviewer detail honors schema-as-submitted (FR-026).

---

## Phase 8: User Story 6 — Reviewer revokes an active approval (Priority: P3)

**Goal**: A senior reviewer holding `verification.revoke` revokes an active `approved` verification with a required reason; eligibility flips immediately; no cool-down for resubmission; full audit.

**Independent Test**: Reviewer with `verification.revoke` opens an active approval, calls revoke with a reason; verification transitions to `revoked`, eligibility flips to `Ineligible: VerificationRevoked`, customer is notified (event published). Same call by a reviewer without `verification.revoke` returns 403 and the attempt is audited.

### Tests for User Story 6

- [ ] T104 [P] [US6] Create `services/backend_api/tests/Verification.Tests/Contract/AdminRevokeContractTests.cs` covering [contracts §3.6](./contracts/verification-contract.md): `verification.revoke_permission_required` (403), reason required, only `approved` is revocable (other states reject with `invalid_state_for_action`), `verification.already_decided` on optimistic-concurrency loss
- [ ] T105 [P] [US6] Create `services/backend_api/tests/Verification.Tests/Integration/RevokeNoCooldownTests.cs`: a customer whose verification was revoked may submit a new verification immediately (FR-009 — no cool-down after revoke)

### Implementation for User Story 6

- [ ] T106 [US6] Create slice `services/backend_api/Modules/Verification/Admin/DecideRevoke/{Request,Validator,Handler,Endpoint}.cs` per [contracts §3.6](./contracts/verification-contract.md): same `{ reason: { en?, ar? } }` body shape as T072 (FR-033 — at least one locale required); requires `verification.revoke`; xmin guard; transitions `approved → revoked`; rebuilds eligibility cache; sets `purge_after` on documents (per T096); publishes `VerificationRevoked`
- [ ] T107 [US6] Update `SubmitVerificationHandler` (T052) cool-down check to skip the cool-down branch when the customer's most recent terminal state is `revoked` (FR-009)
- [ ] T108 [US6] Add ICU keys for revoke reason rendering in both locales; append AR keys to `AR_EDITORIAL_REVIEW.md`
- [ ] T109 [US6] Re-emit `openapi.verification.json` to include the revoke endpoint

**Checkpoint**: All six user stories are independently functional. The full spec 020 surface is buildable, testable, and reviewable.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story integrations (account lifecycle hook), final auditing, AR editorial pass, contract artifact, DoD checklist.

### Account-lifecycle hook (cross-cutting; consumed by US1/US2/US3)

- [ ] T110 Create `services/backend_api/Modules/Verification/Hooks/AccountLifecycleHandler.cs` implementing `ICustomerAccountLifecycleSubscriber` per [research.md §R6 + §R7](./research.md): on locked/deleted → void all non-terminal verifications + active approval → reason `account_inactive` / `account_deleted`; on market-changed → void non-terminal + supersede active → reason `customer_market_changed`; on deleted → expedite document purge by setting `purge_after = now`
- [ ] T111 Register `AccountLifecycleHandler` as the `ICustomerAccountLifecycleSubscriber` binding in `VerificationModule.cs`; coordinate with spec 004 to confirm the publisher fires the events (in spec 004's PR, not here)
- [ ] T112 [P] Create `services/backend_api/tests/Verification.Tests/Integration/AccountLifecycleHandlerTests.cs`: covers the three event paths; verifies eligibility flips, document purge expedition, and no orphaned non-terminal rows after a deletion

### Dev seeder + manual smoke

- [ ] T113 [P] Create `services/backend_api/Modules/Verification/Seeding/VerificationDevDataSeeder.cs` (Dev-gated via `SeedGuard` per spec 003) seeding synthetic submissions across every state (`submitted`, `in-review`, `info-requested`, `approved` near-expiry, `rejected` in cool-down, `expired`, `revoked`, `superseded`, `void`) + sample documents — supports demo + manual QA; idempotent
- [ ] T114 [P] Update `services/backend_api/seed-data/README.md` (per spec 003 convention) with the `verification-v1` synthetic dataset description

### AR editorial sweep + OpenAPI

- [ ] T115 Run an AR editorial pass over every key in `Modules/Verification/Messages/verification.ar.icu`; clear the `AR_EDITORIAL_REVIEW.md` queue; commit reviewer sign-off (Principle 4, SC-006)
- [ ] T116 Regenerate the final `services/backend_api/openapi.verification.json`; CI Guardrail #2 must show no unexpected diff

### Audit + DoD

- [ ] T117 Create `scripts/audit-spot-check-verification.sh` (matches spec 004's pattern): replays a synthetic verification's lifecycle and asserts the expected `audit_log_entries` rows exist for every transition + every PII read + every reminder + every purge
- [ ] T118 Run the full DoD walkthrough per `docs/dod.md`: every FR traced to a passing test (matrix in `services/backend_api/tests/Verification.Tests/coverage-matrix.md`); every SC measurable; constitution + ADR fingerprint computed via `scripts/compute-fingerprint.sh`; impeccable scan N/A (backend-only spec per `docs/design-agent-skills.md`)
- [ ] T119 [P] Performance verification: run the `EligibilityBench` benchmark on the staging-equivalent dev box; record p95 + p99; commit the result to `services/backend_api/tests/Verification.Tests/Benchmarks/baselines.md`
- [ ] T120 Run `quickstart.md` end-to-end against a fresh local Postgres + a fresh module checkout to verify the implementer walkthrough still works after all phases land

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; can start immediately.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story phase**.
- **User Stories (Phases 3–8)**: each depends on Foundational; within Phase 3–5 the three P1 stories couple to form the MVP loop but each is independently testable in isolation against a stub.
  - **US1 (Phase 3)** depends only on Foundational. Can start in parallel with US2 / US3.
  - **US2 (Phase 4)** depends only on Foundational. The US2 integration test for the full round-trip needs a US1 submission, but US2's slice work is independent and the test can use a hand-INSERTed fixture.
  - **US3 (Phase 5)** depends on Foundational + the eligibility-cache rebuilds wired into US1/US2 transition handlers (T085). T085 explicitly updates US1 + US2 handlers, so US3 finishes after US1 + US2's first-pass slices land.
  - **US4 (Phase 6)** depends on Foundational + at least one approved verification existing for the workers' fixtures — practically waits for US2.
  - **US5 (Phase 7)** depends on Foundational + US1's submission validator (T051) — modifies it.
  - **US6 (Phase 8)** depends on Foundational + US2's decision endpoints — adds a sibling.
- **Polish (Phase 9)**: depends on every prior phase being substantively complete.

### Within Each User Story

- Tests written FIRST (T044–T049 for US1, T063–T069 for US2, T079–T082 for US3, T089–T092 for US4, T098–T099 for US5, T104–T105 for US6); they MUST FAIL before implementation begins.
- Slice files within a story marked `[P]` have no shared file → can land in parallel.
- ICU + AR additions (T060–T061, T077, T088, T108, T115) touch the same files → serial within a phase.

### Parallel Opportunities

- **Setup**: T004, T005, T006 fully parallel.
- **Foundational primitives**: T007–T013 fully parallel.
- **Foundational unit tests**: T014–T016 fully parallel.
- **Foundational entities**: T018–T022 fully parallel after T017 (Verification entity referenced by some configurations).
- **Foundational EF configurations**: T024–T029 fully parallel.
- **Foundational shared interfaces**: T032–T036, T038, T039 fully parallel.
- **US1 contract tests**: T044–T049 fully parallel (different files).
- **US1 slices**: T054–T057 fully parallel.
- **US2 contract tests**: T063–T068 fully parallel.
- **US2 sibling decision slices**: T073, T074 parallel after T072 lands the shared decision-handler infrastructure.
- **US3 tests**: T079–T082 fully parallel.
- **US4 tests**: T089–T092 fully parallel.
- **Polish**: T112, T113, T114, T119 mostly parallel.

---

## Parallel Example: User Story 1 contract tests

```bash
# After Foundational checkpoint — fire all six US1 contract tests in parallel:
Task: "T044 SubmitVerificationContractTests"
Task: "T045 AttachDocumentContractTests"
Task: "T046 ResubmitWithInfoContractTests"
Task: "T047 GetMyVerificationContractTests"
Task: "T048 RequestRenewalContractTests"
Task: "T049 CustomerSubmissionLocaleTests"
```

## Parallel Example: Foundational primitives

```bash
# All seven primitive types are independent files:
Task: "T007 VerificationState enum"
Task: "T008 VerificationActorKind enum"
Task: "T009 VerificationReasonCode enum + ICU mapper"
Task: "T010 EligibilityReasonCode + EligibilityResult"
Task: "T011 VerificationStateMachine"
Task: "T012 BusinessDayCalculator"
Task: "T013 VerificationMarketPolicy"
```

---

## Implementation Strategy

### MVP First (Stories US1 + US2 + US3 — all P1)

The independent test for US1 in `spec.md` deliberately spans US2 (admin approval) and US3 (eligibility flip). The launch MVP is therefore the three P1 stories together, not US1 alone. Recommended ordering:

1. **Phase 1 — Setup** (T001–T006).
2. **Phase 2 — Foundational** (T007–T043). Hard gate; nothing else can land.
3. **Phase 3 — US1 customer surface** (T044–T062).
4. **Phase 4 — US2 admin queue + decisions** (T063–T078).
5. **Phase 5 — US3 eligibility query + cache wiring** (T079–T088).
6. **STOP and VALIDATE the launch loop**: a customer submits → reviewer approves → restricted SKU becomes purchasable for that customer; control unverified customer remains restricted; AR + EN both validated; full audit replayable. This is the spec.md US1 independent test.
7. Phase 6 (US4 expiry + reminders) → Phase 7 (US5 schema versioning) → Phase 8 (US6 revoke) in priority order.
8. **Phase 9 — Polish** including the account-lifecycle hook (T110–T112) which is cross-cutting and only safely lands once US1/US2/US3 are stable.

### Incremental Delivery

- **MVP ship candidate**: end of Phase 5. Spec 020 launch loop verified; spec 005/009/010 can begin consuming the eligibility query the day Phase 5 merges.
- **Operations completeness**: end of Phase 6 (auto-expiry + reminders + retention purge).
- **Compliance completeness**: end of Phase 7 (per-market schema versioning) + Phase 8 (revoke).
- **DoD-ready**: end of Phase 9.

### Parallel Team Strategy

With multiple Lane A engineers (or agents) once Foundational lands:
- Engineer A: US1 (Phase 3).
- Engineer B: US2 (Phase 4).
- Engineer C: US3 (Phase 5) — synchronizes with A + B on T085 (cache invalidator wiring).
- Engineer D: starts US4 (Phase 6) workers in parallel with the P1 trio; waits on US2's approval handler to add the worker fixtures.

---

## Notes

- Every state-transitioning POST endpoint must require `Idempotency-Key` per spec 003 platform middleware. Idempotency replay returns 200 with the original response; concurrency loss returns `409 verification.already_decided`.
- Every new `AddDbContext` registration must suppress `RelationalEventId.ManyServiceProvidersCreatedWarning` (project-memory rule).
- Cross-module hook contracts live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule).
- `TimeProvider` is the only time source; never `DateTime.UtcNow` in this module. Tests inject `FakeTimeProvider`.
- `IPiiAccessRecorder` is the chokepoint for FR-015a-e PII-read auditing; every read of a `VerificationDocument` body or a terminal-state `LicenseNumber` MUST go through it.
- Spec 005 (`IProductRestrictionPolicy`), spec 019 (`verification.read_pii` role grant), spec 023 (`verification.read_summary` role grant), and spec 025 (domain-event subscribers) integrate against the contracts merged here; their changes land in their own spec PRs, not in 020.
- **UX-timing budgets in spec.md SC-001 (3-min submission) and SC-002 (90-s decision)** are validated by Phase 1C UI specs against this backend's latency budgets (eligibility p95 ≤ 5 ms; reviewer queue p95 ≤ 600 ms; reviewer detail p95 ≤ 1500 ms; submission write path p95 ≤ 800 ms). Spec 020 owns the latency budgets; the user-facing time-to-complete metrics are owned by the consuming UI surface.
