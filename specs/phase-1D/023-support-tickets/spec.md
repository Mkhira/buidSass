# Feature Specification: Support Tickets

**Feature Branch**: `phase_1D_creating_specs` (working) · target merge branch: `023-support-tickets`
**Created**: 2026-04-28
**Status**: Draft
**Constitution**: v1.0.0
**Phase**: 1D (Business Modules · Milestone 7)
**Depends on**: 011 `orders` at DoD (order + order-line lookup, delivery state); 015 `admin-foundation` contract merged to `main` (admin shell + RBAC + audit panel + idempotency middleware + storage abstraction)
**Soft-couples to**: 013 `returns-and-refunds` (ticket ↔ return-request bidirectional conversion); 020 `verification` (verification-query tickets); 021 `quotes-and-b2b` (quote-related tickets, B2B company-account hierarchy); 022 `reviews-moderation` (review-dispute escalations originate here); 019 `admin-customers` (customer profile lookups + display-handle reuse); 025 `notifications` (customer reply notifications + SLA-breach alerts + agent assignment digests); 027 `payments-integration` (payment-failure tickets in Phase 1E)
**Consumed by**: customer storefront (spec 014 — "Help" surface); admin dashboard (spec 015 — agent queue + ticket detail screens); spec 022 (review dispute landing); spec 013 (return-request creation handshake)
**Input**: User description: "Phase 1D, spec 023 — support tickets. Ticket entity (subject, body, category, priority, linked order/return/quote). Customer ticket creation + list + detail + reply. Admin queue with filters, assignment, status transitions. SLA timers per priority; breach alerting. File attachments via storage abstraction. Internal notes (not customer-visible). Conversion between ticket and return/refund request where applicable. support-v1 seeder spanning categories, priorities, SLA states. Bilingual AR + EN end-to-end; multi-vendor-ready."

## Clarifications

### Session 2026-04-28

- Q: Agent shift / availability source for auto-assignment (FR-019) — full shift schedule, deferred, or minimal toggle? → A: **Minimal `is_on_call` boolean per `(agent_id, market_code)` in a new `SupportAgentAvailability` row, toggled by `support.lead` via a dedicated endpoint.** No shift-window timestamps or planner UI in V1; an agent is either on-call or off-call per market. The auto-assignment query (FR-019) joins on this table when `auto_assignment_enabled=true`. A full shift planner (start/end windows, recurring schedules, automatic on-call expiry) is deferred to Phase 1.5. The lead-toggle endpoint is rate-limited and audited.
- Q: Attachment retention / right-to-erasure policy — indefinite, auto-purge, or super-admin redact? → A: **Indefinite retention by default; `super_admin` MAY redact a specific attachment via a dedicated endpoint that replaces the underlying storage object with a tombstone marker while preserving the `TicketMessage` row, the `TicketAttachment` metadata row, and the audit trail intact.** The redaction itself is audited (`actor_id`, `attachment_id`, `ticket_id`, `reason_note ≥ 20 chars`, `timestamp_utc`) and emits a `ticket.attachment.redacted` event. After redaction, customer-facing and agent-facing reads of that attachment return a `redacted` placeholder with the redaction timestamp + redactor role (NOT actor identity, to protect privacy operators). The original storage object is permanently irrecoverable from the application — backup-restore-and-redact procedures live in the privacy-ops runbook owned by spec 028. Redaction is `super_admin`-only at V1; no `support.lead` redaction path. Ticket rows themselves remain hard-delete-forbidden per FR-005a.
- Q: Reply authorization rule for non-assigned `support.agent`s — fluid, strict, or notes-only? → A: **Non-assigned `support.agent`s MAY read the ticket and post `internal_note` messages on it; customer-visible replies (`agent_reply`) and state transitions (`in_progress ↔ waiting_customer`, `→ resolved`) MUST be performed by either the currently-assigned agent OR a `support.lead` / `super_admin`.** A non-assigned agent attempting a reply or transition MUST receive `403 support.ticket.action_requires_assignment`. This preserves customer-visible accountability while keeping peer collaboration on internal context fluid. A `support.lead` who replies on a non-assigned ticket MUST do so via the existing reassign-then-reply path (with required justification) OR via an explicit `lead_intervention=true` flag on the reply endpoint that records the lead's intervention without changing assignment; the intervention is audited.
- Q: Customer-side PII removal on immutable messages — strict, self-edit, or super-admin redact-on-request? → A: **Strict immutability stays the default for message bodies; but a customer MAY initiate a redaction request via a special ticket of `category=redaction_request` linking to the original ticket + the specific message id(s) they need redacted.** The redaction-request ticket auto-routes to a `super_admin`-only triage queue (NOT the standard agent queue) and bypasses the auto-assignment policy. A `super_admin` who upholds the request MAY redact the message body — replacing it with a tombstone marker carrying the redaction `timestamp_utc` + redactor `role` (NOT actor identity); the original body is preserved only in encrypted audit-only storage owned by spec 028 and is irrecoverable from the application. The redaction is audited (`actor_id`, `ticket_id`, `message_id`, `reason_note ≥ 20 chars`, `requesting_customer_id`) and emits `ticket.message.redacted`. The category `redaction_request` is added to the FR-007 fixed list (10 categories total). No 5-minute self-edit grace window; no self-service body edits. Attachment redaction continues to follow FR-012a (also `super_admin`-only, but does not require a redaction-request ticket — `super_admin` may redact attachments directly during ordinary moderation).
- Q: B2B cross-market ticket `market_code` resolution — customer's market-of-record, linked entity, or admin pick? → A: **`market_code` is inherited from the linked entity when one is present** (`order.market_code`, `order_line.market_code`, `return_request.market_code`, `quote.market_code`, `review.market_code`, `verification.market_code`, accessed via the existing per-module read contracts in `Modules/Shared/`). For standalone tickets (no linked entity, including `category=other` and `category=account_query`), the system falls back to the customer's market-of-record. The resolved `market_code` is computed at submission time and snapshotted onto the ticket row; subsequent edits to the linked entity's market (rare; cross-market migrations) MUST NOT retroactively change the ticket's market scope. The linked entity's market resolution failure (e.g., the linked entity's read contract returns `linked_entity_unavailable`) MUST fail submission with `400 support.ticket.market_code_unresolvable` rather than silently fall back to customer-of-record — explicit failure beats wrong routing.

---

## Primary outcomes

