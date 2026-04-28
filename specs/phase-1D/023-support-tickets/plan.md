# Implementation Plan: Support Tickets

**Branch**: `phase_1D_creating_specs` (working) · target merge: `023-support-tickets` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1D/023-support-tickets/spec.md`

## Summary

Deliver the Phase-1D customer-support-tickets module that turns Principles 17 / 21 ("post-purchase experience MUST be strong; the system MUST be designed for real operations from day one — support / ticketing, returns / refunds, SLA, audit") into a single backend module covering all 8 deliverable items from the implementation plan plus the 5 clarifications resolved in `/speckit-clarify`:

1. **Ticket entity + 5-state lifecycle** (Principle 24): `open → in_progress ↔ waiting_customer → resolved → closed (terminal, no reopen)`. Reopen `resolved → in_progress` allowed within per-market window. Encoded in `TicketStateMachine.cs` with compile-time transition guards.
2. **Customer ticket flow**: open + list + detail + reply + reopen + convert-to-return + redaction-request. Verified-ownership gate on linked entities (orders / order_lines / returns / quotes / reviews / verification submissions) via per-module read contracts in `Modules/Shared/`.
3. **Admin agent queue** (FR-015–FR-020): single filterable queue (`market_code`, `category`, `priority`, `state`, `assigned_agent_id`, `sla_breach_status`, `linked_entity_kind`, `created_at_range`); page 50 max 200; optimistic-concurrency-guarded claim (FR-017); lead reassign + force-close + SLA override.
4. **Frozen-at-creation SLA snapshot + breach worker** (FR-021–FR-026): per-market + per-priority targets snapshotted onto each ticket on creation; `SlaBreachWatchWorker` runs every 60 s, idempotent on `(ticket_id, breach_kind)`; emits `ticket.sla_breached.first_response` / `ticket.sla_breached.resolution`; lead override recomputes targets + audited.
5. **Cross-module linkage + bidirectional return-conversion** (FR-027–FR-029): polymorphic `linked_entity_kind` + `linked_entity_id`; queue renders read-only previews via the per-kind read contracts (`IOrderLinkedReadContract`, `IReturnLinkedReadContract`, `IQuoteLinkedReadContract`, `IReviewLinkedReadContract`, `IVerificationLinkedReadContract`) declared in `Modules/Shared/` (loose-coupling pattern from specs 020 / 021 / 022); ticket → return-request conversion calls `IReturnRequestCreationContract` idempotently; reverse subscriber transitions originating ticket to `resolved` on `return.completed` / `return.rejected`.
6. **Internal notes vs customer-visible replies** (FR-014, FR-014a): immutable message `kind` (customer_reply / agent_reply / internal_note / system_event); customer-facing reads strip `internal_note` entirely; non-assigned agents may post internal notes only — replies + state transitions require assignment OR `support.lead` / `super_admin` (Clarification Q4 of /speckit-clarify).
7. **Minimal agent availability** (FR-019, FR-019a, Clarification Q1): `SupportAgentAvailability` table holds only an `is_on_call` boolean per `(agent_id, market_code)`; no shift windows in V1. `support.lead` toggles via dedicated endpoint; full shift planner deferred to Phase 1.5.
8. **Linked-entity-driven `market_code` resolution** (FR-006a, Clarification Q3): the ticket's market is inherited from the linked entity (snapshotted at creation); standalone tickets fall back to customer's market-of-record; linked-entity-unavailable at submission fails with `400 support.ticket.market_code_unresolvable` (no silent misrouting).
9. **PII redaction paths** (FR-011a + FR-012a, Clarifications Q2 + Q5): `super_admin` may redact attachments directly during moderation; customer-initiated message-body redaction requires a special `category=redaction_request` ticket auto-routed to a `super_admin`-only triage queue; original bodies preserved in encrypted audit-only storage owned by spec 028; tombstone marker on read; both paths emit dedicated events.
10. **Reopen flow with cap** (FR-029): reopen within `reopen_window_days` (default 14, range 0–60, `0` disables); recomputes both `first_response_due_utc` + `resolution_due_utc`; `max_reopen_count` cap (default 3, range 1–10).
11. **Auto-cascades**: subscribes to spec 004's `customer.account_locked` / `customer.account_deleted` events → tickets transition to `closed` with `triggered_by=author_account_locked`; nightly `OrphanedAssignmentReclaimWorker` reclaims tickets assigned to offboarded agents.
12. **Multi-vendor readiness** (Principle 6): `vendor_id` slot reserved on every ticket row; never populated in V1.
13. **`support-v1` seeder**: idempotent; populates ≥ 1 ticket in each of `open`, `in_progress`, `waiting_customer`, `resolved`, `closed` states across all 10 categories; per-market SLA policy rows for KSA + EG; bilingual editorial-grade ICU keys.

No customer-facing UI ships in this spec. Customer storefront is owned by Phase 1C spec 014; the agent queue UI is owned by spec 015. 023 ships only the backend contracts and seeders against which 014 / 015 build their screens.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16 (per spec 004 + ADR-022).

**Primary Dependencies**:
- `MediatR` v12.x + `FluentValidation` v11.x — vertical-slice handlers (ADR-003).
- `Microsoft.EntityFrameworkCore` v9.x — code-first migrations on the new `support` schema (ADR-004).
- `Microsoft.AspNetCore.Authorization` (built-in) — `[RequirePermission("support.*")]` attributes from spec 004's RBAC.
- `Modules/AuditLog/IAuditEventPublisher` (existing) — every state transition + every assignment / reassignment + every SLA override + every breach event + every internal-note creation + every redaction.
- `Modules/Identity` consumables — RBAC primitives + new permissions `support.agent`, `support.lead`. The existing `ICustomerAccountLifecycleSubscriber` from spec 020 / 022 is reused for the auto-close-on-account-locked cascade.
- `Modules/Shared/IAuditEventPublisher`, `Modules/Shared/AppDbContext` — existing; reused.
- New shared interfaces declared under `Modules/Shared/` (see Project Structure):
  - `IOrderLinkedReadContract` — order + order_line read for ownership + queue preview; spec 011 implements.
  - `IReturnLinkedReadContract` + `IReturnRequestCreationContract` + `IReturnOutcomeSubscriber` — spec 013 implements (creation contract + outcome subscriber). The creation contract is invoked by the convert-to-return endpoint with `Idempotency-Key`; the outcome subscriber consumes `return.completed` / `return.rejected` events to auto-transition the originating ticket.
  - `IQuoteLinkedReadContract` — spec 021 implements.
  - `IReviewLinkedReadContract` — spec 022 implements.
  - `IVerificationLinkedReadContract` — spec 020 implements.
  - `ICompanyAccountQuery` — spec 021 implements; resolves `company_id` from `customer_id` for B2B-customer ticket scoping (FR-016a).
  - `IReviewDisplayHandleQuery` — reused from spec 022 (declared by spec 022; consumed by 023 for the FR-016a canonical display rule).
  - `SupportTicketDomainEvents.cs` — 16 `INotification` records subscribed by spec 025.
- `MessageFormat.NET` (already vendored by spec 003) — ICU AR/EN keys for every customer-visible / operator-visible reason code, state label, category label, breach badge.

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). New `support` schema; **9 new tables**:

- `support.tickets` — the lifecycled ticket entity; carries SLA snapshots + market_code + vendor_id + linked_entity columns.
- `support.ticket_messages` — append-only messages (`kind ∈ {customer_reply, agent_reply, internal_note, system_event}`).
- `support.ticket_attachments` — append-only attachment metadata (storage_object_id + MIME + size + filename); supports tombstone state for FR-012a redaction.
- `support.ticket_links` — append-only polymorphic links (one ticket may carry multiple links over its lifetime — e.g., a ticket can spawn a return-request mid-conversation).
- `support.ticket_assignments` — append-only assignment history (current row is the one without `superseded_at`).
- `support.ticket_sla_breach_events` — append-only breach detections; idempotent on `(ticket_id, breach_kind)`.
- `support.sla_policies` — per `(market_code, priority)` SLA targets; admin-editable.
- `support.support_market_schemas` — per-market knobs: `auto_assignment_enabled`, `auto_close_after_resolved_days`, `reopen_window_days`, `max_reopen_count`, `attachment_max_per_ticket`, `attachment_max_size_mb`.
- `support.agent_availability` — minimal `is_on_call` boolean per `(agent_id, market_code)`; toggled by `support.lead`.

State writes use EF Core optimistic concurrency via Postgres `xmin` mapped as `IsRowVersion()` (project pattern from specs 020 / 021 / 022 / 007-b) for the concurrent-claim, concurrent-reply, and concurrent-reopen cases.

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers Postgres (per spec 003 contract — no SQLite shortcut). Contract tests assert HTTP shape parity between every `spec.md` Acceptance Scenario and the live handler. Property tests for state-machine invariants (no terminal→non-terminal except via reopen, no double-claim, idempotent transitions). Concurrency tests for FR-017 (two agents claiming) + FR-019 (reassignment race). Cross-module subscriber tests use fake publishers shipped in `Modules/Shared/Testing/`. Time-driven tests use `FakeTimeProvider` to advance the SLA-breach worker. Cross-module read contracts are stubbed via 5 `Fake*LinkedReadContract` doubles so 023 tests run without specs 011 / 013 / 020 / 021 / 022 at DoD on `main`. Idempotency tests assert FR-040 envelope (every state-transitioning POST requires `Idempotency-Key`).

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — Phase 1C specs 014 / 015 deliver UI.

**Project Type**: .NET vertical-slice module under the modular monolith (ADR-023). Net-new top-level module: `Modules/Support/`.

**Performance Goals**:
- **Ticket creation write path**: p95 ≤ 800 ms (linked-entity read + ownership check + market resolution + SLA snapshot + persist + audit + auto-assign if enabled).
- **Reply write path**: p95 ≤ 500 ms.
- **Attachment upload**: bounded by spec 015 storage abstraction; the 023 endpoint is a reference-association endpoint and writes p95 ≤ 200 ms.
- **Queue list read**: p95 ≤ 500 ms with 10 000 open + in-progress tickets per market (SC-011).
- **Ticket detail load**: p95 ≤ 800 ms with full message + assignment + SLA + linked-entity preview.
- **Claim write**: p95 ≤ 300 ms; concurrent-claim race resolved deterministically.
- **SLA breach detection latency**: p95 ≤ 60 s from deadline (SC-005).
- **Reassign + force-close + SLA override**: p95 ≤ 500 ms.
- **Convert-to-return**: p95 ≤ 1500 ms (includes synchronous spec 013 contract call).

**Constraints**:
- **Idempotency** (FR-040): every state-transitioning POST endpoint requires `Idempotency-Key` (per spec 003 platform middleware); duplicates within 24 h return the original 200 response.
- **Concurrency guard**: every state-transitioning command uses an EF Core `RowVersion` (xmin) optimistic-concurrency check; the loser sees `409 support.ticket.version_conflict` (or its claim-side analog `409 support.ticket.assignment_conflict`).
- **Hard-delete prohibition** (FR-005a): the API layer MUST return `405 support.ticket.row.delete_forbidden` for any `DELETE /v1/admin/support-tickets/{id}` route. Soft-state `closed` is the only deletion path. Append-only tables (`ticket_messages`, `ticket_attachments`, `ticket_links`, `ticket_assignments`, `ticket_sla_breach_events`) MUST be guarded by Postgres `BEFORE UPDATE OR DELETE` triggers (with controlled exceptions for the 2 redaction operations FR-011a / FR-012a, gated by row-level checks for `super_admin` actor + redacted-status update only).
- **PII at rest**: ticket subject + body + replies are customer-supplied free text and may contain PII; stored as plain TEXT (TDE covers at-rest). Attachment storage is owned by spec 015 — 023 stores only `storage_object_id`. PII redaction paths (FR-011a / FR-012a) are `super_admin`-only; original content is preserved in encrypted audit-only storage owned by spec 028 (not in 023's tables).
- **PII in logs**: `ILogger` destructuring filters block any ticket body / reply content from log output. Audit events MAY include the body in `before_jsonb` / `after_jsonb` for support forensics; access to those columns is gated by `support.agent` (assigned-only) / `support.lead` / `super_admin` permissions.
- **Time source**: every state transition + every SLA-breach window + every reopen-window check + every rate-limit window reads `TimeProvider.System.GetUtcNow()`; tests inject `FakeTimeProvider`.
- **Worker idempotency**: `SlaBreachWatchWorker` is idempotent on `(ticket_id, breach_kind)`; `AutoCloseResolutionWindowWorker` is idempotent on `ticket_id`; `OrphanedAssignmentReclaimWorker` is idempotent on `assignment_id`. Workers use the existing Postgres advisory-lock pattern from spec 020 to coordinate horizontally.
- **Single-locale ticket content**: customer-supplied subject + body + replies are stored in the customer's authoring `locale` only; 023 MUST NOT auto-translate (Principle 4). System-generated copy resolves to ICU AR + EN keys.
- **AR editorial**: every system-generated customer-visible string (state labels, category labels, reason codes, breach badge labels, system-event message bodies) MUST have both `ar` and `en` ICU keys; AR strings flagged in `AR_EDITORIAL_REVIEW.md`.

**Scale/Scope**: ~30 HTTP endpoints (customer: 8, agent queue: 7, lead: 4, super_admin redaction: 2, policy admin: 3, lookups: 3, metrics: 1, public unauth: 0 — every endpoint is authenticated). **48 functional requirements** (FR-001–FR-041 with FR-005a, FR-006a, FR-011a, FR-012a, FR-014a, FR-016a, FR-019a interleaved). 11 SCs. 8 key entities + 1 minimal availability table. 1 five-state lifecycle. 9 net-new tables. 3 hosted workers. **16 lifecycle domain events**. 10 fixed categories. 4 priority levels. Target capacity at V1 launch: 500 tickets / day across both markets at steady state, peaks of 30 concurrent agents on the queue, 10 000 open + in-progress tickets resident in the queue, 5 SLA-breach events / hour during peak ops.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Every endpoint requires authentication (no public unauth surface). Browse-without-auth doesn't apply — customers must sign in to open / read tickets. | PASS |
| P4 Arabic / RTL editorial | System-generated strings (state labels, category labels, reason codes, breach badge, system-event bodies) bilingual-required (FR-032). Customer-supplied content is single-locale per FR-006 — not machine-translated (Principle 4 protected). AR-locale screen render verified in SC-008. | PASS |
| P5 Market Configuration | `support_market_schemas` rows hold every per-market knob; `sla_policies` rows hold per-market + per-priority SLA targets; per-market `agent_availability`. No hardcoded EG/KSA branches. | PASS |
| P6 Multi-vendor-ready | `vendor_id` slot reserved on every ticket row, populated from linked entity when available. V1 always null in admin UI. | PASS |
| P9 B2B | Tickets carry resolved `company_id` from `ICompanyAccountQuery` (spec 021); `b2b.account_manager` reads tickets scoped to their owned `company_id`. SLA policies are per-priority — B2B tickets prioritised at submission via lead-only `high` / `urgent` selection. | PASS |
| P17 Order & post-purchase | Tickets link to order / order_line / return / quote / review / verification entities through cross-module read contracts; ticket → return-request bidirectional conversion (FR-028). | PASS |
| P19 Notifications | 16 domain events declared; spec 025 subscribes; no in-line notification calls (FR-035). | PASS |
| P21 Operational Readiness | Support / ticketing module + SLA tracking + breach detection + assignment queue + internal notes + audit. Constitutional-spec; this is the whole spec. | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9, MediatR — no deviation. | PASS |
| P23 Architecture | New vertical-slice module `Modules/Support/`; reuses existing seams (`IAuditEventPublisher`, RBAC, customer-account-lifecycle subscriber, storage abstraction). No premature service extraction. | PASS |
| P24 State Machines | One explicit state machine (`TicketState`, 5 states) documented in `data-model.md §3` with allowed states, transitions, triggers, actors, failure handling. Reopen path explicitly modelled. | PASS |
| P25 Audit | Every state transition + every assignment / reassignment + every SLA override + every breach event + every internal-note creation + every reply + every redaction emits an audit row (FR-030, FR-031). SC-003 verifies end-to-end. | PASS |
| P27 UX Quality | No UI here, but error payloads carry stable reason codes (`support.ticket.linked_entity_not_owned`, `support.ticket.assignment_conflict`, `support.ticket.market_code_unresolvable`, `support.ticket.action_requires_assignment`, etc.) for spec 014 / 015 to render. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request / response / errors / reason codes. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1D Milestone 7. AI-suggestions, CSAT, ticket merging, macros, live-chat, full shift planner all explicitly Out of Scope. | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/Support/`. | PASS |
| ADR-003 Vertical slice | One folder per slice under `Support/Customer/`, `Support/Agent/`, `Support/Lead/`, `Support/PolicyAdmin/`, `Support/SuperAdmin/`. | PASS |
| ADR-004 EF Core 9 | Code-first migrations under `Modules/Support/Persistence/Migrations/`. `SaveChangesInterceptor` audit hook from spec 003 reused. `ManyServiceProvidersCreatedWarning` suppressed in `SupportModule.cs` (project-memory rule). | PASS |
| ADR-010 KSA residency | All tables in the KSA-region Postgres; no cross-region replication. | PASS |

