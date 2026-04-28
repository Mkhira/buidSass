---
description: "Task list for Spec 023 — Support Tickets (Phase 1D · Milestone 7)"
---

# Tasks: Support Tickets

**Input**: Design documents from `/specs/phase-1D/023-support-tickets/`
**Prerequisites**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/support-tickets-contract.md](./contracts/support-tickets-contract.md), [quickstart.md](./quickstart.md)

**Tests**: Included. Integration + contract tests are required per the spec's 11 SCs and the project DoD (Testcontainers Postgres; no SQLite shortcut). Unit tests included for primitives, state machine, market-code resolver, SLA snapshot freezing, and reopen window math.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story (US1 / US2 / US3 / US4 / US5 / US6 / US7). Foundational primitives, persistence, migrations, cross-module shared declarations, and reference seeder land in Phase 2 and unblock all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to a user story from spec.md (US1–US7); Setup, Foundational, and Polish phases have no story label
- Include exact file paths in descriptions

## Path Conventions (per [plan.md §Project Structure](./plan.md))

- Backend module: `services/backend_api/Modules/Support/`
- Cross-module shared declarations: `services/backend_api/Modules/Shared/`
- Tests: `services/backend_api/tests/Support.Tests/` (Unit/, Integration/, Contract/)
- Fakes (cross-module test doubles): `services/backend_api/Modules/Shared/Testing/`
- Reference seeder mode: idempotent across Dev + Staging + Prod (via `SeedGuard`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization for the Support module.

- [ ] T001 Create `services/backend_api/Modules/Support/` directory tree per plan.md §Project Structure (Customer/, Agent/, Lead/, PolicyAdmin/, SuperAdmin/, Subscribers/, Workers/, Filtering/, Authorization/, Entities/, Persistence/, Messages/, Seeding/, Primitives/) and add a placeholder `SupportModule.cs` registering an empty `AddSupportModule` extension method
- [ ] T002 [P] Create `services/backend_api/tests/Support.Tests/` test project with xUnit + FluentAssertions + Microsoft.Extensions.TimeProvider.Testing references and Testcontainers.PostgreSql wiring (mirror `Reviews.Tests/Support.Tests.csproj`); add `Unit/`, `Integration/`, `Contract/` folders
- [ ] T003 [P] Add `support` schema to the connection-string seeded migration generator (no schema content yet — empty migration just to confirm EF tooling works); verify `dotnet ef migrations add InitSupportSchema --project services/backend_api/Modules/Support` produces a no-op migration
- [ ] T004 [P] Wire `AddSupportModule(builder.Configuration)` into `services/backend_api/Program.cs` after the existing `AddReviewsModule(...)` call (suppression of `ManyServiceProvidersCreatedWarning` is enforced inside `SupportModule.cs` per project-memory rule)
- [ ] T005 Add a Support-test `Support.Tests.csproj` reference into `tests.sln` and verify `dotnet test --filter Category=Smoke` from repo root runs zero tests successfully (sanity of harness wiring)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core primitives, persistence, migrations, cross-module shared declarations, and reference seeder. **No user story work can begin until this phase is complete.**

### Primitives (Phase A)

- [ ] T006 [P] Create `Modules/Support/Primitives/TicketState.cs` enum: `Open`, `InProgress`, `WaitingCustomer`, `Resolved`, `Closed`
- [ ] T007 [P] Create `Modules/Support/Primitives/TicketCategory.cs` enum with the 10 fixed values from FR-007 + ICU-key mapper (en + ar)
- [ ] T008 [P] Create `Modules/Support/Primitives/TicketPriority.cs` enum: `Low`, `Normal`, `High`, `Urgent`
- [ ] T009 [P] Create `Modules/Support/Primitives/TicketActorKind.cs` enum: `Customer`, `Agent`, `Lead`, `SuperAdmin`, `FinanceViewer`, `ReviewModerator`, `B2BAccountManager`, `System`
- [ ] T010 [P] Create `Modules/Support/Primitives/TicketReasonCode.cs` enum + ICU-key mapper for all 52 owned reason codes from `contracts/support-tickets-contract.md §8`
- [ ] T011 [P] Create `Modules/Support/Primitives/TicketTriggerKind.cs` enum: 13 values per data-model.md §3 (`customer_submission`, `agent_claim`, `agent_assignment`, `customer_reply`, `agent_reply`, `agent_resolve`, `customer_reopen`, `auto_close_resolution_window`, `return_outcome`, `author_account_locked`, `lead_force_close`, `lead_reassign`, `agent_offboarded`)
- [ ] T012 [P] Create `Modules/Support/Primitives/TicketLinkedEntityKind.cs` enum: `Order`, `OrderLine`, `ReturnRequest`, `Quote`, `Review`, `Verification`
- [ ] T013 [P] Create `Modules/Support/Primitives/TicketMessageKind.cs` enum: `CustomerReply`, `AgentReply`, `InternalNote`, `SystemEvent`
- [ ] T014 [P] Create `Modules/Support/Primitives/TicketStateMachine.cs` with compile-time transition guards covering all valid transitions including the reopen edge `Resolved → InProgress`; reject all other transitions with `InvalidTransitionException`
- [ ] T015 [P] Create `Modules/Support/Primitives/SupportMarketPolicy.cs` value object that loads from `support_market_schemas` row (auto_assignment_enabled, reopen_window_days, max_reopen_count, auto_close_after_resolved_days, attachment caps, allowed MIME types)
- [ ] T016 [P] Create `Modules/Support/Primitives/SlaPolicySnapshot.cs` value object with `FirstResponseTargetMinutes`, `ResolutionTargetMinutes`, `Priority`, `MarketCode` — pure data carrier copied onto each ticket at creation
- [ ] T017 [P] Create `Modules/Support/Primitives/MarketCodeResolver.cs` implementing FR-006a — linked-entity-first resolution with `MarketCodeUnresolvableException` on linked-entity-unavailable
- [ ] T018 [P] Create `Modules/Support/Primitives/TicketRowVersion.cs` typed wrapper for the EF Core xmin row_version with helpers for the optimistic-concurrency check
- [ ] T019 [P] Create `Modules/Support/Authorization/SupportPermissions.cs` static class with constants `support.agent`, `support.lead` (used by `[RequirePermission(...)]` attributes from spec 004's RBAC)

### Persistence — entities (Phase B)

- [ ] T020 [P] Create `Modules/Support/Entities/SupportTicket.cs` with all columns from data-model.md §4 (incl. SLA snapshot, breach acknowledgments, reopen_count, vendor_id, company_id, row_version)
- [ ] T021 [P] Create `Modules/Support/Entities/TicketMessage.cs` with `kind`, `actor_id`, `actor_role`, `body` (nullable for redaction), `lead_intervention`, `redacted_*` columns
- [ ] T022 [P] Create `Modules/Support/Entities/TicketAttachment.cs` with `state ∈ {active, redacted}`, `storage_object_id` nullable for redaction, `mime_type`, `size_bytes`, `original_filename`, `redacted_*` columns
- [ ] T023 [P] Create `Modules/Support/Entities/TicketLink.cs` with `kind`, `linked_entity_id`, `created_via`, `idempotency_key`
- [ ] T024 [P] Create `Modules/Support/Entities/TicketAssignment.cs` with `agent_id`, `assignment_kind`, `assigned_by_actor_id`, `justification_note`, `superseded_at_utc`, `superseded_reason`
- [ ] T025 [P] Create `Modules/Support/Entities/TicketSlaBreachEvent.cs` with composite PK `(ticket_id, breach_kind, detected_at_utc)` + `superseded_by_event_id` for reopen handling
- [ ] T026 [P] Create `Modules/Support/Entities/SlaPolicy.cs` with PK `(market_code, priority)` and target columns
- [ ] T027 [P] Create `Modules/Support/Entities/SupportMarketSchema.cs` with PK `market_code` and all per-market knob columns
- [ ] T028 [P] Create `Modules/Support/Entities/SupportAgentAvailability.cs` with PK `(agent_id, market_code)` and the V1 minimal `is_on_call` boolean (no shift windows per FR-019a / Clarification Q1)

### Persistence — DbContext, configurations, migration (Phase B)

- [ ] T029 Create `Modules/Support/Persistence/SupportDbContext.cs` deriving from `DbContext`; register `DbSet<T>` for all 9 entities; configure `support` schema; suppress `ManyServiceProvidersCreatedWarning` per project-memory rule
- [ ] T030 [P] Create `Modules/Support/Persistence/Configurations/SupportTicketConfiguration.cs` (and 8 sibling configurations — one per entity) implementing `IEntityTypeConfiguration<T>` with all FK / index / column-type definitions per data-model.md §4
- [ ] T031 Create the EF Core migration `20260428_001_AddSupportSchema` via `dotnet ef migrations add AddSupportSchema --project services/backend_api/Modules/Support`; review the generated SQL for the 9 tables + indexes
- [ ] T032 Edit migration `20260428_001_AddSupportSchema.cs` to add Postgres `BEFORE UPDATE OR DELETE` triggers on the 6 append-only tables (`ticket_messages`, `ticket_attachments`, `ticket_links`, `ticket_assignments`, `ticket_sla_breach_events`); add the controlled redaction-exception WHEN-clause for `ticket_messages` + `ticket_attachments` per data-model.md §4
- [ ] T033 Edit migration `20260428_001_AddSupportSchema.cs` to add the partial unique index `(ticket_id) WHERE superseded_at_utc IS NULL` on `ticket_assignments` (one active assignment per ticket)
- [ ] T034 Run `dotnet ef database update --project services/backend_api/Modules/Support` against a Testcontainers Postgres and assert all 9 tables + triggers + indexes are created via `\dt support.*` + `\d support.ticket_messages`

### Cross-module shared declarations (Phase D)

- [ ] T035 [P] Create `Modules/Shared/IOrderLinkedReadContract.cs` with `ReadAsync(linkedEntityId, actorCustomerId, ct) → LinkedEntityReadResult` per research.md §R-01; register a `Fake*` double in `Modules/Shared/Testing/FakeOrderLinkedReadContract.cs`
- [ ] T036 [P] Create `Modules/Shared/IReturnLinkedReadContract.cs` (same shape) + `Modules/Shared/Testing/FakeReturnLinkedReadContract.cs`
- [ ] T037 [P] Create `Modules/Shared/IReturnRequestCreationContract.cs` with `CreateAsync(customerId, orderLineId, narrative, attachmentIds, originatingTicketId, idempotencyKey, ct) → ReturnRequestCreationResult` (idempotent per research.md §R-02) + `Modules/Shared/Testing/FakeReturnRequestCreationContract.cs` that captures invocations for assertion
- [ ] T038 [P] Create `Modules/Shared/IReturnOutcomeSubscriber.cs` with `OnReturnCompletedAsync(returnRequestId, ticketId?, ct)` and `OnReturnRejectedAsync(...)` + matching publisher; register binding so spec 013 publishes via the in-process MediatR bus
- [ ] T039 [P] Create `Modules/Shared/IQuoteLinkedReadContract.cs` + fake double
- [ ] T040 [P] Create `Modules/Shared/IReviewLinkedReadContract.cs` + fake double
- [ ] T041 [P] Create `Modules/Shared/IVerificationLinkedReadContract.cs` + fake double
- [ ] T042 [P] Create `Modules/Shared/ICompanyAccountQuery.cs` with `ResolveCompanyIdAsync(customerId, ct) → Guid?` for B2B scoping (FR-016a) + fake double
- [ ] T043 [P] Reuse `IReviewDisplayHandleQuery` from spec 022; verify it is referenced in `Modules/Shared/` and create a fake double in `Modules/Shared/Testing/FakeReviewDisplayHandleQuery.cs` for tests where spec 022 isn't on `main`
- [ ] T044 [P] Create `Modules/Shared/SupportTicketDomainEvents.cs` with all 16 `INotification` records per data-model.md §6 (`TicketOpened`, `TicketAssigned`, `TicketReassigned`, `TicketCustomerReplyReceived`, `TicketAgentReplySent`, `TicketStateChanged`, `TicketResolved`, `TicketClosed`, `TicketReopened`, `TicketSlaBreachedFirstResponse`, `TicketSlaBreachedResolution`, `TicketConvertedToReturn`, `TicketReturnOutcomeReceived`, `TicketAttachmentRedacted`, `TicketMessageRedacted`, `TicketAgentAvailabilityChanged`)

### Reference seeder + module wiring (Phase C + Module wiring)

- [ ] T045 Create `Modules/Support/Seeding/SupportReferenceDataSeeder.cs` populating 8 `sla_policies` rows (4 priorities × 2 markets) with FR-021 defaults + 2 `support_market_schemas` rows for KSA + EG; idempotent across Dev + Staging + Prod via `SeedGuard` (project pattern from spec 020)
- [ ] T046 Wire `SupportReferenceDataSeeder` into `services/backend_api/Modules/Bootstrap/ReferenceDataSeederHost.cs` registry alongside the existing `ReviewsReferenceDataSeeder`
- [ ] T047 Update `Modules/Support/SupportModule.cs` to register: `AddDbContext<SupportDbContext>` with warning suppression; MediatR handler scan; `AddValidatorsFromAssembly`; subscribers (`IReturnOutcomeSubscriber`, `ICustomerAccountLifecycleSubscriber`); workers (`SlaBreachWatchWorker`, `AutoCloseResolutionWindowWorker`, `OrphanedAssignmentReclaimWorker`); `IRequirePermission` policies for `support.agent` + `support.lead`

### Foundational tests

- [ ] T048 [P] Create `tests/Support.Tests/Unit/TicketStateMachineTests.cs` — property tests asserting: every legal transition is exactly one entry in the FSM; no terminal→non-terminal except via reopen; no `Closed → *`; reopen requires source state `Resolved`
- [ ] T049 [P] Create `tests/Support.Tests/Unit/MarketCodeResolverTests.cs` — covers (a) linked-entity available → linked entity's market; (b) linked-entity unavailable → throws `MarketCodeUnresolvableException`; (c) no linked entity → customer-of-record fallback; uses fakes from T035–T041
- [ ] T050 [P] Create `tests/Support.Tests/Unit/TicketReasonCodeMapperTests.cs` — asserts every owned reason code from `TicketReasonCode` enum has both `en` and `ar` ICU keys in `support.en.icu` + `support.ar.icu` (drives the editorial sweep)
- [ ] T051 [P] Create `tests/Support.Tests/Integration/SupportSchemaSmokeTests.cs` — Testcontainers Postgres; assert all 9 tables exist after migration; assert append-only triggers reject naive UPDATE/DELETE on `ticket_messages` and `ticket_attachments` outside the redaction-exception WHEN clause
- [ ] T052 [P] Create `tests/Support.Tests/Integration/SupportReferenceDataSeederTests.cs` — assert 8 `sla_policies` rows + 2 `support_market_schemas` rows present after seeder run; assert second invocation is a no-op (idempotency); assert `--mode=dry-run` writes nothing

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 — Customer opens ticket and exchanges replies (Priority: P1) 🎯 MVP

**Goal**: Verified-customer ticket creation with optional attachments + linked entity, then a customer reply loop covering open → in_progress ↔ waiting_customer transitions.

**Independent Test**: Sign in as a customer with a delivered order line; open a ticket via `POST /v1/customer/support-tickets` with category `order_issue` + 2 attachments; verify the row exists in `open`; sign in as an agent and claim; reply both ways; verify state transitions and audit rows.

### Tests for User Story 1

- [ ] T053 [P] [US1] Create `tests/Support.Tests/Contract/OpenTicketContractTests.cs` asserting all spec.md US1 Acceptance Scenarios 1–6 against the live handler (success path; unauthenticated; linked-entity-not-owned; linked-entity-kind-inconsistent; rate-limit; idempotency replay)
- [ ] T054 [P] [US1] Create `tests/Support.Tests/Integration/MarketCodeResolutionTests.cs` asserting FR-006a: linked-entity present + Available → linked market; linked-entity Unavailable → `400 support.ticket.market_code_unresolvable`; no linked-entity → customer-of-record fallback
- [ ] T055 [P] [US1] Create `tests/Support.Tests/Integration/TicketAttachmentTests.cs` asserting FR-012: per-attachment max 10 MB rejected; per-ticket cumulative 50 MB rejected; disallowed MIME rejected; allowed types accepted
- [ ] T056 [P] [US1] Create `tests/Support.Tests/Integration/TicketReplyLoopTests.cs` asserting US1 reply loop: customer reply on `waiting_customer` → `in_progress`; reply on `open` → `in_progress`; reply on `closed` → `400 support.ticket.closed_terminal`

### Implementation for User Story 1

- [ ] T057 [P] [US1] Create `Modules/Support/Customer/OpenTicket/OpenTicketCommand.cs` + `OpenTicketResult.cs` records per quickstart.md §1.4
- [ ] T058 [P] [US1] Create `Modules/Support/Customer/OpenTicket/OpenTicketValidator.cs` (FluentValidation) covering FR-006 + FR-007 (category-kind consistency) + FR-006 priority restriction (`low|normal` for customer-side)
- [ ] T059 [US1] Create `Modules/Support/Customer/OpenTicket/OpenTicketHandler.cs` implementing the full handler from quickstart.md §1.4: linked-entity ownership + market resolution + SLA snapshot + persist + audit + auto-assign-if-enabled + emit `TicketOpened` event (depends on T020–T034, T035–T044)
- [ ] T060 [P] [US1] Create `Modules/Support/Customer/ListMyTickets/ListMyTicketsQuery.cs` + handler with paging + filters per `contracts/support-tickets-contract.md §1`
- [ ] T061 [P] [US1] Create `Modules/Support/Customer/GetMyTicket/GetMyTicketQuery.cs` + handler returning the ticket with `internal_note` rows stripped (FR-014); redacted messages return tombstone payload
- [ ] T062 [P] [US1] Create `Modules/Support/Customer/UploadAttachment/UploadAttachmentCommand.cs` + handler invoking spec 015 storage abstraction signed-URL flow (FR-012)
- [ ] T063 [US1] Create `Modules/Support/Customer/ReplyAsCustomer/ReplyAsCustomerCommand.cs` + handler: validates state ∈ {open, in_progress, waiting_customer}; transitions `waiting_customer → in_progress` per FR-009; emits `TicketCustomerReplyReceived` event
- [ ] T064 [US1] Wire HTTP endpoints in `Modules/Support/Customer/CustomerEndpoints.cs`: `POST /v1/customer/support-tickets`, `GET /v1/customer/support-tickets`, `GET /v1/customer/support-tickets/{id}`, `POST /v1/customer/support-tickets/attachments/upload`, `POST /v1/customer/support-tickets/{id}/replies` (Idempotency-Key middleware applied)
- [ ] T065 [US1] Add ICU keys for `support.ticket.opened`, `support.ticket.linked_entity_not_owned`, `support.ticket.market_code_unresolvable`, all FR-012 attachment errors, `support.ticket.closed_terminal`, `support.ticket.creation_rate_exceeded` to both `Modules/Support/Messages/support.en.icu` + `support.ar.icu`

**Checkpoint**: US1 complete — a customer can open a ticket end-to-end and exchange replies. SLA snapshot is frozen on the ticket row.

---

## Phase 4: User Story 2 — Agent works queue and claims ticket (Priority: P1) 🎯 MVP

**Goal**: Agent queue with filters + paging + claim race resolution + ticket-detail with full thread.

**Independent Test**: Seed 30 tickets across 5 categories + 4 priorities + 2 markets; sign in as `support.agent`; apply each filter combination; verify result counts; claim a ticket and verify only one of two concurrent claims succeeds.

### Tests for User Story 2

- [ ] T066 [P] [US2] Create `tests/Support.Tests/Contract/AgentQueueContractTests.cs` asserting all US2 Acceptance Scenarios 1–5 (filters; concurrent-claim conflict; permission-denied; default sort; lead reassignment audit)
- [ ] T067 [P] [US2] Create `tests/Support.Tests/Integration/ClaimRaceConcurrencyTests.cs` asserting SC-007: 100 concurrent claims, exactly 1 winner; loser receives `409 support.ticket.assignment_conflict`
- [ ] T068 [P] [US2] Create `tests/Support.Tests/Integration/AgentQueuePerfTests.cs` asserting SC-011: 50-row page in < 500 ms p95 against 10 000 seeded tickets

### Implementation for User Story 2

- [ ] T069 [P] [US2] Create `Modules/Support/Agent/ListAgentQueue/ListAgentQueueQuery.cs` + handler with all filters from `contracts/support-tickets-contract.md §2` (market, category[], priority[], state[], assigned_agent_id, sla_breach_status, linked_entity_kind, created_at_range)
- [ ] T070 [US2] Create `Modules/Support/Agent/ClaimTicket/ClaimTicketCommand.cs` + handler with optimistic-concurrency guard via xmin row_version per FR-017; writes `TicketAssignment` row + transitions `open → in_progress` + audits
- [ ] T071 [P] [US2] Create `Modules/Support/Agent/GetTicketAdminDetail/GetTicketAdminDetailQuery.cs` + handler returning full thread (incl. internal notes for support roles), full assignment history, full audit history, linked-entity preview via the appropriate per-kind read contract (`IOrderLinkedReadContract` / `IReturnLinkedReadContract` / `IQuoteLinkedReadContract` / `IReviewLinkedReadContract` / `IVerificationLinkedReadContract`)
- [ ] T072 [US2] Wire HTTP endpoints in `Modules/Support/Agent/AgentEndpoints.cs`: `GET /v1/admin/support-tickets/queue`, `GET /v1/admin/support-tickets/{id}`, `POST /v1/admin/support-tickets/{id}/claim` (with `If-Match: <row_version>` for the claim)
- [ ] T073 [US2] Apply `[RequirePermission(SupportPermissions.SupportAgent)]` to all agent endpoints; finance-viewer + reviews-moderator + b2b-account-manager scoped reads gated per role table in `contracts/support-tickets-contract.md §2`
- [ ] T074 [US2] Add ICU keys for `support.ticket.queue_forbidden`, `support.ticket.assignment_conflict`, `support.ticket.version_conflict` to both EN + AR ICU files

**Checkpoint**: US2 complete — agents can work the queue, claim safely under race, read full ticket detail with role-gated thread.

---

## Phase 5: User Story 3 — Customer converts ticket to return-request (Priority: P1) 🎯 MVP

**Goal**: Idempotent ticket-to-return conversion with bidirectional linkage; reverse-event subscriber auto-resolves originating ticket.

**Independent Test**: Open a ticket category=`return_refund_request` with linked `order_line`; trigger conversion; verify spec 013 contract receives the call (via fake); verify bidirectional `TicketLink`; emit `return.completed` from the fake and verify the ticket auto-transitions to `resolved`.

### Tests for User Story 3

- [ ] T075 [P] [US3] Create `tests/Support.Tests/Contract/ConvertToReturnContractTests.cs` asserting US3 Acceptance Scenarios 1–5 (success; category-not-eligible; idempotent retry; outcome-event reception; non-owner forbidden)
- [ ] T076 [P] [US3] Create `tests/Support.Tests/Integration/ConversionIdempotencyTests.cs` asserting SC-010: 100-iteration retry with the same `Idempotency-Key` produces exactly 1 return-request + 1 `TicketLink` row
- [ ] T077 [P] [US3] Create `tests/Support.Tests/Integration/ReturnOutcomeSubscriberTests.cs` asserting `return.completed` event arrival auto-transitions the originating ticket to `resolved` with `triggered_by=return_outcome` + appends a system-event message summarising the outcome

### Implementation for User Story 3

- [ ] T078 [P] [US3] Create `Modules/Support/Customer/ConvertToReturnRequest/ConvertToReturnRequestCommand.cs` + handler invoking `IReturnRequestCreationContract.CreateAsync(...)` with the inbound `Idempotency-Key`; persists `TicketLink` (`kind=return_request`); transitions ticket to `waiting_customer`; emits `TicketConvertedToReturn`
- [ ] T079 [US3] Wire HTTP endpoint `POST /v1/customer/support-tickets/{id}/convert-to-return` with idempotency middleware applied
- [ ] T080 [P] [US3] Create `Modules/Support/Subscribers/ReturnOutcomeHandler.cs` implementing `IReturnOutcomeSubscriber`; on `return.completed` or `return.rejected`, locate the originating ticket via `TicketLink` back-traversal; transition to `resolved`; append `system_event` message; emit `TicketReturnOutcomeReceived`
- [ ] T081 [US3] Bind `ReturnOutcomeHandler` in `SupportModule.cs` so spec 013's published events fan out to it via the in-process MediatR bus
- [ ] T082 [US3] Add ICU keys for `support.ticket.conversion_category_not_eligible`, `support.ticket.conversion_already_converted`, `support.ticket.conversion_forbidden`, `support.ticket.return_creation_contract_failed` to EN + AR ICU files

**Checkpoint**: US3 complete — customer-initiated conversion to return-request works idempotently and the reverse outcome event closes the loop.

---

## Phase 6: User Story 4 — SLA breach worker triggers notification + lead reassignment (Priority: P2)

**Goal**: Time-driven SLA breach detection (first-response + resolution) with idempotency; lead reassignment with required justification; SLA override path.

**Independent Test**: Seed an `open` ticket with backdated `first_response_due_utc`; advance the worker tick via `FakeTimeProvider`; verify the breach event row + the emitted event + the acknowledgment stamp; re-run the worker and verify no duplicate.

### Tests for User Story 4

- [ ] T083 [P] [US4] Create `tests/Support.Tests/Contract/SlaBreachContractTests.cs` asserting US4 Acceptance Scenarios 1–5 (breach detection; idempotency on re-tick; reassign-justification-required; resolution-due breach distinct from first-response; override recomputes targets)
- [ ] T084 [P] [US4] Create `tests/Support.Tests/Integration/SlaBreachWorkerLatencyTests.cs` asserting SC-005: breach event created within 60 s of deadline (using `FakeTimeProvider`); assert SC-006: 100-iteration repeat-tick produces exactly 1 event row per `(ticket_id, breach_kind)`
- [ ] T085 [P] [US4] Create `tests/Support.Tests/Integration/LeadReassignmentTests.cs` asserting reassign writes superseded assignment row, denormalized `tickets.assigned_agent_id` flips, audit row captures justification

### Implementation for User Story 4

- [ ] T086 [P] [US4] Create `Modules/Support/Workers/SlaBreachWatchWorker.cs` as a `BackgroundService`: 60-second cadence; advisory-lock guarded; selects rows per data-model.md §4 `idx_tickets_breach_scan`; writes `TicketSlaBreachEvent` + stamps `breach_acknowledged_at_*` in the same transaction; emits the corresponding domain event
- [ ] T087 [P] [US4] Create `Modules/Support/Lead/ReassignTicket/ReassignTicketCommand.cs` + handler with `justification_note ≥ 10 chars` validation; writes new `TicketAssignment` row; stamps `superseded_at_utc` on the prior; updates denormalized `tickets.assigned_agent_id`; audits + emits `TicketReassigned`
- [ ] T088 [P] [US4] Create `Modules/Support/Lead/OverrideSlaTargets/OverrideSlaTargetsCommand.cs` + handler updating the snapshot columns + recomputing `_due_utc` fields; clears prior `breach_acknowledged_at_*` if the override moves the deadline beyond `now()`; audits
- [ ] T089 [US4] Wire HTTP endpoints in `Modules/Support/Lead/LeadEndpoints.cs`: `POST /v1/admin/support-tickets/{id}/reassign`, `POST /v1/admin/support-tickets/{id}/sla-override`
- [ ] T090 [US4] Apply `[RequirePermission(SupportPermissions.SupportLead)]` to lead endpoints; rate-limit middleware `60/h reassign`, `10/h SLA override` per FR-039
- [ ] T091 [US4] Add ICU keys for `support.ticket.reassign_justification_required`, `support.ticket.target_agent_not_in_market`, `support.ticket.sla_override_justification_required`, `support.ticket.sla_override_resolution_must_exceed_first_response`, `support.ticket.admin_rate_limit_exceeded` to EN + AR ICU files

**Checkpoint**: US4 complete — SLA breach worker emits idempotent events; lead reassign + SLA override paths audited and rate-limited.

---

## Phase 7: User Story 5 — Internal notes vs customer-visible replies (Priority: P2)

**Goal**: Internal-note posting (any agent in market); customer-visible reply restricted to assigned agent OR lead/super_admin (FR-014a); customer-facing reads strip internal notes (FR-014).

**Independent Test**: Post one internal note + one customer reply on the same ticket; call customer-facing detail and verify only the reply is returned; call agent-facing detail and verify both are returned with their `kind` flags intact.

### Tests for User Story 5

- [ ] T092 [P] [US5] Create `tests/Support.Tests/Contract/InternalNoteContractTests.cs` asserting US5 Acceptance Scenarios 1–5 (internal-note hidden from customer read; agent-facing read includes it; customer attempts forbidden; audit row written; `message_kind_immutable` reject on retroconversion attempt)
- [ ] T093 [P] [US5] Create `tests/Support.Tests/Integration/InternalNoteLeakDetectionTests.cs` asserting SC-004: 0 % of `internal_note` rows leak to any customer-facing endpoint via an exhaustive scan over all customer-facing reads with a fixture containing internal notes
- [ ] T094 [P] [US5] Create `tests/Support.Tests/Integration/NonAssignedAgentAuthTests.cs` asserting FR-014a: non-assigned agent posting `agent_reply` → `403 support.ticket.action_requires_assignment`; non-assigned agent posting `internal_note` → success; lead with `lead_intervention=true` → reply succeeds without changing assignment

### Implementation for User Story 5

- [ ] T095 [P] [US5] Create `Modules/Support/Agent/AddInternalNote/AddInternalNoteCommand.cs` + handler persisting `TicketMessage` with `kind=InternalNote`; non-assigned agents allowed; emits audit `support.ticket.internal_note_added`
- [ ] T096 [P] [US5] Create `Modules/Support/Agent/ReplyAsAgent/ReplyAsAgentCommand.cs` + handler with assignment check from FR-014a; supports `lead_intervention=true` flag for lead-without-reassign path; transitions `in_progress → waiting_customer` when agent reply asks for info
- [ ] T097 [P] [US5] Create `Modules/Support/Agent/TransitionToResolved/TransitionToResolvedCommand.cs` + handler with assignment check; requires a final agent reply per FR-003 (or system-generated reply on conversion-outcome path)
- [ ] T098 [US5] Wire HTTP endpoints `POST /v1/admin/support-tickets/{id}/replies`, `POST /v1/admin/support-tickets/{id}/internal-notes`, `POST /v1/admin/support-tickets/{id}/transition`
- [ ] T099 [US5] Update `GetMyTicketHandler` (T061) and customer-facing list to filter out `kind=InternalNote` rows server-side (FR-014); update `GetTicketAdminDetailHandler` (T071) to gate internal notes by role per `contracts/support-tickets-contract.md §2`
- [ ] T100 [US5] Add ICU keys for `support.ticket.internal_note_forbidden`, `support.ticket.action_requires_assignment`, `support.ticket.message_kind_immutable`, `support.ticket.invalid_transition`, `support.ticket.resolved_requires_agent_reply` to EN + AR ICU files

**Checkpoint**: US5 complete — internal notes are agent-only; customer-visible reply requires assignment or lead intervention.

---

## Phase 8: User Story 6 — Customer reopens resolved ticket within window (Priority: P2)

**Goal**: Reopen flow within per-market window + cap; recomputes both SLA deadlines; clears breach acknowledgments; routes back to original agent or queue.

**Independent Test**: Resolve a ticket; reopen within window; verify state transition + SLA deadlines reset + assignment routing. Then attempt reopen after window or after cap; verify rejection.

### Tests for User Story 6

- [ ] T101 [P] [US6] Create `tests/Support.Tests/Contract/ReopenTicketContractTests.cs` asserting US6 Acceptance Scenarios 1–5 (success path; closed-terminal reject; window-closed reject; market-disabled reject; count-exceeded reject)
- [ ] T102 [P] [US6] Create `tests/Support.Tests/Unit/ReopenWindowMathTests.cs` per research.md §R-09 — exhaustive table-driven tests against the window + cap math
- [ ] T103 [P] [US6] Create `tests/Support.Tests/Integration/ReopenSlaResetTests.cs` asserting reopen recomputes both `first_response_due_utc` AND `resolution_due_utc`; clears `breach_acknowledged_at_*`; allows re-breach detection on a new SLA failure

### Implementation for User Story 6

- [ ] T104 [P] [US6] Create `Modules/Support/Customer/ReopenTicket/ReopenTicketCommand.cs` + handler implementing the 10-step flow from research.md §R-09 (window check, cap check, transition, increments, SLA recompute, breach clear, assignment routing, audit, event emit)
- [ ] T105 [P] [US6] Create `Modules/Support/Customer/ReplyAsCustomer/ReplyAsCustomerHandler.cs` upgrade: detects `state=resolved AND within reopen window` and triggers reopen flow inline (FR-013) before persisting the reply
- [ ] T106 [US6] Wire HTTP endpoint `POST /v1/customer/support-tickets/{id}/reopen` (separate from reply-induced reopen)
- [ ] T107 [US6] Update validation chain in `OpenTicketValidator` and `ReplyAsCustomerValidator` to enforce reopen-window, max-reopen-count, and `reopen_window_days=0` (`reopen_disabled_for_market`) rules
- [ ] T108 [US6] Add ICU keys for `support.ticket.reopen_window_closed`, `support.ticket.reopen_count_exceeded`, `support.ticket.reopen_disabled_for_market` to EN + AR ICU files

**Checkpoint**: US6 complete — customer reopen flow is bounded by per-market window + cap; SLA reset and breach acknowledgment clearing work correctly.

---

## Phase 9: User Story 7 — `support-v1` seeder (Priority: P3)

**Goal**: Idempotent dev/staging seeder spanning all 5 states × 10 categories with breach + conversion + redaction-request examples.

**Independent Test**: `seed --dataset=support-v1 --mode=apply` against a fresh staging DB; verify per-state distribution; verify breach examples surface in queue's `breached` filter; verify conversion examples have bidirectional `TicketLink` rows.

### Tests for User Story 7

- [ ] T109 [P] [US7] Create `tests/Support.Tests/Integration/SupportV1SeederTests.cs` asserting SC-009: ≥ 1 ticket per state × 10 categories; 2 SLA-breach examples; 2 return-conversion examples; 1 redaction-request example
- [ ] T110 [P] [US7] Create `tests/Support.Tests/Integration/SupportV1SeederIdempotencyTests.cs` asserting second invocation is a no-op (no duplicate tickets); `--mode=dry-run` writes nothing; partial failure rolls back

### Implementation for User Story 7

- [ ] T111 [US7] Create `Modules/Support/Seeding/SupportV1DevSeeder.cs` registered under `SeedGuard.DevAndStaging` (NOT Prod); produces synthetic tickets covering: 10 `open`, 5 `in_progress`, 4 `waiting_customer`, 6 `resolved` (3 within reopen window + 3 past), 3 `closed`, 2 active SLA breaches, 2 return-conversion examples, 2 review-dispute escalations, 1 redaction-request
- [ ] T112 [P] [US7] Add bilingual sample subjects + bodies to `SupportV1DevSeeder` (AR + EN editorial-grade copy; reviewed in `AR_EDITORIAL_REVIEW.md`); ensure no machine-translation artifacts (Principle 4)
- [ ] T113 [P] [US7] Wire `SupportV1DevSeeder` into the dev-seeder registry alongside `ReviewsV1DevSeeder` so `seed --dataset=support-v1` works end-to-end
- [ ] T114 [US7] Add a CI smoke test on the staging deploy pipeline: after the seeder runs, `GET /v1/admin/support-tickets/queue?sla_breach_status=both` returns the 2 expected breach examples
- [ ] T115 [US7] Update [quickstart.md](./quickstart.md) §2 / §3 / §4 sample fixtures to reference seeded tickets from `support-v1`

**Checkpoint**: US7 complete — seeder produces a representative fixture for staging + local development.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Cross-cutting concerns + remaining slices not on any single user-story critical path.

### Policy admin slices (Phase K)

- [ ] T116 [P] Create `Modules/Support/PolicyAdmin/UpdateSlaPolicy/UpdateSlaPolicyCommand.cs` + handler with `resolution > first_response` validation; audited; rate-limited per FR-039
- [ ] T117 [P] Create `Modules/Support/PolicyAdmin/UpdateMarketSchema/UpdateMarketSchemaCommand.cs` + handler accepting any subset of the `support_market_schemas` knobs
- [ ] T118 [P] Create `Modules/Support/PolicyAdmin/ListSlaPolicies/ListSlaPoliciesQuery.cs` + handler returning all per-market + per-priority targets
- [ ] T119 Wire HTTP endpoints `PUT /v1/admin/support-policies/sla/{market_code}/{priority}`, `PUT /v1/admin/support-policies/market-schemas/{market_code}`, `GET /v1/admin/support-policies`
- [ ] T120 Apply `[RequirePermission(SupportPermissions.SupportLead)]` (or `super_admin`) to policy edit endpoints; `viewer.finance` read-only access for `GET`

### Lead — agent availability + force-close (remaining lead slices)

- [ ] T121 [P] Create `Modules/Support/Lead/ToggleAgentAvailability/ToggleAgentAvailabilityCommand.cs` + handler flipping `is_on_call` per FR-019a; audited; rate-limited 60 toggles / hour
- [ ] T122 [P] Create `Modules/Support/Lead/ForceCloseTicket/ForceCloseTicketCommand.cs` + handler with `reason_note ≥ 10 chars` validation; transitions to `closed` from any non-closed state with `triggered_by=lead_force_close`
- [ ] T123 Wire HTTP endpoints `POST /v1/admin/agent-availability/toggle`, `POST /v1/admin/support-tickets/{id}/force-close`

### Agent — retag-category + by-customer history (remaining agent slices)

- [ ] T124 [P] Create `Modules/Support/Agent/RetagCategory/RetagCategoryCommand.cs` + handler with category-vs-linked-entity-kind consistency check from FR-007; audited
- [ ] T125 [P] Create `Modules/Support/Agent/ListTicketsByCustomer/ListTicketsByCustomerQuery.cs` + handler for support-investigation reads; rate-limited
- [ ] T126 Wire HTTP endpoints `POST /v1/admin/support-tickets/{id}/retag-category`, `GET /v1/admin/support-tickets/by-customer/{customer_id}`

### Super-admin redaction slices (Phase L)

- [ ] T127 [P] Create `Modules/Support/SuperAdmin/RedactAttachment/RedactAttachmentCommand.cs` + handler implementing FR-012a per research.md §R-07: tombstones storage object via spec 015; updates `TicketAttachment` row to `state=redacted`; emits `TicketAttachmentRedacted`
- [ ] T128 [P] Create `Modules/Support/SuperAdmin/RedactMessage/RedactMessageCommand.cs` + handler implementing FR-011a: forwards original body to spec 028 encrypted audit-only storage (stub at V1); updates `TicketMessage` row to `state=redacted, body=null`; closes the originating redaction-request ticket with synthetic resolution note; emits `TicketMessageRedacted`
- [ ] T129 [P] Create `Modules/Support/Customer/OpenRedactionRequestTicket/OpenRedactionRequestTicketCommand.cs` + handler implementing the customer-initiated redaction-request flow per FR-011a; auto-routes the resulting ticket to the super_admin queue; bypasses FR-019 auto-assignment
- [ ] T130 [P] Create `Modules/Support/SuperAdmin/ListRedactionRequestQueue/ListRedactionRequestQueueQuery.cs` + handler returning only `category=redaction_request` tickets; super_admin-only filter on the agent queue UI
- [ ] T131 Wire HTTP endpoints `POST /v1/admin/support-tickets/{id}/attachments/{attachment_id}/redact`, `POST /v1/admin/support-tickets/{id}/messages/{message_id}/redact`, `POST /v1/customer/support-tickets/redaction-request`, `GET /v1/admin/support-tickets/redaction-request-queue`
- [ ] T132 Apply `[RequirePermission("super_admin")]` to the two redaction endpoints + the redaction-request-queue endpoint; verify `support.lead` is rejected at handler layer with `403 support.ticket.redaction_super_admin_only`
- [ ] T133 Add ICU keys for `support.ticket.redaction_super_admin_only`, `support.ticket.redaction_reason_required`, `support.ticket.redaction_request_message_not_in_originating_ticket`, `support.ticket.redaction_request_already_redacted`, `support.ticket.redaction_message_not_redactable`, `support.ticket.redaction_attachment_already_redacted` to EN + AR ICU files

### Workers (Phase N)

- [ ] T134 [P] Create `Modules/Support/Workers/AutoCloseResolutionWindowWorker.cs` as a `BackgroundService`: hourly cadence; advisory-lock guarded; transitions `resolved` tickets past `auto_close_after_resolved_days` to `closed` with `triggered_by=auto_close_resolution_window`; emits `TicketClosed`
- [ ] T135 [P] Create `Modules/Support/Workers/OrphanedAssignmentReclaimWorker.cs` as a `BackgroundService`: nightly (00:30 UTC); advisory-lock guarded; reclaims tickets whose `agent_id` is no longer active in `identity.users`; appends synthetic system-event message; emits `TicketReassigned`
- [ ] T136 [P] Create `Modules/Support/Subscribers/CustomerAccountLifecycleHandler.cs` implementing `ICustomerAccountLifecycleSubscriber`; on `customer.account_locked` / `customer.account_deleted`, transitions every non-`closed` ticket by that customer to `closed` with `triggered_by=author_account_locked`

### Metrics endpoint (FR-041)

- [ ] T136a [P] Create `Modules/Support/Metrics/GetSupportMetricsQuery.cs` + handler computing per-market open-ticket counts, average first-response time, average resolution time, breach rate per priority per FR-041; readable by `super_admin` OR `viewer.finance`; detailed analytics dashboards live in spec 028
- [ ] T136b Wire HTTP endpoint `GET /v1/admin/support-tickets/metrics` with `[RequirePermission(...)]` accepting `super_admin` OR `viewer.finance`

### Reviewer-display rule wiring (FR-016a)

- [ ] T136c [P] Update `Modules/Support/Agent/GetTicketAdminDetail/GetTicketAdminDetailHandler.cs` (T071) and `Modules/Support/Agent/ListAgentQueue/ListAgentQueueHandler.cs` (T069) to apply the canonical FR-016a reviewer-display rule via `IReviewDisplayHandleQuery` (reused from spec 022): if `review_display_handle` is non-empty render it; else render `first_name + ' ' + last_initial + '.'`. For B2B customers, also surface the company name as a secondary line via `ICompanyAccountQuery`

### Submission-to-queue latency test (SC-002)

- [ ] T136d [P] Create `tests/Support.Tests/Integration/SubmissionToQueueLatencyTests.cs` asserting SC-002: a ticket submitted via `POST /v1/customer/support-tickets` MUST be queryable from `GET /v1/admin/support-tickets/queue` within 5 seconds of submission, p95, measured over a 100-iteration soak with realistic concurrent load

### Domain events + spec 025 contract (Phase P)

- [ ] T137 Verify all 16 events from `Modules/Shared/SupportTicketDomainEvents.cs` (T044) are published by their corresponding handlers via grep + a contract test; spec 025 binding deferred to its own PR
- [ ] T138 Document the event payload schemas in `contracts/support-tickets-contract.md §9` and add an OpenAPI extension stub for spec 025 to consume

### OpenAPI artifact (Phase Q)

- [ ] T139 Regenerate `services/backend_api/openapi.support.json` via `dotnet build` + Swashbuckle to capture all 30 endpoints + the 52 reason codes; commit the artifact
- [ ] T140 Add a contract-diff CI check that fails the PR if `openapi.support.json` is out-of-sync with the source-of-truth `contracts/support-tickets-contract.md`

### Audit coverage (Phase T)

- [ ] T141 Run the spec 015 audit-coverage script against the implemented module; assert 100 % coverage of the 18 audit-event kinds from data-model.md §5; investigate + fix any gaps
- [ ] T142 [P] Create `tests/Support.Tests/Integration/AuditCoverageTests.cs` asserting SC-003: every state transition + every assignment / reassignment + every SLA override + every breach event + every internal-note creation + every reply + every redaction produces a matching audit row

### AR editorial sweep (Phase R)

- [ ] T143 [P] Author / review every AR string in `Modules/Support/Messages/support.ar.icu` to editorial-grade quality (Principle 4); flag pending entries in `Modules/Support/Messages/AR_EDITORIAL_REVIEW.md`
- [ ] T144 [P] Verify SC-008: AR-locale screen-render correctness scores 100 % against a representative 25-string editorial-review checklist (no missing keys; no machine-translated artifacts; correct RTL alignment hints)

### Concurrency + rate-limit hardening (Phase T)

- [ ] T145 [P] Create `tests/Support.Tests/Integration/CustomerRateLimitTests.cs` asserting FR-010: 5 creations / hour / customer; 30 replies / hour / actor / ticket; over-limit returns `429 support.ticket.creation_rate_exceeded` or `support.ticket.reply_rate_exceeded`
- [ ] T146 [P] Create `tests/Support.Tests/Integration/AdminRateLimitTests.cs` asserting FR-039: 30 claims / minute, 30 reassigns / hour, 10 SLA overrides / hour, 60 availability toggles / hour
- [ ] T147 [P] Create `tests/Support.Tests/Integration/IdempotencyTests.cs` asserting FR-040: every state-change endpoint requires `Idempotency-Key`; duplicate within 24 h returns the original 200 response with the same body

### DoD checklist + final verification

- [ ] T148 Run the full test suite (`dotnet test services/backend_api/tests/Support.Tests/Support.Tests.csproj`); assert all unit + integration + contract tests pass against Testcontainers Postgres; assert SC-001 through SC-011 are all green
- [ ] T149 Verify `DELETE /v1/admin/support-tickets/{id}` returns `405 support.ticket.row.delete_forbidden` per FR-005a (run a one-shot integration test)
- [ ] T150 Compute the constitution / ADR fingerprint via `scripts/compute-fingerprint.sh` and attach to the PR; ensure CI green for lint + format + contract-diff + impeccable-scan (advisory) per docs/dod.md

**Checkpoint**: All 7 user stories complete + polish phases done. Module is at DoD and ready for spec 014 (storefront) + spec 015 (admin shell) UI work to begin.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phases 3–9)**: All depend on Foundational. Within Phase 2, Primitives (T006–T019) → Persistence (T020–T034) → Cross-module shared (T035–T044) → Reference seeder + module wiring (T045–T047) → Foundational tests (T048–T052).
- **Polish (Phase 10)**: Depends on the user stories that exercise the polished surfaces (e.g., Audit coverage T141 depends on every state-transitioning slice having shipped).

### User Story Dependencies

- **US1 (P1)** — Customer opens ticket: depends only on Foundational. MVP slice.
- **US2 (P1)** — Agent claims + queue: depends only on Foundational. MVP slice. Independent of US1 implementation but needs US1's `OpenTicket` to exercise the queue end-to-end during local dev.
- **US3 (P1)** — Convert to return: depends on US1 (needs a ticket to convert) + the spec 013 fake from T037. MVP slice.
- **US4 (P2)** — SLA breach + lead reassign: depends on US1 + US2 (needs tickets in `open` / `in_progress` to breach + reassign).
- **US5 (P2)** — Internal notes: depends on US1 + US2 (needs the queue + reply paths). Adds the FR-014a non-assigned-agent rule + the customer-side internal-note hiding.
- **US6 (P2)** — Reopen flow: depends on US1 (needs the resolve + reply paths).
- **US7 (P3)** — Seeder: depends on all of US1 / US2 / US3 / US4 / US5 / US6 (the seeder produces ticket fixtures spanning every state + flow).

### Within Each User Story

- Tests written + failing before implementation (per project DoD).
- Models / handlers / endpoints / wiring follow in dependency order.
- Story is complete + independently testable before the next priority starts.

### Parallel Opportunities

- All Phase 1 tasks marked [P] can run in parallel.
- All Phase 2 primitives (T006–T019) can run in parallel — no dependencies between them.
- All Phase 2 entities (T020–T028) can run in parallel — different files.
- All Phase 2 cross-module shared declarations (T035–T044) can run in parallel — different files.
- Within each user story phase, all `[P]` tasks for tests + parallelisable handlers can run in parallel.
- Across stories: once Foundational is done, US1 / US2 / US3 can all start in parallel (different developers); US4 / US5 / US6 follow once their dependencies land.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests in parallel:
Task: "Contract test in tests/Support.Tests/Contract/OpenTicketContractTests.cs"
Task: "MarketCodeResolution integration tests in tests/Support.Tests/Integration/MarketCodeResolutionTests.cs"
Task: "Attachment integration tests in tests/Support.Tests/Integration/TicketAttachmentTests.cs"
Task: "Reply-loop integration tests in tests/Support.Tests/Integration/TicketReplyLoopTests.cs"

# Launch all US1 [P] handlers in parallel (after T057-T059):
Task: "ListMyTicketsQuery + handler"
Task: "GetMyTicketQuery + handler"
Task: "UploadAttachmentCommand + handler"
```

---

## Implementation Strategy

### MVP First (US1 + US2 + US3)

1. Phase 1: Setup.
2. Phase 2: Foundational (CRITICAL — blocks all stories).
3. Phase 3: US1 — Customer opens ticket + replies.
4. Phase 4: US2 — Agent works queue.
5. Phase 5: US3 — Convert to return.
6. **STOP and VALIDATE**: a customer can open a ticket, an agent can claim and reply, and a ticket can be converted to a return-request. This is the launch-grade MVP.

### Incremental Delivery (P2 stories)

7. Phase 6: US4 — SLA breach + lead reassignment (operational depth).
8. Phase 7: US5 — Internal notes (audit + accountability).
9. Phase 8: US6 — Reopen flow (post-resolution recourse).

### Polish + Seeder

10. Phase 9: US7 — Seeder for staging fixtures.
11. Phase 10: Polish (policy admin, super_admin redaction, workers, OpenAPI, audit coverage, AR editorial, hardening, DoD verification).

### Parallel Team Strategy

With three developers post-Foundational:
- Developer A: US1 (customer-side) → US6 (reopen).
- Developer B: US2 (agent queue) → US5 (internal notes).
- Developer C: US3 (conversion) → US4 (SLA breach worker).
- All three converge on US7 + Phase 10 polish before DoD.

---

## Notes

- [P] tasks = different files, no cross-task dependencies.
- [Story] label maps each task to a specific user story for traceability — required on Phase 3–9 tasks; absent on Setup, Foundational, and Polish tasks.
- Each user story phase is a complete, independently testable increment — stop at any checkpoint to validate the story alone.
- Verify tests fail before implementing (project DoD).
- Commit after each task or logical group; small commits help CodeRabbit iteration (per project-memory rule).
- Avoid: vague tasks, same-file conflicts on parallel tasks, cross-story dependencies that break independence.

**Total tasks: 154** · 5 Setup · 47 Foundational · 13 US1 · 9 US2 · 8 US3 · 9 US4 · 9 US5 · 8 US6 · 7 US7 · 39 Polish.