1. Every signed-in customer can open a support ticket against any of their orders, order lines, returns, quotes, reviews, verification submissions — or as a standalone account-level question — with subject + body + optional attachments, see the ticket transition through clear states, exchange replies with a support agent, and reach a resolution they can rate.
2. Support agents work a single, filterable, market-aware, RBAC-gated queue surfacing every open ticket; can claim or be auto-assigned; reply to customers; record internal notes invisible to customers; transition status with audit; and trust that SLA breach is surfaced before it impacts service.
3. Tickets carry SLA targets driven by priority + market policy; breach detection runs continuously; breach events trigger lead-level reassignment and notifications; SLA performance is reportable for operations review.
4. Bidirectional conversion between a support ticket and a `returns-and-refunds` (013) request preserves the customer's narrative and attachments while letting agents hand off the formal refund flow without copy-paste.
5. Tickets that originate from a review dispute (spec 022 auto-hide) carry the originating review reference end-to-end so a `reviews.moderator` can re-evaluate with the customer's voice in hand.
6. The data model, market configuration, vendor scoping, and admin role boundaries are designed so that future multi-vendor expansion (Phase 2) can layer vendor-scoped triage queues on top without rewriting the ticket schema or its state machine.
7. The customer-facing surface and the admin agent surface are bilingual AR + EN end-to-end (Principle 4), with editorial-grade ICU keys for every system message and full RTL support in the agent queue.

---

## Roles and actors

| Role | Permission origin | What they can do in 023 |
|---|---|---|
| `customer` (signed-in) | spec 004 | Open a ticket; list / read their own tickets; post a customer reply; upload attachments; mark "info provided"; reopen a resolved ticket within the reopen window; convert their own ticket into a return-request handoff (spec 013) when category is eligible. |
| `customer` (unsigned / browsing) | none | Cannot open or view tickets; the "Help" surface presents read-only FAQ (spec 024) only. |
| `support.agent` | new in this spec | Open the agent queue; claim a ticket; receive auto-assigned tickets; reply to customers; add internal notes; transition `open → in_progress → waiting_customer ↔ in_progress → resolved`; convert a ticket into a return-request (spec 013) on the customer's behalf with consent recorded. |
| `support.lead` | new in this spec | All `support.agent` powers plus: reassign tickets across agents; force-close abandoned tickets; override SLA on a ticket with required justification; edit per-market SLA policy (response + resolution targets per priority); manage agent shift / availability list. |
| `super_admin` | spec 015 | Implicit superset of all of the above. |
| `viewer.finance` | spec 015 | Read-only on tickets that link to a payment / refund / order line — for dispute investigation. Cannot reply or transition. |
| `reviews.moderator` (spec 022) | spec 022 | Read-only on tickets whose category is `review_dispute` or that link to a review row — for cross-context reading during a moderation re-decision. Cannot reply or transition. |
| `b2b.account_manager` (spec 021) | spec 021 | Read-only on tickets whose `company_id` they own — for company-level relationship visibility. Cannot reply or transition (replies remain a `support.*` role responsibility). |

The customer-facing surface is owned by Phase 1C spec 014 (mobile + web storefront); 023 ships only the backend contracts. The admin agent queue UI is owned by spec 015 + this spec's contract; 015 builds the screens against 023's endpoints.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A customer opens a ticket about a delivered order line and exchanges replies with an agent (Priority: P1)

A customer in KSA-AR receives a delivered order line with a damaged unit. They open the order detail screen, tap **Get help**, pick category `order_issue`, type a bilingual subject + body in Arabic, attach two photos via the storage abstraction (spec 015), and submit. The ticket persists in state `open` with `priority=normal` (default). The auto-assignment routine queues it to an available `support.agent` for the customer's market. The agent claims it, replies in Arabic asking for the order line's expiry sticker photo, and transitions to `waiting_customer`. The customer uploads the photo and replies; state flips back to `in_progress`. The agent issues a refund-instruction reply and transitions to `resolved`. The customer either confirms (state → `closed` after the auto-close window) or reopens within the reopen window if unsatisfied.

**Why this priority**: Customer-initiated support is the constitutional core of Principle 17 / 21 (operational readiness) and the most-frequent customer flow in this spec.

**Independent Test**: Sign in as a customer with a delivered order line; open a ticket with attachments; verify the row exists in `open`; sign in as `support.agent`; claim and reply; verify state machine transitions and audit rows.

**Acceptance Scenarios**:

1. *Given* a signed-in customer, *when* they submit a ticket with subject + body + 2 attachments + category `order_issue` + a linked `order_line_id`, *then* the ticket persists in `open`, attachments are uploaded via the spec 015 signed-URL flow, and the response payload echoes the ticket id + state + assigned agent (or `null` if the queue is empty).
2. *Given* an unsigned visitor attempts to open a ticket, *then* the request returns `401 support.ticket.unauthenticated`.
3. *Given* a customer attempts to open a ticket linked to an order they do not own, *then* the request rejects with `403 support.ticket.linked_entity_not_owned`.
4. *Given* a `support.agent` claims an `open` ticket, *then* the state transitions to `in_progress`, the assignment row is written, and the audit log captures `actor_id`, `from_state=open`, `to_state=in_progress`, `triggered_by=agent_claim`.
5. *Given* an agent replies and sets `state=waiting_customer`, *when* the customer posts a reply, *then* the ticket auto-transitions back to `in_progress` and an audit row captures `triggered_by=customer_reply`.
6. *Given* an agent transitions a ticket to `resolved`, *when* `auto_close_after_resolved_days` (default 7, per-market) elapses without a customer reply, *then* the system auto-transitions to `closed` with `triggered_by=auto_close_resolution_window`.

---

### User Story 2 — A `support.agent` works the queue, filters by market and priority, and claims a ticket (Priority: P1)

A `support.agent` signs in to the admin shell, opens the support queue, filters by `market_code=SA` and `priority IN (high, urgent)`, sees three open + two in-progress tickets, sorts by oldest first-response-due, and claims the topmost ticket. The claim write is optimistic-concurrency-guarded (xmin) so two agents cannot claim the same ticket. The queue badge for that agent decrements; the ticket detail loads with the customer's full reply history, internal-note history, attachments, and the linked-entity preview (order, return, quote, review, or verification).

**Why this priority**: The agent queue is the operational heart of the spec. Without it, agents cannot triage at scale.

**Independent Test**: Seed 30 tickets across 5 categories + 4 priorities + 2 markets; sign in as `support.agent`; apply each filter combination; verify the result counts match expectations; claim a ticket and verify only one agent's claim succeeds under concurrent-claim race.

**Acceptance Scenarios**:

1. *Given* the queue endpoint with filter `market_code=SA AND priority=urgent`, *then* the response includes only tickets matching both clauses.
2. *Given* two agents attempt to claim the same `open` ticket concurrently, *when* both writes hit the database, *then* exactly one claim succeeds; the loser receives `409 support.ticket.assignment_conflict` with the current assignment surfaced for re-decision.
3. *Given* a `support.agent` without `support.agent` permission attempts to claim, *then* the request returns `403 support.ticket.queue_forbidden`.
4. *Given* a queue read with no filters, *then* the response is paged at 50 per page, capped at 200, sorted by `first_response_due_utc ASC` then `created_at ASC` by default.
5. *Given* a `support.lead` reassigns a ticket from agent A to agent B, *then* a new assignment row is written, the prior assignment row is `superseded_at` stamped, and an audit row captures the reassignment with the lead's `actor_id` + required justification.

---