**No violations**. Complexity Tracking below documents intentional non-obvious design choices.

### Post-design re-check (after Phase 1 artifacts)

Re-evaluated after `data-model.md`, `contracts/support-tickets-contract.md`, `quickstart.md`, and `research.md` were authored. **No new violations introduced.**

- **P21 (re-emphasised)**: every operational-readiness MUST is bound to a specific FR + table column + acceptance scenario. SLA + breach + audit + queue + assignment + internal notes verified end-to-end. ✅
- **P5**: every market-tunable knob is sourced from `support_market_schemas` + `sla_policies` rows. ✅
- **P24**: the 5-state machine + reopen edge are encoded in `TicketStateMachine.cs` with compile-time transition guards. ✅
- **P25**: 18 audit-event kinds documented in `data-model.md §5`. ✅
- **P28**: contracts file enumerates 30 endpoints + 8 newly-declared cross-module interfaces (+ 2 reused from specs 020 / 022 = 10 total per data-model.md §7) with full reason-code inventory (52 owned codes). ✅

## Project Structure

### Documentation (this feature)

```text
specs/phase-1D/023-support-tickets/
├── plan.md                  # This file
├── research.md              # Phase 0 — linked-entity contract design, return-conversion idempotency, SLA snapshot semantics, breach worker idempotency, market-code resolution failure path, agent availability minimal toggle, redaction tombstone storage, non-assigned reply rule, reopen window math, OrphanedAssignmentReclaim
├── data-model.md            # Phase 1 — 9 tables, 1 state machine + reopen edge, ERD, 18 audit-event kinds, 16 domain events
├── contracts/
│   └── support-tickets-contract.md   # Phase 1 — every customer + agent + lead + policy-admin + super-admin endpoint, every reason code, every domain event, every cross-module interface
├── quickstart.md            # Phase 1 — implementer walkthrough, first slice (open ticket), claim-and-reply smoke, SLA-breach simulation
├── checklists/
│   └── requirements.md      # quality gate (pass)
└── tasks.md                 # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   ├── Shared/                                              # EXTENDED
│   │   ├── IOrderLinkedReadContract.cs                      # NEW — spec 011 implements
│   │   ├── IReturnLinkedReadContract.cs                     # NEW — spec 013 implements
│   │   ├── IReturnRequestCreationContract.cs                # NEW — spec 013 implements (idempotent)
│   │   ├── IReturnOutcomeSubscriber.cs                      # NEW — spec 013 publishes via the in-process bus
│   │   ├── IQuoteLinkedReadContract.cs                      # NEW — spec 021 implements
│   │   ├── IReviewLinkedReadContract.cs                     # NEW — spec 022 implements
│   │   ├── IVerificationLinkedReadContract.cs               # NEW — spec 020 implements
│   │   ├── ICompanyAccountQuery.cs                          # NEW — spec 021 implements (B2B customer → company_id)
│   │   ├── SupportTicketDomainEvents.cs                     # NEW — 16 INotification records
│   │   └── (existing files unchanged; ICustomerAccountLifecycleSubscriber + IReviewDisplayHandleQuery reused)
│   ├── Support/                                             # NEW MODULE
│   │   ├── SupportModule.cs                                 # AddSupportModule(IServiceCollection); MediatR scan; AddDbContext suppressing ManyServiceProvidersCreatedWarning; register subscribers + workers + IReturnOutcomeSubscriber binding
│   │   ├── Primitives/
│   │   │   ├── TicketState.cs                               # enum: Open, InProgress, WaitingCustomer, Resolved, Closed
│   │   │   ├── TicketStateMachine.cs                        # transition rules + reopen edge + force-close + auto-close
│   │   │   ├── TicketCategory.cs                            # enum: 10 fixed values + ICU mapper
│   │   │   ├── TicketPriority.cs                            # enum: Low, Normal, High, Urgent
│   │   │   ├── TicketActorKind.cs                           # enum: Customer, Agent, Lead, SuperAdmin, FinanceViewer, ReviewModerator, B2BAccountManager, System
│   │   │   ├── TicketReasonCode.cs                          # enum + ICU-key mapper for all owned reason codes
│   │   │   ├── TicketTriggerKind.cs                         # enum: 13 trigger kinds
│   │   │   ├── TicketLinkedEntityKind.cs                    # enum: Order, OrderLine, ReturnRequest, Quote, Review, Verification
│   │   │   ├── TicketMessageKind.cs                         # enum: CustomerReply, AgentReply, InternalNote, SystemEvent
│   │   │   ├── SupportMarketPolicy.cs                       # value-object resolved from support_market_schemas row
│   │   │   ├── SlaPolicySnapshot.cs                         # value-object frozen onto ticket at creation
│   │   │   └── MarketCodeResolver.cs                        # FR-006a: linked-entity-first, customer-of-record fallback for standalone
│   │   ├── Customer/
│   │   │   ├── OpenTicket/                                  # creates ticket; resolves market; runs ownership check; snapshots SLA; auto-assigns if enabled
│   │   │   ├── ListMyTickets/
│   │   │   ├── GetMyTicket/
│   │   │   ├── ReplyAsCustomer/                             # transitions waiting_customer → in_progress
│   │   │   ├── UploadAttachment/                            # creates TicketAttachment row from spec 015 storage_object_id
│   │   │   ├── ReopenTicket/                                # within reopen window; recomputes SLA; resets breach acknowledgments
│   │   │   ├── ConvertToReturnRequest/                      # idempotent invocation of IReturnRequestCreationContract
│   │   │   └── OpenRedactionRequestTicket/                  # FR-011a — special category=redaction_request
│   │   ├── Agent/
│   │   │   ├── ListAgentQueue/                              # filters: market, category, priority, state, assigned_agent, sla_breach, linked_entity_kind
│   │   │   ├── ClaimTicket/                                 # optimistic-concurrency-guarded
│   │   │   ├── GetTicketAdminDetail/                        # full message + audit + assignment + linked-entity-preview + internal notes (role-gated)
│   │   │   ├── ReplyAsAgent/                                # assigned-only; transitions in_progress ↔ waiting_customer
│   │   │   ├── AddInternalNote/                             # any agent-in-market may post (FR-014a)
│   │   │   ├── TransitionToResolved/                        # assigned-only
│   │   │   └── RetagCategory/                               # audited; checks linked-entity consistency
│   │   ├── Lead/
│   │   │   ├── ReassignTicket/                              # supports lead_intervention=true reply path too
│   │   │   ├── ForceCloseTicket/                            # any non-closed state → closed; audited with reason
│   │   │   ├── OverrideSlaTargets/                          # recomputes due_utc; audits
│   │   │   └── ToggleAgentAvailability/                     # FR-019a — flips is_on_call boolean
│   │   ├── PolicyAdmin/
│   │   │   ├── UpdateSlaPolicy/                             # per-market + per-priority targets
│   │   │   ├── UpdateMarketSchema/                          # auto-assignment_enabled, reopen_window_days, etc.
│   │   │   └── ListSlaPolicies/
│   │   ├── SuperAdmin/
│   │   │   ├── RedactAttachment/                            # FR-012a — tombstone storage object
│   │   │   ├── RedactMessage/                               # FR-011a — body redaction via redaction-request ticket linkage
│   │   │   └── ListRedactionRequestQueue/                   # super_admin-only filtered queue (category=redaction_request)
│   │   ├── Subscribers/
│   │   │   ├── ReturnOutcomeHandler.cs                      # consumes return.completed / return.rejected; transitions originating ticket to resolved
│   │   │   └── CustomerAccountLifecycleHandler.cs           # account_locked / deleted → ticket auto-close (FR-005a-style preservation)
│   │   ├── Workers/
│   │   │   ├── SlaBreachWatchWorker.cs                      # 60s cadence; idempotent on (ticket_id, breach_kind)
│   │   │   ├── AutoCloseResolutionWindowWorker.cs           # hourly; transitions resolved → closed after window
│   │   │   └── OrphanedAssignmentReclaimWorker.cs           # nightly; reassigns offboarded-agent tickets back to queue
│   │   ├── Authorization/
│   │   │   └── SupportPermissions.cs                        # support.agent, support.lead
│   │   ├── Entities/
│   │   │   ├── SupportTicket.cs
│   │   │   ├── TicketMessage.cs
│   │   │   ├── TicketAttachment.cs
│   │   │   ├── TicketLink.cs
│   │   │   ├── TicketAssignment.cs
│   │   │   ├── TicketSlaBreachEvent.cs
│   │   │   ├── SlaPolicy.cs
│   │   │   ├── SupportMarketSchema.cs
│   │   │   └── SupportAgentAvailability.cs
│   │   ├── Persistence/
│   │   │   ├── SupportDbContext.cs
│   │   │   ├── Configurations/                              # IEntityTypeConfiguration<T> per entity
│   │   │   └── Migrations/                                  # net-new; creates `support` schema + 9 tables + append-only triggers + redaction-tombstone exception
│   │   ├── Messages/
│   │   │   ├── support.en.icu                               # system-generated EN keys
│   │   │   ├── support.ar.icu                               # system-generated AR keys (editorial-grade)
│   │   │   └── AR_EDITORIAL_REVIEW.md
│   │   └── Seeding/
│   │       ├── SupportReferenceDataSeeder.cs                # KSA + EG market schemas + 8 SLA-policy rows (4 priorities × 2 markets); idempotent across all envs
│   │       └── SupportV1DevSeeder.cs                        # synthetic tickets spanning all 5 states + 10 categories + breach examples + return-conversion examples (Dev+Staging only, SeedGuard)
└── tests/
    └── Support.Tests/
        ├── Unit/                                            # state machine, reopen-window math, SLA-snapshot freezing, market-code resolver, reason-code mapper, agent-display rule (reuses 022's)
        ├── Integration/                                     # WebApplicationFactory + Testcontainers Postgres; every customer + agent + lead + policy + super_admin slice; concurrency guards; SLA-breach worker; subscriber tests; conversion idempotency
        └── Contract/                                        # asserts every Acceptance Scenario from spec.md against live handlers
```

