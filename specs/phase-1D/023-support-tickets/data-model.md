# Phase 1 Data Model: Support Tickets

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-28
**Schema name**: `support` (net-new; lives in the existing Postgres database in Azure Saudi Arabia Central per ADR-010)

---

## §1 · Schema overview

9 net-new tables grouped by responsibility:

| Group | Tables |
|---|---|
| Lifecycle entity | `support.tickets` |
| Append-only message thread | `support.ticket_messages`, `support.ticket_attachments` |
| Append-only operational history | `support.ticket_links`, `support.ticket_assignments`, `support.ticket_sla_breach_events` |
| Per-market policy | `support.sla_policies`, `support.support_market_schemas` |
| Agent availability (V1 minimal) | `support.agent_availability` |

Audit entries live in the existing `audit_log_entries` table owned by spec 003 (one row per state-transition / report / SLA-event / redaction; FR-030 + FR-031). No `support.*` table mirrors the audit log.

---

## §2 · ERD (text-rendered)

```text
                             ┌──────────────────────────────────┐
                             │      support.tickets             │  (lifecycled root)
                             │  PK: id                          │
                             │  FK→ identity.customers          │
                             │  linked_entity_kind/id (poly)    │
                             │  market_code, locale             │
                             │  priority, state                 │
                             │  vendor_id (P6 reserved)         │
                             │  company_id (P9 B2B scope)       │
                             │  SLA snapshot columns            │
                             │  reopen_count, reopened_at       │
                             │  row_version (xmin)              │
                             └─────────────┬────────────────────┘
                                           │ 1
            ┌──────────────────────────────┼─────────────────────┬───────────────────────┐
            │                              │                     │                       │
       ┌────▼─────────┐         ┌──────────▼─────────┐    ┌──────▼─────────┐    ┌────────▼─────────────────┐
       │  ticket_     │         │   ticket_          │    │  ticket_       │    │  ticket_sla_breach_      │
       │  messages    │         │   assignments      │    │  links         │    │  events                  │
       │  (append)    │         │   (append)         │    │  (append, poly)│    │  (append)                │
       │  kind:       │         │   active row =     │    │  to: order/    │    │  PK: (ticket_id,         │
       │   reply/note │         │    superseded_at   │    │   return/quote │    │       breach_kind)       │
       │   /system    │         │    IS NULL         │    │   /review/     │    │  emitted=true after      │
       └─────┬────────┘         └────────────────────┘    │   verification │    │   first detection        │
             │                                             └────────────────┘    └──────────────────────────┘
             │ 1 → N
        ┌────▼──────────┐
        │  ticket_      │
        │  attachments  │
        │  (append +    │
        │   redaction   │
        │   tombstone)  │
        └───────────────┘

  Per-market policy (no FK to tickets — looked up by market_code):
  ┌──────────────────────────────┐    ┌──────────────────────────────┐    ┌──────────────────────────────┐
  │  support.sla_policies        │    │  support.support_market_     │    │  support.agent_availability  │
  │  PK: (market_code, priority) │    │  schemas                     │    │  PK: (agent_id, market_code) │
  │  first_response_target_min   │    │  PK: market_code             │    │  is_on_call (bool)           │
  │  resolution_target_min       │    │  auto_assignment_enabled     │    │                              │
  │                              │    │  reopen_window_days          │    │                              │
  └──────────────────────────────┘    │  max_reopen_count            │    └──────────────────────────────┘
                                      │  auto_close_after_resolved_  │
                                      │   days, attachment caps      │
                                      └──────────────────────────────┘
```

---

## §3 · Lifecycle state machine (`TicketState`)

Five states. Reopen is an explicit edge from `resolved` back to `in_progress`, scoped to the per-market reopen window + cap.