### User Story 3 — A customer converts a ticket into a return-request and the spec 013 flow takes over (Priority: P1)

A customer's ticket about a damaged item reaches a state where the agent confirms the case is a return. The agent (or the customer themselves, when category is eligible) clicks **Convert to return request**. The system invokes the spec 013 return-request creation contract, passing `customer_id`, `order_line_id`, `narrative` (the ticket's first body), `attachment_ids[]`, `originating_ticket_id`. Spec 013 returns the new return-request id; 023 writes a bidirectional `TicketLink` row of type `return_request`, transitions the ticket to `waiting_customer` (the customer is now driving the formal return flow in 013) and emits a `ticket.converted_to_return` event for spec 025. When 013 emits `return.completed` or `return.rejected`, 023 receives the event and transitions the originating ticket to `resolved` with `triggered_by=return_outcome` and a synthetic agent reply summarizing the outcome.

**Why this priority**: Without this conversion, customers and agents face copy-paste between two domains for the most-common ticket category. P1.

**Independent Test**: Open a ticket with category `return_refund_request` and a linked order line; trigger the conversion; verify spec 013 receives the contract call (use a fake at module boundary); verify the bidirectional link exists; emit a `return.completed` event from the fake and verify the ticket auto-transitions to `resolved`.

**Acceptance Scenarios**:

1. *Given* a ticket in `in_progress` with category `return_refund_request` and a linked `order_line_id`, *when* the conversion endpoint is called, *then* the spec 013 return-request creation contract is invoked, a bidirectional `TicketLink` row is persisted, and the ticket state transitions to `waiting_customer`.
2. *Given* a ticket in category `order_issue` (not return-eligible) attempts conversion, *then* the request rejects with `400 support.ticket.conversion_category_not_eligible`.
3. *Given* the spec 013 contract call fails with a transient error, *when* the conversion endpoint retries idempotently (same `Idempotency-Key`), *then* the second call succeeds and produces only one return-request + one `TicketLink` row.
4. *Given* a return-request linked to a ticket reaches `return.completed` in spec 013, *when* 023 receives the event, *then* the originating ticket auto-transitions to `resolved` with a synthetic agent reply summarizing the outcome.
5. *Given* a customer attempts conversion on someone else's ticket, *then* the request rejects with `403 support.ticket.conversion_forbidden`.

---

### User Story 4 — An SLA breach triggers a notification + lead-level reassignment opportunity (Priority: P2)

A high-priority ticket sits in `open` past its `first_response_due_utc` (e.g., 1 hour for KSA-priority `high`). The `SlaBreachWatchWorker` runs every 60 s and detects the breach. It writes a `TicketSlaBreachEvent` row, emits `ticket.sla_breached.first_response`, and the spec 025 subscriber notifies the assigned agent (if any) plus the on-shift `support.lead`. The lead reassigns the ticket to a free agent, attaching a justification note. The reassignment is audited; the ticket's `breach_acknowledged_at` is stamped to suppress duplicate alerts on the same breach. A second breach (resolution due) re-triggers the same flow with a different event key.

**Why this priority**: SLA breach handling is the operational difference between a launch-grade and a demo-grade support module. P2 because the basic queue + reply flow can ship before breach automation, but breach automation MUST be in V1 per Principle 21.

**Independent Test**: Seed an `open` ticket with a backdated `first_response_due_utc`; advance the worker tick; verify the `TicketSlaBreachEvent` row, the emitted event, and that re-running the worker on the same backdated ticket within the same hour does NOT create duplicate breach events.

**Acceptance Scenarios**:

1. *Given* an `open` ticket with `first_response_due_utc < now()` and no `breach_acknowledged_at`, *when* the SLA breach worker runs, *then* a `TicketSlaBreachEvent` row is created, `ticket.sla_breached.first_response` is emitted, and `breach_acknowledged_at` is stamped.
2. *Given* the same ticket on the next worker tick, *then* a duplicate breach event is NOT created (idempotent on `(ticket_id, breach_kind)`).
3. *Given* a `support.lead` reassigns the breached ticket, *when* the reassignment endpoint requires a justification < 10 chars, *then* the request rejects with `support.ticket.reassign_justification_required`.
4. *Given* a resolution-deadline breach occurs after a first-response breach, *then* a second `TicketSlaBreachEvent` row is created with `breach_kind=resolution_due` (distinct from the first-response breach key).
5. *Given* a `support.lead` overrides SLA on a ticket with valid justification, *then* the SLA targets are recomputed, the override is audited, and subsequent breach detection uses the overridden targets.

---

### User Story 5 — Internal notes vs customer-visible replies (Priority: P2)

A `support.agent` working a ticket needs to record an investigation finding ("contacted warehouse manager, lot 442 confirmed defective batch") that the customer must NOT see. They post an internal note via the dedicated endpoint. The note persists with `kind=internal_note`, `actor_role=support.agent`, and is excluded from every customer-facing read. The customer's ticket-detail call returns only `reply` + `system_event` messages, never `internal_note`. A `viewer.finance` reading the same ticket sees the internal note (read-only on internal context). Audit captures every internal-note creation.

**Why this priority**: Without internal notes, agents are forced to leak investigation context to the customer or work in a separate tool, violating Principle 25 (audit + traceability). P2 because the basic reply flow can ship before internal notes.

**Independent Test**: Post one internal note + one customer reply on the same ticket; call the customer-facing detail endpoint and verify only the reply is returned; call the agent-facing detail endpoint and verify both are returned with their `kind` flags intact.

**Acceptance Scenarios**:

1. *Given* a `support.agent` posts an internal note, *when* the customer calls their ticket-detail endpoint, *then* the response excludes the internal note entirely (no leakage of body, attachments, or agent identity).
2. *Given* a `support.agent` posts an internal note, *when* the agent calls the admin ticket-detail endpoint, *then* the response includes the note with `kind=internal_note` and the actor's display name.
3. *Given* a customer attempts to call any endpoint that exposes internal notes, *then* the request returns `403 support.ticket.internal_note_forbidden`.
4. *Given* an internal note is created, *then* an audit row captures `actor_id`, `actor_role`, `ticket_id`, `note_length`, `attachment_count`.
5. *Given* a `support.agent` attempts to mark a customer-visible reply as internal-note retroactively, *then* the operation rejects with `400 support.ticket.message_kind_immutable`.

---

### User Story 6 — A customer reopens a resolved ticket within the reopen window (Priority: P2)