**Structure Decision**: Net-new `Modules/Support/` vertical-slice module under the modular monolith. Cross-module read contracts and event types live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule). The `Customer/`, `Agent/`, `Lead/`, `PolicyAdmin/`, `SuperAdmin/` sibling layout enforces visibly that the five actor surfaces consume the same state machine but expose disjoint endpoints with disjoint RBAC. The `Subscribers/` folder houses cross-module event consumers; the `Workers/` folder houses the three reconciliation safety nets (SLA breach detection, auto-close, orphaned-assignment reclaim). The minimal `SupportAgentAvailability` entity is co-located with the other entities to make its V1 scope visible (one boolean per row; no schedules).

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `TicketState`, `TicketStateMachine`, `TicketCategory`, `TicketPriority`, `TicketReasonCode`, `TicketTriggerKind`, `TicketLinkedEntityKind`, `TicketMessageKind`, `SupportMarketPolicy`, `SlaPolicySnapshot`, `MarketCodeResolver` | Foundation for all slices |
| B. Persistence + migrations | 9 entities + EF configurations + initial migration; `SupportDbContext` with warning suppression; append-only triggers on the 6 audit-detail tables; tombstone-state exception for redaction | Unblocks all slices and workers |
| C. Reference seeder | `SupportReferenceDataSeeder` (KSA + EG market schemas + 8 SLA-policy rows; idempotent across all envs) | Unblocks integration tests + Staging/Prod boot |
| D. Cross-module shared declarations | `IOrderLinkedReadContract`, `IReturnLinkedReadContract`, `IReturnRequestCreationContract`, `IReturnOutcomeSubscriber`, `IQuoteLinkedReadContract`, `IReviewLinkedReadContract`, `IVerificationLinkedReadContract`, `ICompanyAccountQuery`, `SupportTicketDomainEvents` | Unblocks specs 011 / 013 / 020 / 021 / 022 / 025 to author their PRs in parallel |
| E. Customer slices — opening + reading | OpenTicket → ListMyTickets → GetMyTicket → UploadAttachment | FR-006–FR-008, FR-012, FR-040 |
| F. Customer slices — replying + reopen | ReplyAsCustomer → ReopenTicket → OpenRedactionRequestTicket | FR-009, FR-013, FR-029, FR-011a |
| G. Customer slices — convert-to-return | ConvertToReturnRequest (idempotent invocation) | FR-028 |
| H. Agent queue + claim | ListAgentQueue → ClaimTicket → GetTicketAdminDetail | FR-015–FR-017 |
| I. Agent reply + transitions | ReplyAsAgent (assigned-only) → AddInternalNote → TransitionToResolved → RetagCategory | FR-007, FR-014, FR-014a, FR-018 |
| J. Lead actions | ReassignTicket → ForceCloseTicket → OverrideSlaTargets → ToggleAgentAvailability | FR-018, FR-020, FR-026, FR-019a |
| K. PolicyAdmin slices | UpdateSlaPolicy → UpdateMarketSchema → ListSlaPolicies | FR-021, FR-022, P5 |
| L. SuperAdmin redaction slices | RedactAttachment → RedactMessage → ListRedactionRequestQueue | FR-011a, FR-012a |
| M. Subscribers (cross-module event consumers) | ReturnOutcomeHandler, CustomerAccountLifecycleHandler | FR-028 reverse-linkage, account-lifecycle auto-close |
| N. Workers (reconciliation + SLA + reclaim) | SlaBreachWatchWorker (60 s), AutoCloseResolutionWindowWorker (hourly), OrphanedAssignmentReclaimWorker (nightly) | FR-024–FR-025, FR-001 auto-close, FR-edge-cases reclaim |
| O. Authorization wiring | `SupportPermissions.cs` constants + `[RequirePermission]` attributes; spec 015 wires role bindings on its PR | Permission boundary |
| P. Domain events + 025 contract | Publish 16 events on each lifecycle / breach / redaction transition; subscribed by spec 025 (lands on 025's PR, not here) | FR-034, FR-035 |
| Q. Contracts + OpenAPI | Regenerate `openapi.support.json`; assert contract test suite green; document every reason code | Guardrail #2 |
| R. AR/EN editorial | All system-generated strings ICU-keyed; AR strings flagged in `AR_EDITORIAL_REVIEW.md` | P4 |
| S. `support-v1` dev seeder | `SupportV1DevSeeder` — synthetic tickets spanning all 5 states × 10 categories; SLA-breach + return-conversion + redaction-request examples | SC-009, FR seeder requirement |
| T. Integration / DoD | Full Testcontainers run; SLA-breach idempotency test (SC-006); claim-race test (SC-007); conversion-idempotency test (SC-010); queue-perf test (SC-011); subscriber tests; fingerprint; DoD checklist; audit-coverage script | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Net-new `Modules/Support/` module rather than co-locating with `Orders` or `Identity` | Tickets carry their own state machine, RBAC, audit, SLA + breach workers, and 6 cross-module subscribers / read contracts — none of which belong in orders or identity. | Co-location would force orders or identity to take a hard dependency on support logic and break the modular-monolith boundary. |
| Single 5-state lifecycle (`open → in_progress ↔ waiting_customer → resolved → closed`) with reopen edge | Compile-time guarantee that no transition path is silently legal; reopen edge modelled as an explicit transition (`resolved → in_progress`) rather than a generic state-rewind. Mirror of the pattern in specs 020 / 021 / 022 / 007-b. | A two-state `open \| closed` plus orthogonal flags loses transition-guard expressiveness; auditors can't tell if a ticket is currently waiting on the customer vs the agent vs neither. |
| 9 net-new tables (vs. trying to fit messages + assignments + breach events on the ticket row) | Append-only operational data (messages, assignments, breach events, links) demands separate tables for trigger-guarded immutability + queryability. | A single mutable JSONB on the ticket row destroys append-only guarantees and is hard to query for "tickets where agent X breached SLA in March". |
| Frozen-at-creation SLA snapshot (`first_response_target_minutes_snapshot`, `resolution_target_minutes_snapshot`) on each ticket row | Policy edits during a ticket's lifetime would otherwise retroactively change "was it breached?". Frozen snapshot makes breach evaluation deterministic and reproducible during audit. | Live-policy-lookup means "was Mr X's ticket breached?" depends on when you check, not on what was true when the customer reported. |
| `SlaBreachWatchWorker` writes append-only `TicketSlaBreachEvent` rows AND stamps `breach_acknowledged_at_*` on the ticket row | Idempotency on `(ticket_id, breach_kind)` requires a non-null acknowledgment marker; the breach-event row is the audit-faithful record; the ticket-row stamp is the worker's "I already handled this" flag. | Acknowledgment-on-event-row alone makes the worker tick scan the entire breach-events table; ticket-row alone loses the audit-faithful append-only history. |
| Polymorphic `TicketLink` rows (no DB-level FK to cross-module entities) | Cross-module read pattern from specs 020 / 021 / 022 — declaring an FK to the `orders` schema from `support` would couple the modules at the database level and prevent horizontal extraction in Phase 2. | DB-level FK forces cross-module DDL coordination and breaks loose-coupling. |
| Idempotent return-conversion via `Idempotency-Key` (FR-028) | A network-flaky double-tap on **Convert to return** must not create two return-requests; the spec 013 creation contract is invoked with the same key on retry. | Without idempotency, a network retry duplicates the return entity and confuses the customer. |
| Customer message-body redaction routed through a dedicated `category=redaction_request` ticket | Clarification Q5 — keeps the redaction path narrow and auditable; prevents abuse of self-service body edits as a history-rewriting tool. | Self-service body edits are a privacy-quick-fix but invite gaming the audit trail. |
| Attachment redaction is a `super_admin`-only direct action; message-body redaction requires the customer-initiated request flow | Asymmetric access reflects that attachments are commonly redacted during ordinary moderation (lewd photos, accidental PII screenshots), while body redaction is a customer-rights flow. | Symmetric access either over-empowers `support.lead`s or under-empowers `super_admin` for routine moderation. |
| Linked-entity-driven `market_code` resolution with `400 support.ticket.market_code_unresolvable` failure on contract-unavailability | Clarification Q3 — silent fallback to customer-of-record on a linked-entity ticket would misroute the SLA + queue pool. Explicit failure beats wrong routing. | Silent fallback creates an EG queue handling KSA quotes — wrong on-call agent, wrong SLA, wrong language preference for proxy users. |
| Non-assigned `support.agent` can post internal notes BUT NOT customer-visible replies | Clarification Q4 — preserves customer-visible accountability while keeping peer collaboration on internal context fluid. | Strict assigned-only blocks healthy peer review of internal investigation notes; fluid blurs accountability for customer-visible actions. |
| Minimal `SupportAgentAvailability.is_on_call` boolean (no shift windows) at V1 | Clarification Q1 — matches conservative `auto_assignment_enabled=false` default; full shift planner is a Phase 1.5 surface; avoids over-engineering V1. | A full shift entity at V1 demands shift-planning UI + automatic on-call expiry + recurring-schedule UX — none of which has a launch deadline. |
| `OrphanedAssignmentReclaimWorker` runs nightly (not real-time) | Real-time reclaim on agent offboard would race with in-flight assignment writes. Nightly batch is operationally bounded and auditable. | Real-time creates a thundering-herd reassignment when an agent shift ends. |
| `redaction_request` is a 10th category (FR-007) routed to a `super_admin`-only queue (bypasses FR-019 auto-assignment) | Customer-initiated PII removal must NEVER appear in the agent queue (PII-leakage risk: agents seeing "I posted my passport"); a dedicated super-admin queue keeps the path narrow. | Routing redaction-request tickets through the standard queue exposes agents to PII-laden requests and blurs the privacy-ops boundary. |
| Customer-supplied content stored in a single `locale`; no machine translation | P4 — Arabic editorial quality must be human-grade; auto-translation is forbidden. | Auto-translation seeds the queue with low-quality AR/EN cross-fills, defeating editorial-grade goals. |
| `vendor_id` slot reserved on every ticket row but never populated in V1 | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2. Same pattern as specs 020 / 021 / 022 / 007-b. | Omitting forces a migration of every ticket row when vendor-scoped triage queues land in Phase 2. |
| `ICustomerAccountLifecycleSubscriber` reused from spec 020, NOT re-declared | Same lifecycle event; second declaration would create a duplicate subscription on spec 020's bus. | Duplicating the interface forces re-implementation across 020 / 021 / 022 / 023 — pointless. |