```text
                  customer_submission
                          │
                          ▼
                       ┌──────┐
                  ┌────│ open │
                  │    └──┬───┘
                  │       │ agent_claim │ agent_assignment │ auto_assignment
                  │       ▼
                  │  ┌──────────────┐                     ┌──────────────────┐
                  │  │ in_progress  │ ◄── customer_reply ─│ waiting_customer │
                  │  │              │ ── agent_reply(WC)─►│                  │
                  │  └──┬───────────┘                     └──────────────────┘
                  │     │
                  │     │ agent_resolve (assigned-only)
                  │     ▼
                  │  ┌──────────┐  customer_reopen (within window + cap)
                  │  │ resolved │ ───────────────────────────────────────► in_progress
                  │  └──┬───────┘
                  │     │ auto_close_resolution_window  │  return_outcome  │  lead_force_close
                  │     ▼
                  │  ┌────────┐  (terminal — no reopen)
                  └─►│ closed │
                     └────────┘

   Force-close from any non-closed state by support.lead / super_admin → closed
   Account-locked from spec 004 → all tickets by author auto-close → closed
```

Encoded in `TicketStateMachine.cs` with compile-time transition guards. Every transition writes an audit row with `triggered_by ∈ TicketTriggerKind`:

```text
TicketTriggerKind:
  customer_submission       → open (initial)
  agent_claim               → open → in_progress
  agent_assignment          → open → in_progress (auto / lead-assignment)
  customer_reply            → waiting_customer → in_progress
  agent_reply               → in_progress → waiting_customer (when agent asks for info)
  agent_resolve             → in_progress → resolved
  customer_reopen           → resolved → in_progress
  auto_close_resolution_window → resolved → closed (worker)
  return_outcome            → in_progress / waiting_customer → resolved (013 event)
  author_account_locked     → * → closed (004 event)
  lead_force_close          → * → closed
  lead_reassign             → metadata (no state change; assignment row update)
  agent_offboarded          → metadata (assignment reclaim)
```

---

## §4 · Table definitions

### `support.tickets` (lifecycled root)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `id` | uuid | NO | PK |
| `customer_id` | uuid | NO | FK → `identity.customers.id` (spec 004); ownership |
| `company_id` | uuid | YES | resolved via `ICompanyAccountQuery` at submission for B2B; null for B2C |
| `market_code` | text | NO | FR-006a — inherited from linked entity if present, else customer-of-record |
| `locale` | text | NO | `'ar' \| 'en'` — customer's authoring locale |
| `category` | text | NO | one of 10 fixed values; see `TicketCategory` enum |
| `priority` | text | NO | `'low' \| 'normal' \| 'high' \| 'urgent'` |
| `state` | text | NO | one of 5 lifecycle values |
| `subject` | text | NO | max 150 chars |
| `body` | text | NO | max 8000 chars |
| `linked_entity_kind` | text | YES | `'order' \| 'order_line' \| 'return_request' \| 'quote' \| 'review' \| 'verification'`; null for standalone |
| `linked_entity_id` | uuid | YES | nullable when no link |
| `vendor_id` | uuid | YES | P6 multi-vendor-ready; from linked entity if available |
| `assigned_agent_id` | uuid | YES | denormalized cache of the active `ticket_assignments` row's `agent_id`; null when in queue |
| `first_response_target_minutes_snapshot` | int | NO | frozen at creation (FR-022) |
| `resolution_target_minutes_snapshot` | int | NO | frozen at creation |
| `first_response_due_utc` | timestamptz | NO | computed at creation; recomputed on reopen / lead override |
| `resolution_due_utc` | timestamptz | NO | as above |
| `breach_acknowledged_at_first_response` | timestamptz | YES | stamped by `SlaBreachWatchWorker` |
| `breach_acknowledged_at_resolution` | timestamptz | YES | as above |
| `reopen_count` | int | NO | default 0; capped by `max_reopen_count` |
| `reopened_at_utc` | timestamptz | YES | last reopen time |
| `resolved_at_utc` | timestamptz | YES | most recent transition into `resolved` |
| `closed_at_utc` | timestamptz | YES | terminal |
| `created_at_utc` | timestamptz | NO | default `now()` |
| `updated_at_utc` | timestamptz | NO | bumped on every state-or-content change |
| `row_version` | xmin (system) | NO | `IsRowVersion()` for optimistic concurrency |