A customer's ticket resolved 5 days ago. The customer realises the agent's instructions did not solve the underlying problem and taps **Reopen** on the ticket detail screen. The reopen window default is 14 days from `resolved_at` (per-market, range 0–60 days; `0` disables reopen). The system transitions the ticket from `resolved` to `in_progress`, stamps `reopened_at`, increments `reopen_count`, recomputes SLA targets (resolution-due is reset; first-response-due is `now() + first_response_target` since the agent's prior reply is no longer authoritative), routes the ticket back to the original agent (if still on shift) or returns it to the queue, writes an audit row with `triggered_by=customer_reopen`, and emits `ticket.reopened`. After the reopen window closes, the **Reopen** action returns `400 support.ticket.reopen_window_closed`; the customer must open a new ticket linked to the prior one.

**Why this priority**: Reopens prevent the operational anti-pattern of agents prematurely closing tickets and customers giving up. P2 because the basic resolution flow can ship before reopen is exposed.

**Independent Test**: Resolve a ticket; reopen within window; verify state transition + SLA reset + assignment routing. Then attempt reopen after window; verify rejection.

**Acceptance Scenarios**:

1. *Given* a `resolved` ticket within the reopen window, *when* the customer calls the reopen endpoint, *then* the state transitions to `in_progress`, `reopen_count` increments, SLA targets recompute, and the audit row carries `triggered_by=customer_reopen`.
2. *Given* a `closed` ticket, *when* the customer attempts to reopen, *then* the request rejects with `400 support.ticket.closed_terminal`.
3. *Given* a `resolved` ticket past the reopen window, *then* the request rejects with `400 support.ticket.reopen_window_closed`.
4. *Given* the per-market reopen window is set to `0`, *then* the reopen endpoint always returns `400 support.ticket.reopen_disabled_for_market` regardless of resolved age.
5. *Given* a ticket has been reopened 3 times (per-market `max_reopen_count`, default 3), *when* the customer attempts a 4th reopen, *then* the request rejects with `400 support.ticket.reopen_count_exceeded`.

---

### User Story 7 — `support-v1` seeder for staging and local development (Priority: P3)

A developer or QA engineer runs the seeder. It creates: 10 `open` tickets across all 10 categories + 4 priorities; 5 `in_progress` tickets with one + multi-message conversations + internal notes; 4 `waiting_customer` tickets; 6 `resolved` tickets (3 within reopen window, 3 past); 3 `closed` tickets; 2 tickets with active SLA breaches (`first_response` + `resolution`); 2 tickets converted to return-requests; 2 tickets linked to spec 022 review-dispute escalations. Each ticket is tied to a synthetic customer + a linked entity (order, return, quote, review, verification) where the category implies one. Bilingual editorial-grade AR/EN labels (Principle 4). Per-market SLA policies seeded for KSA + EG.

**Why this priority**: Without realistic seed data the queue, SLA breach worker, and conversion flow cannot be exercised end-to-end in staging or local. P3 because manual ticket submission via the customer test client is also possible (just less efficient).

**Independent Test**: `seed --dataset=support-v1 --mode=apply` against a fresh staging DB; verify per-state distribution; verify per-market SLA policy rows; verify the 2 SLA-breach examples surface in the queue's "breached" filter.

**Acceptance Scenarios**:

1. *Given* a fresh staging DB, *when* the seeder runs, *then* it produces ≥ 1 row in each of `open`, `in_progress`, `waiting_customer`, `resolved`, `closed` states across all 10 categories.
2. *Given* the seeder runs twice on the same DB, *then* it is idempotent (no duplicate tickets).
3. *Given* the seeder runs with `--mode=dry-run`, *then* it exits 0 with a planned-changes report and writes nothing.
4. *Given* the seeder fails partway, *then* the partial transaction is rolled back.
5. *Given* an admin opens any seeded ticket, *then* AR + EN system messages render correctly with no machine-translated artifacts in the system-generated copy.

---

### Edge Cases

- A customer opens a ticket against an `order_line_id` they own but the order has since been hard-deleted by support / admin (rare): the linked-entity preview returns `linked_entity_unavailable`; the ticket remains workable on its own narrative; agent UI shows a "linked entity removed" badge.
- An agent replies on a ticket and at the same moment the customer reopens after a brief `resolved` window: optimistic-concurrency (xmin row_version) — one of the writes loses with `409 support.ticket.version_conflict` and retries against the new state.
- A customer attaches a file that exceeds the per-attachment size limit (default 10 MB) or per-ticket-attachment count (default 10): the spec 015 storage abstraction returns the upload error; 023 surfaces it as `400 support.ticket.attachment_too_large` or `support.ticket.attachment_count_exceeded` and persists nothing.
- A customer's account is locked / deleted in spec 004: their open tickets transition to `closed` with `triggered_by=author_account_locked` and `reason_note='auto_closed:author_account_locked'`; a `support.lead` may reopen manually if dispute investigation requires it.
- A `support.agent` is offboarded: their assigned in-flight tickets are reassigned by the nightly `OrphanedAssignmentReclaimWorker` to the queue under the on-shift lead's purview; an audit row captures the reclaim with `triggered_by=agent_offboarded`.
- A ticket linked to a return-request reaches `return.completed` AFTER the originating ticket was already `closed` (e.g., customer closed early): the originating ticket remains `closed` (terminal); a system reply with the return outcome is appended for record-keeping; no state transition happens.
- A customer opens 100 tickets in 1 hour (abuse): rate-limit returns `429 support.ticket.creation_rate_exceeded` after the per-customer-per-hour cap (default 5).
- A `support.lead` overrides SLA targets to longer values mid-flight: the breach worker on the next tick uses the new targets; previously-recorded breach events remain valid (history is not rewritten).
- A SLA policy is edited per-market mid-flight (`super_admin` action via spec 015 settings): in-flight tickets retain the targets they were created with (frozen-at-creation snapshot stored on the ticket row); only newly-created tickets adopt the new policy.
- A `support.lead` issues a hard-delete via the API: rejected with `405 support.ticket.row.delete_forbidden` (FR-005a-style preservation). The only "deletion" path is `closed` (terminal soft state).
- A ticket has both an SLA-breach alert AND a customer reply within the same minute: both are processed; the customer reply transitions the state from `waiting_customer → in_progress` regardless of breach status; the breach event is preserved.
- A B2B customer (spec 021 company account) opens a ticket — the ticket carries the resolved `company_id` so the `b2b.account_manager` for that company can read-only read it; per-company-monthly ticket volume is reportable but not capped at V1.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Lifecycle and state model (Principle 24)

- **FR-001**: Tickets MUST share a five-state lifecycle: `open` (initial) → `in_progress` (agent claimed or assigned) ↔ `waiting_customer` (agent reply requested customer info) → `resolved` (agent decided complete) → `closed` (terminal). The `resolved → in_progress` reopen transition is allowed within the per-market reopen window; the `closed → *` transition is forbidden.
- **FR-002**: Every state transition MUST write an audit row with `actor_id` (or `'system'`), `actor_role`, `timestamp_utc`, `from_state`, `to_state`, `reason_note?`, `triggered_by` (one of `customer_submission`, `agent_claim`, `agent_assignment`, `customer_reply`, `agent_reply`, `agent_resolve`, `customer_reopen`, `auto_close_resolution_window`, `return_outcome`, `author_account_locked`, `lead_force_close`, `lead_reassign`, `agent_offboarded`).
- **FR-003**: Decisions to `resolved` MUST require an agent reply (or a system-generated reply on conversion-outcome path); decisions to `lead_force_close` MUST require a `reason_note ≥ 10 chars`; SLA overrides MUST require a `justification ≥ 10 chars`.
- **FR-004**: Only `support.lead` and `super_admin` MAY force-close (`closed` directly from any non-terminal state); only `support.lead` and `super_admin` MAY reassign across agents; only `support.agent`, `support.lead`, and `super_admin` MAY reply or transition. The API MUST enforce these at the handler layer with the corresponding `403` reason codes.
- **FR-005**: A `closed` ticket MUST be terminal and read-only for messages and state; reopen from `closed` is forbidden (`400 support.ticket.closed_terminal`); historical reads (audit, support-investigation) remain possible.
- **FR-005a**: Tickets MUST NEVER be hard-deleted from the operational store. `closed` is a soft terminal state; the row is retained indefinitely so audit, dispute, and refund-trace queries remain resolvable. Any `DELETE` API call against a ticket MUST return `405 support.ticket.row.delete_forbidden`.

#### Customer ticket creation

- **FR-006**: Ticket creation MUST capture: `subject` (max 150 chars; required; stored under the customer's authoring `locale`), `body` (max 8000 chars; required), `category` (one of ten fixed values, see FR-007), `priority` (`low|normal|high|urgent`; defaults to `normal`; customers may select `low` or `normal` only — `high` / `urgent` are admin-only and require lead-level override on customer requests), `linked_entity_kind?` + `linked_entity_id?` (one of `order|order_line|return_request|quote|review|verification`; nullable for standalone account tickets), `attachment_ids?` (max 10; pre-uploaded via spec 015 storage abstraction), `market_code` (resolved per FR-006a), `locale` (`ar` or `en`).
- **FR-006a**: The ticket's `market_code` MUST be resolved at submission time as follows: (a) if `linked_entity_kind` is non-null, read the linked entity's `market_code` via the corresponding per-kind read contract (`IOrderLinkedReadContract` for `order`/`order_line`, `IReturnLinkedReadContract` for `return_request`, `IQuoteLinkedReadContract` for `quote`, `IReviewLinkedReadContract` for `review`, `IVerificationLinkedReadContract` for `verification`; all declared in `Modules/Shared/` and implemented by specs 011 / 013 / 021 / 022 / 020 respectively); (b) if no linked entity, fall back to the submitting customer's market-of-record (spec 004). The resolved `market_code` MUST be snapshotted onto the ticket row and MUST NOT change retroactively if the linked entity's market is later migrated. If the linked-entity read contract returns `linked_entity_unavailable` at submission time, the request MUST reject with `400 support.ticket.market_code_unresolvable`; silent fallback to customer-of-record on a linked-entity ticket is forbidden because it would misroute SLA + queue work (e.g., an EG buyer's ticket about a KSA quote MUST land in the KSA queue, not EG).
- **FR-007**: The fixed category list MUST be: `order_issue`, `payment_issue`, `product_question`, `return_refund_request`, `verification_query`, `account_query`, `review_dispute`, `quote_b2b`, `redaction_request`, `other` (10 categories total). Each category resolves to bilingual ICU keys for display. The category MAY be re-tagged by an agent at any time (audited) but the linked-entity kind MUST remain consistent with the new category. The `redaction_request` category is reserved for customer-initiated PII removal flows (see FR-011a); its ticket auto-routes to a `super_admin`-only triage queue and bypasses the auto-assignment policy of FR-019.
- **FR-008**: Linked-entity ownership MUST be enforced at submission and at every reply: a customer MUST own (or be authorised B2B-buyer-or-approver of, per spec 021) the linked order / return / quote / review / verification. Mismatches return `403 support.ticket.linked_entity_not_owned`.
- **FR-009**: Customers MUST be able to add a customer-reply on any ticket they authored when state ∈ {`open`, `in_progress`, `waiting_customer`}; replies on `resolved` or `closed` tickets MUST go through the reopen flow (FR-013).
- **FR-010**: Customer-side ticket creation MUST be rate-limited per `customer_id`: 5 ticket creations / hour, 20 / day (overridable per environment). Reply rate-limit is 30 / hour / actor on a single ticket. Over-limit returns `429 support.ticket.creation_rate_exceeded` or `support.ticket.reply_rate_exceeded`.

#### Replies, attachments, and message types

- **FR-011**: Every ticket MUST persist messages with `kind ∈ {customer_reply, agent_reply, internal_note, system_event}`, `actor_id`, `actor_role`, `body` (max 8000 chars; `system_event` MAY be empty when serialized via ICU key), `attachment_ids[]`, `created_at_utc`, immutable after creation.
- **FR-011a**: Customer-initiated message-body redaction MUST flow through the dedicated `category=redaction_request` ticket type. A customer creates a redaction-request ticket linking the original ticket + specific `message_id(s)` they want redacted, and providing a justification (≥ 20 chars). The redaction-request ticket auto-routes to a `super_admin`-only triage queue (it MUST NOT enter the standard agent queue and MUST NOT be auto-assigned per FR-019). A `super_admin` who upholds the request MAY redact the targeted message body via a dedicated `POST /admin/support-tickets/{ticket_id}/messages/{message_id}/redact` endpoint with `reason_note ≥ 20 chars` referencing the redaction-request ticket id. The body is replaced with a tombstone marker carrying `redacted_at_utc` + `redactor_role` (NOT actor identity) on read; the original body is preserved only in encrypted audit-only storage owned by spec 028 and MUST NOT be re-derivable from the application. The redaction MUST be audited (`actor_id`, `ticket_id`, `message_id`, `reason_note`, `requesting_customer_id`, `originating_redaction_request_ticket_id`) and MUST emit a `ticket.message.redacted` event. Self-service body edits are NOT supported; attachment redaction continues to follow FR-012a (also `super_admin`-only and does not require a redaction-request ticket, since attachment redaction may be initiated during ordinary moderation as well).
- **FR-012**: Attachments MUST upload via the existing spec 015 storage abstraction (signed-URL flow); 023 stores only the `attachment_id` array on the message row. Per-attachment max 10 MB; per-ticket cumulative max 50 MB; supported MIME types: image/png, image/jpeg, image/webp, application/pdf, video/mp4 (configurable per market). Disallowed types return `400 support.ticket.attachment_mime_forbidden`.
- **FR-012a**: Attachments MUST be retained indefinitely after a ticket reaches `closed`; auto-purge based on age is NOT performed. A `super_admin` MAY redact a specific attachment via a dedicated `POST /admin/support-tickets/{ticket_id}/attachments/{attachment_id}/redact` endpoint with `reason_note ≥ 20 chars`; the underlying storage object is replaced with a tombstone marker. The `TicketMessage` row and the `TicketAttachment` metadata row MUST remain intact (FR-005a-style preservation). The redaction MUST be audited and MUST emit a `ticket.attachment.redacted` event. After redaction, read endpoints (customer + agent) MUST return a `redacted` placeholder carrying the redaction `timestamp_utc` + redactor `role` (NOT actor identity); the original storage object MUST NOT be re-derivable from the application. Redaction is `super_admin`-only at V1; `support.lead` and `support.agent` MUST NOT have a redaction path. Backup-restore implications and operator-side erasure SLAs live in the privacy-ops runbook owned by spec 028.
- **FR-013**: A customer-side reply on a ticket in `resolved` within the reopen window MUST trigger a reopen (FR-029); on a `closed` ticket the request MUST reject with `400 support.ticket.closed_terminal`.
- **FR-014**: Internal notes MUST be visible only to roles `support.agent | support.lead | super_admin | viewer.finance | reviews.moderator (when ticket category is review_dispute)`. Customer-facing read endpoints MUST exclude `internal_note` rows entirely. The message `kind` is immutable after creation (FR-005a-style — no kind retroconversion).
- **FR-014a**: Reply-and-transition authorization MUST distinguish assigned-agent actions from non-assigned-agent actions. A non-assigned `support.agent` (any agent in the ticket's market who is NOT the currently-active `TicketAssignment`) MAY read the ticket AND post `internal_note` messages on it; they MUST NOT post `agent_reply` (customer-visible) messages and MUST NOT transition the state. Attempts return `403 support.ticket.action_requires_assignment`. A `support.lead` who needs to intervene without taking over assignment MAY post a customer-visible reply with an explicit `lead_intervention=true` flag on the reply endpoint; the intervention is audited (`actor_role=support.lead`, `lead_intervention=true`) but does NOT auto-claim the ticket. The currently-assigned agent retains accountability for the ticket until reassignment per FR-018.

#### Agent queue, assignment, and triage

- **FR-015**: The admin support queue MUST surface every ticket in `open`, `in_progress`, `waiting_customer`, or `resolved` (within reopen window) state, with filters by `market_code`, `category`, `priority`, `state`, `assigned_agent_id`, `sla_breach_status` (`none|first_response_breached|resolution_breached|both`), `linked_entity_kind`, `created_at_range`. Default page size 50, max 200.
- **FR-016**: A queue item MUST display: ticket id, subject (truncated), authoring `locale`, customer display (using FR-016a), `category`, `priority`, `state`, `assigned_agent` (or "Unassigned"), `first_response_due_utc` + `resolution_due_utc` (with badge when breached), `linked_entity_preview` (read from the owning module via cross-module read contract), `attachment_count`, `last_message_actor_role`, `last_message_at_utc`.
- **FR-016a**: The customer-display rule on the agent queue and ticket detail MUST reuse the spec 022 FR-016a canonical rule: `review_display_handle` if present on the customer profile (spec 019); otherwise `first_name` + first character of `last_name` + `.` (e.g., `"Mohamed K."`). For B2B customers (spec 021), the company name MUST appear as a secondary line.
- **FR-017**: A `support.agent` MUST be able to claim an `open` ticket via the claim endpoint; the claim is optimistic-concurrency-guarded (xmin row_version). Concurrent claims return `409 support.ticket.assignment_conflict`. A claim writes a `TicketAssignment` row and transitions state to `in_progress`.
- **FR-018**: A `support.lead` MUST be able to reassign a ticket from agent A to agent B with a required `justification ≥ 10 chars`. The prior assignment row is `superseded_at` stamped (preserved for audit); the new assignment row is active. An audit row captures actor + justification.
- **FR-019**: Auto-assignment MUST be available as a per-market policy: when enabled, a newly-created `open` ticket is assigned to the `support.agent` whose `SupportAgentAvailability` row is `is_on_call=true` for that `market_code` AND who has the lowest open + in_progress count. Tie-breaks by oldest assignment time. When no on-call agent exists for the market at submission time, the ticket sits in `open` until manually claimed (no fallback to off-call agents). When disabled (default at V1), tickets sit in `open` until manually claimed regardless.
- **FR-019a**: A `SupportAgentAvailability` row keyed `(agent_id, market_code)` MUST hold `is_on_call` (boolean), `last_toggled_at_utc`, `last_toggled_by_actor_id`. Only `support.lead` and `super_admin` MAY toggle the flag via a dedicated endpoint; toggles are audited and rate-limited (60 toggles / hour / lead). No shift-window timestamps, recurring schedules, or planner UI ship in V1; that surface is deferred to Phase 1.5.
- **FR-020**: A `support.lead` MUST be able to force-close a ticket from any non-`closed` state with `reason_note ≥ 10 chars`. Force-close transitions the ticket directly to `closed` (skipping `resolved`), recorded with `triggered_by=lead_force_close`.

#### SLA timers, breach detection, and escalation

- **FR-021**: A `TicketSlaPolicy` table MUST hold per-market + per-priority entries with `first_response_target_minutes`, `resolution_target_minutes`. Defaults at V1:
  - `urgent`: first-response 30 min; resolution 4 h.
  - `high`: first-response 1 h; resolution 8 h.
  - `normal`: first-response 4 h; resolution 24 h.
  - `low`: first-response 8 h; resolution 72 h.
  Targets are tunable by `support.lead` and `super_admin` per market via the policy admin endpoint; edits are audited.
- **FR-022**: On creation, a ticket MUST snapshot the active `(market_code, priority)` SLA targets onto the ticket row as `first_response_target_minutes_snapshot` + `resolution_target_minutes_snapshot` and compute `first_response_due_utc` + `resolution_due_utc`. Subsequent edits to the policy do NOT retroactively update in-flight tickets (frozen-at-creation).
- **FR-023**: When a ticket transitions from `open` or `waiting_customer` to `in_progress` via an agent action, the `first_response_due_utc` MUST stay in place if the action includes the agent's first agent-reply on the ticket; otherwise (claim without immediate reply) the deadline persists and breach detection continues against it.
- **FR-024**: A `SlaBreachWatchWorker` MUST run on a 60-second cadence, scanning all non-`closed` tickets where `first_response_due_utc < now()` AND `breach_acknowledged_at_first_response IS NULL`, OR `resolution_due_utc < now()` AND `breach_acknowledged_at_resolution IS NULL`. For each breach, a `TicketSlaBreachEvent` row is written, the corresponding `breach_acknowledged_at_*` is stamped, and `ticket.sla_breached.first_response` or `ticket.sla_breached.resolution` is emitted to spec 025.
- **FR-025**: SLA-breach events MUST be idempotent on `(ticket_id, breach_kind)` — a duplicate breach event MUST NOT be created on subsequent worker runs while `breach_acknowledged_at_*` is non-null. A reopen reset (FR-029) clears the relevant `breach_acknowledged_at_*` and recomputes `first_response_due_utc` / `resolution_due_utc`, allowing fresh breach detection.
- **FR-026**: A `support.lead` MUST be able to override SLA targets on a single ticket via the override endpoint with `justification ≥ 10 chars`. The override updates `first_response_target_minutes_snapshot`, `resolution_target_minutes_snapshot`, recomputes the `_due_utc` fields, and writes an audit row. Subsequent breach detection uses the overridden targets.

#### Cross-module linkage and conversion

- **FR-027**: Tickets MUST link to one of `order | order_line | return_request | quote | review | verification` via a polymorphic `linked_entity_kind` + `linked_entity_id` pair (no DB-level FK; cross-module pattern from specs 020 / 021 / 022). The agent queue MUST render a read-only preview of the linked entity by calling the owning module's read contract (`IOrderLinkedReadContract`, `IReturnLinkedReadContract`, `IQuoteLinkedReadContract`, `IReviewLinkedReadContract`, `IVerificationLinkedReadContract` — each defined in `Modules/Shared/`). Missing or unavailable previews surface as `linked_entity_unavailable` without blocking ticket workability.
- **FR-028**: A ticket with `category=return_refund_request` MAY be converted to a spec 013 return-request via the conversion endpoint (customer-initiated when the customer's role permits, agent-initiated otherwise). The conversion call invokes the spec 013 return-request creation contract idempotently (`Idempotency-Key` required), persists a bidirectional `TicketLink` row of type `return_request`, transitions the ticket to `waiting_customer`, and emits `ticket.converted_to_return`. Reverse linkage: when 013 emits `return.completed` or `return.rejected`, 023 receives the event, transitions the originating ticket to `resolved` with `triggered_by=return_outcome`, and appends a system-event message summarising the outcome.
- **FR-029**: Customers MUST be able to reopen a `resolved` ticket within `reopen_window_days` (default 14 per market; range 0–60; `0` disables reopen). Reopen transitions to `in_progress`, increments `reopen_count`, recomputes SLA targets (`first_response_due_utc = now() + first_response_target_minutes_snapshot`; `resolution_due_utc = now() + resolution_target_minutes_snapshot`), and writes an audit row. A per-market `max_reopen_count` (default 3; range 1–10) caps the total reopens per ticket.

#### Audit (Principle 25)

- **FR-030**: Every state transition, every assignment / reassignment, every SLA override, every SLA breach event, every internal note creation, every customer / agent reply, every conversion to a return-request, every category retag MUST emit an audit row to the shared audit log via the existing `IAuditEventPublisher`.
- **FR-031**: Audit rows MUST be immutable and MUST NOT be deletable from any UI; the underlying audit-log table is owned by spec 003 and append-only.

#### Bilingual + RTL (Principle 4)

- **FR-032**: Customer-supplied subject + body + replies are stored under a single `locale` per FR-006 (no bilingual authoring requirement). Customer-facing **system-generated** strings (state labels, category labels, reason codes, breach badge labels, "Original written in {locale}" annotation, system-event message bodies) MUST resolve to ICU keys in both `en` and `ar` and be rendered in the viewer's locale.
- **FR-033**: The agent queue and ticket detail MUST switch to RTL when the operator's locale is `ar`, including the message-thread direction, attachment-card layout, and badge alignment.

#### Notifications integration (Principle 19)

- **FR-034**: 023 MUST emit domain events consumed by spec 025: `ticket.opened`, `ticket.assigned`, `ticket.reassigned`, `ticket.customer_reply_received`, `ticket.agent_reply_sent`, `ticket.state_changed`, `ticket.resolved`, `ticket.closed`, `ticket.reopened`, `ticket.sla_breached.first_response`, `ticket.sla_breached.resolution`, `ticket.converted_to_return`, `ticket.return_outcome_received`, `ticket.attachment.redacted`, `ticket.message.redacted`. Spec 025 templates handle customer notification (state changes, agent replies, redaction-request outcomes) and agent / lead notification (assignment, breach).
- **FR-035**: This spec MUST NOT directly send notifications; it only emits events.
- **FR-036**: Customers MAY opt out of non-essential ticket notifications via spec 025 preference center (post-launch); essential notifications (state-change-resolved, conversion-outcome) MUST be delivered regardless of preference, mirroring spec 025's transactional-essentials rule.

#### Multi-vendor readiness (Principle 6)

- **FR-037**: Every ticket row MUST carry a `vendor_id` column (nullable in V1; populated from the linked entity's vendor when the linked entity has a vendor; otherwise null for standalone account tickets). Indexed for future-vendor-scoped reads. The admin UI MUST NOT expose vendor scoping in V1; the column is forward-compatibility only.

#### Operational safeguards

- **FR-038**: All admin endpoints MUST require authentication and the corresponding RBAC permission; lookups MUST cap result-set size at 200 with paging.
- **FR-039**: Admin actions MUST be rate-limited per `actor_id`: agent claim 30 / minute (defeats automation abuse); lead reassign 30 / hour; SLA override 10 / hour. Over-limit returns `429 support.ticket.admin_rate_limit_exceeded`.
- **FR-040**: Ticket creation, reply submission, attachment upload, and conversion endpoints MUST require an `Idempotency-Key` header (consistent with spec 015 idempotency middleware) so retried network-flaky writes do not duplicate rows.
- **FR-041**: The agent queue MUST be observable: per-market open-ticket counts, average first-response time, average resolution time, breach rate per priority MUST be exposed via a metrics endpoint readable by `super_admin` and `viewer.finance`. Detailed analytics dashboards are owned by spec 028 (Phase 1.5).

### Key Entities

- **SupportTicket** — customer-authored issue tied optionally to one linked entity (order / order_line / return / quote / review / verification). Carries lifecycle, priority, market scope, SLA snapshots, vendor_id (multi-vendor-ready).
- **TicketMessage** — append-only message on a ticket: customer reply, agent reply, internal note, or system event. Immutable kind. Bilingual locale tag on free-text bodies.
- **TicketAttachment** — link from a `TicketMessage` to a spec 015 storage object id; carries MIME, size, original filename.
- **TicketLink** — polymorphic link from a ticket to one or more cross-module entities (order, return-request, etc.). Bidirectional indexing for back-traversal.
- **TicketAssignment** — append-only assignment record: who is currently assigned, when they were assigned, who superseded them.
- **TicketSlaPolicy** — per-market + per-priority SLA targets; admin-editable; frozen-at-creation onto each ticket row.
- **TicketSlaBreachEvent** — append-only breach record: which ticket, which breach kind (first-response or resolution), when detected, whether acknowledged.
- **SupportMarketSchema** — per-market policy: `auto_assignment_enabled`, `auto_close_after_resolved_days`, `reopen_window_days`, `max_reopen_count`, `attachment_max_per_ticket`, `attachment_max_size_mb`.
- **SupportAgentAvailability** — minimal on-call toggle per `(agent_id, market_code)`; `is_on_call` boolean only at V1 (no shift windows). `support.lead` and `super_admin` toggle via dedicated endpoint; audited.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in customer with a delivered order line MUST be able to open a ticket (subject + body + 1 attachment) in under 90 seconds from order-detail tap to confirmation.
- **SC-002**: A submitted ticket MUST be queryable from the agent queue within 5 seconds of submission, p95.
- **SC-003**: 100 % of state transitions, assignments, reassignments, SLA overrides, breach events, internal-note creations, and conversion calls MUST produce a matching audit row, verified by the audit-coverage script in spec 015.
- **SC-004**: 0 % of `internal_note` messages MAY appear in any customer-facing API response, verified by a leak-detection test suite that exercises every customer-facing read endpoint with a fixture containing internal notes.
- **SC-005**: SLA breach events MUST be created within 60 seconds of the deadline being exceeded, p95, measured over a 7-day soak.
- **SC-006**: SLA breach events MUST be idempotent: under a 100-iteration repeat-worker stress test against the same backdated ticket, exactly 1 `TicketSlaBreachEvent` row per `(ticket_id, breach_kind)` MUST exist.
- **SC-007**: Concurrent agent-claim races MUST resolve with exactly one winning claim under a 100-concurrent-attempt stress test (FR-017).
- **SC-008**: AR-locale screen-render correctness (RTL, label completeness, formatting) MUST score 100 % against a representative 25-screen editorial-review checklist (Principle 4).
- **SC-009**: The `support-v1` seeder MUST populate ≥ 1 ticket in each of `open`, `in_progress`, `waiting_customer`, `resolved`, `closed` states across all 10 categories, with at least one SLA-breach example and one return-conversion example, in under 15 s on a fresh staging DB.
- **SC-010**: The ticket-to-return-request conversion MUST be idempotent on `Idempotency-Key`: a 100-iteration retry stress test MUST produce exactly 1 return-request + 1 `TicketLink` row.
- **SC-011**: The agent queue read endpoint MUST return a 50-row page in under 500 ms p95 against a seeded fixture of 10,000 open + in-progress tickets.

---

## Assumptions

- The 011 `orders` module exposes a deterministic order + order-line read contract `IOrderLinkedReadContract.Read(order_id_or_line_id) → {customer_id, summary_view, vendor_id, market_code}` that 023 consumes for ownership enforcement (FR-008) and the queue's linked-entity preview (FR-016). If the contract is not yet on `main`, 023 ships against a documented stub and integration tests assert against a fake.
- The 013 `returns-and-refunds` module exposes a return-request creation contract `IReturnRequestCreationContract.Create(...) → {return_request_id}` and emits `return.completed` + `return.rejected` events on the in-process MediatR bus; 023 subscribes via the same channel pattern used by specs 020 / 021 / 022 / 007-b.
- The 020 `verification`, 021 `quotes-and-b2b`, and 022 `reviews-moderation` modules each expose a linked-entity read contract for their respective entity kinds (`IVerificationLinkedReadContract`, `IQuoteLinkedReadContract`, `IReviewLinkedReadContract`); each is declared in `Modules/Shared/` to avoid module dependency cycles, consistent with the existing project pattern.
- The 004 `identity-and-access` module emits `customer.account_locked` and `customer.account_deleted` events on its lifecycle channel; 023 subscribes via the existing `ICustomerAccountLifecycleSubscriber` interface from spec 020.
- The 015 `admin-foundation` shell (RBAC, audit panel, idempotency middleware, rate-limit middleware, storage abstraction) is at DoD on `main` before 023 implementation begins.
- The 019 `admin-customers` customer profile schema carries the optional `review_display_handle` field introduced for spec 022 FR-016a; 023 reuses the same canonical display rule (FR-016a here). If 019 has not yet shipped the field, 023 falls back to first-name + last-initial only until 019 lands.
- The 021 `quotes-and-b2b` company-account schema exposes a `company_id` resolver from `customer_id`; 023 stores the resolved `company_id` on B2B-customer-authored tickets to enable `b2b.account_manager` read access.
- The 025 `notifications` module is implemented in Phase 1E; 023 emits domain events on the existing in-process bus from V1, and 025 subscribers are wired up at 1E DoD. Until 1E ships, customer notifications on ticket events are recorded as "would-have-sent" log entries — but the events themselves MUST emit from V1 onward to unblock the 1E subscribers without retro-fitting.
- Storage for ticket attachments uses the existing spec 015 storage abstraction (signed-URL upload + retrieval); 023 stores only the storage `attachment_id` array on the message row.
- Single-vendor at V1 (Principle 6); `vendor_id` columns on the ticket row are present and indexed but not exposed in admin UI.
- Operators sign in through the spec 015 admin shell; this spec does not introduce a new auth path.
- Currency / market resolution per market is fixed (EG → EGP, KSA → SAR); tickets carry `market_code` only (financial figures, when relevant, are read from the linked entity, not stored on the ticket).
- The default V1 SLA targets in FR-021 are conservative starting values picked for launch; per-market `support.lead` and `super_admin` are expected to tune them post-launch as ops capacity is calibrated.
- The default `auto_assignment_enabled=false` at V1 (FR-019) reflects the conservative starting position: human triage is the default while the team builds a baseline of agent shift schedules; auto-assignment can be enabled per-market once the shift table is reliable.
- Maximum file types for attachments (FR-012) are intentionally narrow at V1 to reduce malware-vector exposure; expansion is a per-market policy edit by `super_admin`, not a code change.

---

## Out of Scope

- **AI-assisted reply suggestions / drafting** — Phase 1.5+; agents reply manually at V1.
- **Customer satisfaction (CSAT) survey on resolution** — Phase 1.5 (`1.5-g` candidate); event hooks at FR-034 will support it without spec changes.
- **Ticket merging** (combining duplicate tickets from the same customer) — Phase 1.5; agents close-with-note + cross-link manually at V1.
- **Macros / canned responses for agents** — Phase 1.5; agents type freeform at V1.
- **Multi-language reply within a single ticket** (auto-translation between agent EN reply and customer AR thread) — explicitly rejected; Principle 4 forbids machine-translation. Bilingual agents handle bilingual customers manually; specialised editorial reply is human-only.
- **Public-facing ticket portal / community Q&A** — out of scope; tickets are private to the customer + their support team.
- **Live-chat / synchronous messaging** — Phase 2; tickets are async-by-design at V1.
- **Phone-call integration / call-back scheduling** — out of scope.
- **Custom ticket fields / custom forms per category** — Phase 1.5; the fixed FR-006 shape covers V1 needs.
- **Full agent shift planner** (start/end windows, recurring schedules, automatic on-call expiry, planner UI) — Phase 1.5; V1 ships only the minimal `SupportAgentAvailability.is_on_call` boolean per FR-019a.
- **Ticket-to-quote conversion** (the reverse of FR-028 for 021) — Phase 2 multi-vendor concern; agents manually reference quote ids in replies at V1.
- **External email-to-ticket ingestion** (catch-all `support@…` mailbox) — Phase 1.5; tickets are app/web-originated only at V1.
- **Vendor-scoped agent queues** — Phase 2 multi-vendor.
- **Customer reputation / trust score on ticket prioritisation** — Phase 2.
- **AI-driven category auto-tagging** — Phase 1.5; customers + agents tag manually at V1.
- **CSAT-driven agent performance dashboards** — owned by spec 028 (Phase 1.5 analytics).