**Indexes**:
- `idx_tickets_queue` on `(market_code, state, first_response_due_utc, created_at_utc)` — queue listing.
- `idx_tickets_customer` on `(customer_id, created_at_utc DESC)` — customer's My-Tickets view.
- `idx_tickets_assigned_agent` on `(assigned_agent_id, state)` filtered to `state IN ('in_progress', 'waiting_customer')` — agent workload.
- `idx_tickets_linked_entity` on `(linked_entity_kind, linked_entity_id)` — cross-module back-reference.
- `idx_tickets_vendor` on `(vendor_id)` — P6 forward-compat.
- `idx_tickets_company` on `(company_id, state)` — B2B account-manager scoped reads.
- `idx_tickets_breach_scan` on `(state, first_response_due_utc, resolution_due_utc)` filtered to `state NOT IN ('closed')` — `SlaBreachWatchWorker` scan path.

### `support.ticket_messages` (append-only)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `id` | uuid | NO | PK |
| `ticket_id` | uuid | NO | FK → `tickets.id` |
| `kind` | text | NO | `'customer_reply' \| 'agent_reply' \| 'internal_note' \| 'system_event'`; immutable |
| `actor_id` | uuid | YES | null for `system_event` |
| `actor_role` | text | NO | enum captured at write |
| `body` | text | YES | nullable when redacted (FR-011a) or system_event with ICU-key only |
| `body_locale` | text | YES | follows ticket.locale at write; null for system_event |
| `lead_intervention` | bool | NO | default false; true iff `actor_role IN ('support.lead', 'super_admin')` AND not the assigned agent (FR-014a) |
| `redacted_at_utc` | timestamptz | YES | non-null iff redacted (FR-011a) |
| `redacted_by_role` | text | YES | `'super_admin'` |
| `originating_redaction_request_ticket_id` | uuid | YES | FR-011a — link to redaction-request ticket |
| `created_at_utc` | timestamptz | NO | default `now()` |

**Indexes**:
- `idx_messages_ticket` on `(ticket_id, created_at_utc)` — thread render.
- `idx_messages_ticket_kind` on `(ticket_id, kind)` — customer-facing read excludes `internal_note`.

**Append-only trigger**: `BEFORE UPDATE OR DELETE` rejects updates EXCEPT `state` / `body` / `redacted_*` columns when `current_setting('app.actor_role') = 'super_admin'` AND only those columns are changing (redaction exception).

### `support.ticket_attachments` (append-only + redaction tombstone)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `id` | uuid | NO | PK |
| `message_id` | uuid | NO | FK → `ticket_messages.id` |
| `ticket_id` | uuid | NO | denormalized for fast queue render |
| `storage_object_id` | text | YES | spec 015 storage abstraction id; nullable when redacted |
| `mime_type` | text | NO | one of allowed list |
| `size_bytes` | bigint | NO | per-attachment cap enforced in handler |
| `original_filename` | text | NO | sanitized |
| `state` | text | NO | `'active' \| 'redacted'` |
| `redacted_at_utc` | timestamptz | YES | non-null iff redacted (FR-012a) |
| `redacted_by_role` | text | YES | `'super_admin'` |
| `redaction_reason_note` | text | YES | ≥ 20 chars when redacted |
| `created_at_utc` | timestamptz | NO | default `now()` |

**Append-only trigger**: same redaction-exception pattern as `ticket_messages`.

### `support.ticket_links` (append-only, polymorphic)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `id` | uuid | NO | PK |
| `ticket_id` | uuid | NO | FK → `tickets.id` |
| `kind` | text | NO | `'order' \| 'order_line' \| 'return_request' \| 'quote' \| 'review' \| 'verification'` |
| `linked_entity_id` | uuid | NO | the cross-module entity id (no DB-level FK — loose-coupling) |
| `created_via` | text | NO | `'submission' \| 'conversion' \| 'lead_link'` |
| `idempotency_key` | text | YES | FR-028 — captured for forensic traceability on conversion |
| `created_at_utc` | timestamptz | NO | default `now()` |

**Indexes**:
- `idx_links_ticket` on `(ticket_id)`.
- `idx_links_lookup` on `(kind, linked_entity_id)` — back-traversal for spec 013 `return.completed` event handling.

### `support.ticket_assignments` (append-only)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `id` | uuid | NO | PK |
| `ticket_id` | uuid | NO | FK |
| `agent_id` | uuid | NO | FK → `identity.users.id` (the agent) |
| `assignment_kind` | text | NO | `'self_claim' \| 'auto_assignment' \| 'lead_reassignment' \| 'reclaim_after_offboard'` |
| `assigned_by_actor_id` | uuid | YES | null when `kind=auto_assignment` |
| `justification_note` | text | YES | required for `lead_reassignment` (≥ 10 chars) |
| `assigned_at_utc` | timestamptz | NO | default `now()` |
| `superseded_at_utc` | timestamptz | YES | non-null when this assignment was replaced |
| `superseded_reason` | text | YES | `'reassigned' \| 'reclaimed_offboard' \| 'reopened_back_to_queue'` |

**Active row constraint**: a partial unique index `(ticket_id) WHERE superseded_at_utc IS NULL` ensures at most one active assignment per ticket. The denormalized `tickets.assigned_agent_id` mirrors the active row.

### `support.ticket_sla_breach_events` (append-only)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `ticket_id` | uuid | NO | PK part 1 |
| `breach_kind` | text | NO | PK part 2: `'first_response' \| 'resolution'` |
| `detected_at_utc` | timestamptz | NO | when worker detected |
| `target_due_utc` | timestamptz | NO | the snapshot value at detection |
| `event_emitted` | bool | NO | true after `ticket.sla_breached.*` was published |
| `superseded_by_event_id` | uuid | YES | for re-breach after reopen, points to the new event row |

**Reopen handling**: on reopen, the relevant breach acknowledgment is cleared on the ticket row. If a fresh breach occurs, a new event row is inserted with a fresh `(ticket_id, breach_kind)` after the prior row's `superseded_by_event_id` is set. To allow this, the PK is `(ticket_id, breach_kind, detected_at_utc)` rather than `(ticket_id, breach_kind)` — this gives natural reopen-tolerance while keeping idempotency at the worker tick level (the worker's idempotency check is `WHERE ticket_id=X AND breach_kind=Y AND superseded_by_event_id IS NULL AND detected_at_utc > tickets.reopened_at_utc`).

### `support.sla_policies` (per market + priority)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `market_code` | text | NO | PK part 1 |
| `priority` | text | NO | PK part 2 |
| `first_response_target_minutes` | int | NO | min 1 |
| `resolution_target_minutes` | int | NO | min 1; `> first_response_target_minutes` |
| `updated_at_utc` | timestamptz | NO | bumped on policy edit |
| `updated_by_actor_id` | uuid | YES | last editor |

**Seeded** by `SupportReferenceDataSeeder` for KSA + EG with the FR-021 defaults (8 rows total).

### `support.support_market_schemas`

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `market_code` | text | NO | PK |
| `auto_assignment_enabled` | bool | NO | default false (V1) |
| `reopen_window_days` | int | NO | default 14; range 0–60; 0 disables reopen |
| `max_reopen_count` | int | NO | default 3; range 1–10 |
| `auto_close_after_resolved_days` | int | NO | default 7; range 0–30; 0 disables auto-close |
| `attachment_max_per_ticket` | int | NO | default 10 |
| `attachment_max_size_mb` | int | NO | default 10 |
| `attachment_cumulative_max_mb` | int | NO | default 50 |
| `allowed_mime_types` | text[] | NO | default `['image/png','image/jpeg','image/webp','application/pdf','video/mp4']` |
| `updated_at_utc` | timestamptz | NO | |
| `updated_by_actor_id` | uuid | YES | |

### `support.agent_availability` (V1 minimal)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `agent_id` | uuid | NO | PK part 1 |
| `market_code` | text | NO | PK part 2 |
| `is_on_call` | bool | NO | default false |
| `last_toggled_at_utc` | timestamptz | NO | default `now()` |
| `last_toggled_by_actor_id` | uuid | YES | last lead who toggled |

---

## §5 · Audit-event taxonomy (18 kinds)

Every entry below produces a row in `audit_log_entries` (spec 003) with `module='support'` and the discriminator below as `kind`.

| Audit kind | Triggered by | Captured fields |
|---|---|---|
| `support.ticket.opened` | customer creation | `ticket_id, customer_id, market_code, category, priority, linked_entity_kind/id` |
| `support.ticket.state_transitioned` | every state change | `ticket_id, from_state, to_state, triggered_by, reason_note?, actor_id, actor_role` |
| `support.ticket.assigned` | claim, auto-assign, reassign, reclaim | `ticket_id, agent_id, assignment_kind, assigned_by_actor_id, justification_note?` |
| `support.ticket.unassigned` | reopen-back-to-queue, reclaim | `ticket_id, prior_agent_id, superseded_reason` |
| `support.ticket.replied` | customer reply, agent reply, lead intervention | `ticket_id, message_id, kind, actor_id, body_length, attachment_count, lead_intervention` |
| `support.ticket.internal_note_added` | non-customer-facing note | `ticket_id, message_id, actor_id, note_length, attachment_count` |
| `support.ticket.system_event_appended` | system-generated message (return outcome, breach summary) | `ticket_id, message_id, event_kind` |
| `support.ticket.sla_breached` | `SlaBreachWatchWorker` | `ticket_id, breach_kind, target_due_utc, detected_at_utc` |
| `support.ticket.sla_overridden` | lead override | `ticket_id, prior_first_target, new_first_target, prior_resolution_target, new_resolution_target, justification_note` |
| `support.ticket.reopened` | customer reopen | `ticket_id, reopen_count, prior_resolved_at` |
| `support.ticket.force_closed` | lead force-close | `ticket_id, reason_note` |
| `support.ticket.auto_closed` | resolution-window worker | `ticket_id, resolution_window_days` |
| `support.ticket.converted_to_return` | conversion endpoint | `ticket_id, return_request_id, idempotency_key, originating_actor_id` |
| `support.ticket.return_outcome_received` | spec 013 event | `ticket_id, return_request_id, outcome (completed/rejected)` |
| `support.ticket.category_retagged` | agent retag | `ticket_id, prior_category, new_category, actor_id` |
| `support.ticket.attachment.redacted` | super_admin redact attachment | `ticket_id, attachment_id, reason_note, requesting_actor_id` |
| `support.ticket.message.redacted` | super_admin redact body | `ticket_id, message_id, reason_note, originating_redaction_request_ticket_id, requesting_customer_id` |
| `support.policy.updated` | SLA / market schema / availability edit | `policy_kind ('sla' \| 'market_schema' \| 'agent_availability'), key (market_code/priority/agent_id), prior_jsonb, new_jsonb, actor_id` |

---

## §6 · Domain events (16 emitted; consumed by spec 025)

| Event | Trigger | Payload |
|---|---|---|
| `ticket.opened` | creation success | `{ ticket_id, customer_id, market_code, category, priority }` |
| `ticket.assigned` | claim, auto-assign, reassign | `{ ticket_id, agent_id, assignment_kind }` |
| `ticket.reassigned` | lead reassign | `{ ticket_id, prior_agent_id, new_agent_id, justification_note }` |
| `ticket.customer_reply_received` | customer posts reply | `{ ticket_id, message_id }` |
| `ticket.agent_reply_sent` | agent posts customer-visible reply | `{ ticket_id, message_id, lead_intervention }` |
| `ticket.state_changed` | any state transition (catch-all) | `{ ticket_id, from_state, to_state, triggered_by }` |
| `ticket.resolved` | transition into resolved | `{ ticket_id, resolved_at_utc, resolved_by_agent_id }` |
| `ticket.closed` | transition into closed (any path) | `{ ticket_id, closed_at_utc, triggered_by }` |
| `ticket.reopened` | customer reopen | `{ ticket_id, reopen_count }` |
| `ticket.sla_breached.first_response` | breach worker | `{ ticket_id, target_due_utc, agent_id? }` |
| `ticket.sla_breached.resolution` | breach worker | as above |
| `ticket.converted_to_return` | conversion endpoint | `{ ticket_id, return_request_id }` |
| `ticket.return_outcome_received` | spec 013 event arrival | `{ ticket_id, return_request_id, outcome }` |
| `ticket.attachment.redacted` | FR-012a | `{ ticket_id, attachment_id, requesting_actor_id }` |
| `ticket.message.redacted` | FR-011a | `{ ticket_id, message_id, requesting_customer_id }` |
| `ticket.agent_availability_changed` | lead toggles `is_on_call` | `{ agent_id, market_code, is_on_call }` |

All events are `INotification` records on the existing in-process MediatR bus, declared in `Modules/Shared/SupportTicketDomainEvents.cs`. Spec 025's notification module subscribes once (post-launch); 023's V1 only emits.

---

## §7 · Cross-module read interfaces (declared in `Modules/Shared/`)

| Interface | Implementing spec | Purpose |
|---|---|---|
| `IOrderLinkedReadContract` | 011 | order + order_line read for ownership + market resolution |
| `IReturnLinkedReadContract` | 013 | return-request read for ownership + status display |
| `IReturnRequestCreationContract` | 013 | idempotent creation invocation (FR-028) |
| `IReturnOutcomeSubscriber` | 013 (publisher) → 023 (subscriber) | `return.completed` / `return.rejected` events |
| `IQuoteLinkedReadContract` | 021 | quote read for ownership / B2B-buyer authorization + market |
| `IReviewLinkedReadContract` | 022 | review read for `review_dispute` ticket category |
| `IVerificationLinkedReadContract` | 020 | verification submission read for `verification_query` |
| `ICompanyAccountQuery` | 021 | resolves `company_id` from `customer_id` for B2B scoping |
| `IReviewDisplayHandleQuery` | 022 (declared) → 023 (consumer) | reused for FR-016a canonical display rule |
| `ICustomerAccountLifecycleSubscriber` | 020 (declared) → 023 (consumer) | account-locked / account-deleted |

**Stub policy**: any contract whose owning spec is not yet at DoD on `main` is shipped as a fake in `Modules/Shared/Testing/` so 023's tests run independently. Production binding happens at the owning spec's PR.

---

## §8 · Validation rules (handler-level)

| Rule | FR | Where enforced |
|---|---|---|
| Subject ≤ 150 chars; body ≤ 8000 chars | FR-006 | OpenTicket / ReplyAsCustomer FluentValidation |
| Category ∈ 10 fixed values | FR-007 | enum validation |
| Priority ∈ {low, normal} for customer-side; {high, urgent} require lead override | FR-006 | OpenTicket; lead-only attribute on priority-elevation endpoint |
| Linked entity kind consistent with category | FR-007 | OpenTicket validator + RetagCategory validator |
| Linked entity ownership | FR-008 | per-kind `IXxxLinkedReadContract.IsOwnedByActor` |
| Reopen window: `now() - resolved_at_utc ≤ reopen_window_days` AND `reopen_count < max_reopen_count` AND `reopen_window_days > 0` | FR-029 | ReopenTicket validator |
| Idempotency-Key required on creation, reply, conversion, reopen | FR-040 | spec 003 platform middleware |
| Customer rate-limits: 5 / hour creation, 30 / hour reply per ticket | FR-010 | spec 015 rate-limit middleware |
| Agent rate-limits: 30 / minute claim, 30 / hour reassign, 10 / hour SLA override | FR-039 | as above |
| Message kind immutable | FR-011 | trigger + handler check |
| Attachment MIME / size / count caps | FR-012 + market schema | UploadAttachment handler |
| Internal note role-gate on read | FR-014 | adapter strips `internal_note` rows on customer-facing reads |
| Reply-and-transition assignment check | FR-014a | handler-level authorization |
| Force-close requires reason ≥ 10 chars | FR-020 | ForceCloseTicket validator |
| SLA override requires justification ≥ 10 chars | FR-026 | OverrideSlaTargets validator |
| Reassign requires justification ≥ 10 chars | FR-018 | ReassignTicket validator |
| Redaction requires reason ≥ 20 chars | FR-011a + FR-012a | redaction handlers |
| Hard-delete forbidden | FR-005a | DELETE route returns 405 unconditionally |

---

## §9 · Migration strategy

Single net-new EF Core migration `20260428_001_AddSupportSchema` creates:

1. The `support` schema.
2. All 9 tables with constraints + indexes.
3. The append-only triggers (with controlled redaction exception).
4. The seed-data insertions for KSA + EG market schemas + 8 SLA-policy rows.

Subsequent migrations are additive only (new columns, new indexes). No table rename is anticipated; the schema is V1-stable.

---

## §10 · Capacity assumptions

- 500 tickets / day across both markets at steady state.
- Peak 30 concurrent agents on the queue; 5 SLA-breach events / hour.
- 10 000 open + in-progress tickets resident in the queue at peak (catastrophic incident scenario).
- Index-walk costs verified at 10× steady-state by the SC-011 perf test on the queue list endpoint.
