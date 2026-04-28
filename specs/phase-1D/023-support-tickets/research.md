# Phase 0 Research: Support Tickets

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-28
**Status**: Phase 0 complete — all NEEDS CLARIFICATION resolved (5 questions answered in /speckit-clarify session)

This document captures the design decisions, the rationale, and the alternatives considered for the V1 implementation of the Support Tickets module.

---

## R-01 · Linked-entity read contracts

**Decision**: Define five new interfaces under `Modules/Shared/` — one per linked entity kind (`IOrderLinkedReadContract`, `IReturnLinkedReadContract`, `IQuoteLinkedReadContract`, `IReviewLinkedReadContract`, `IVerificationLinkedReadContract`). Each implements:

```csharp
public interface IXxxLinkedReadContract
{
    Task<LinkedEntityReadResult> ReadAsync(Guid linkedEntityId, Guid actorCustomerId, CancellationToken ct);
}

public sealed record LinkedEntityReadResult(
    bool IsOwnedByActor,           // ownership check (FR-008)
    Guid? CompanyId,               // B2B scoping (spec 021); null for B2C
    string MarketCode,             // FR-006a market resolution
    string DisplayName,            // queue preview (FR-016)
    Guid? VendorId,                // FR-037 multi-vendor slot
    LinkedEntityAvailability Availability  // Available | Unavailable | Hidden
);
```

**Rationale**:
- Single contract per kind avoids overloaded "polymorphic read" generics that the .NET compiler can't enforce.
- All five contracts return the same shape so the queue list endpoint can call them uniformly.
- Ownership + company resolution + market resolution are folded into one round-trip (no chatty per-field lookup).
- The `Availability` enum lets 023 surface the `linked_entity_unavailable` indicator without conflating it with 401/403 errors.

**Alternatives rejected**:
- A single generic `ILinkedReadContract<TKind>` — looks elegant but forces module-level registration of N closed generics, which doesn't compose with MediatR's open-generic registration.
- Per-kind read endpoints over HTTP (cross-module HTTP calls) — violates the modular-monolith ADR and adds latency on the hot ticket-creation path.
- Reading directly from cross-module DbContexts — couples the schemas and breaks the loose-coupling pattern (specs 020 / 021 / 022 set the precedent of `Modules/Shared/` interfaces).

**Failure mode**: when a contract returns `Availability=Unavailable` at submission time, FR-006a mandates `400 support.ticket.market_code_unresolvable` (no silent fallback). The queue's preview endpoint surfaces `linked_entity_unavailable` after submission without blocking ticket workability.

---

## R-02 · Return-request conversion idempotency

**Decision**: The convert-to-return endpoint takes an `Idempotency-Key` header (already enforced by spec 003 platform middleware) and forwards it to spec 013's `IReturnRequestCreationContract.CreateAsync(...)`. Spec 013 dedupes on the key for 24 h. 023 records the (`ticket_id`, `idempotency_key`, `return_request_id`) tuple on the `TicketLink` row for forensic traceability.

**Rationale**:
- Network-flaky double-tap on **Convert to return** must NOT create two return-requests. Per FR-028 + SC-010, retry must produce exactly 1 return-request + 1 `TicketLink` row.
- Spec 013 already needs idempotency for its own customer-facing return flow; reusing its key dedupe avoids a parallel implementation in 023.
- Storing the idempotency key on the `TicketLink` row gives ops a forensic path: "did the link write succeed but the ticket-state-transition fail?"

**Alternatives rejected**:
- Two-phase commit — overkill for cross-module in-process MediatR; adds latency without buying anything.
- Optimistic-concurrency on a unique constraint `(ticket_id, kind=return_request)` — works for single-link case but fails when a ticket spawns multiple returns (e.g., partial-line returns over time).

**Failure mode**: if 013's contract throws after creating the return-request but before responding (rare), 023's retry will hit 013's idempotency dedupe and read back the existing `return_request_id`. The `TicketLink` is then created and the state transition proceeds — eventually consistent.

---

## R-03 · SLA snapshot freezing semantics

**Decision**: On ticket creation, copy the active `(market_code, priority)` row from `sla_policies` into two columns on the `tickets` row: `first_response_target_minutes_snapshot`, `resolution_target_minutes_snapshot`. Compute `first_response_due_utc` + `resolution_due_utc` from `created_at_utc + snapshot`. Subsequent edits to `sla_policies` MUST NOT propagate to existing tickets. Lead override (FR-026) updates the snapshots in place + writes an audit row.

**Rationale**:
- "Was Mr X's ticket breached?" must be deterministic. Live-policy-lookup makes the answer depend on when you ask.
- Auditors and dispute-investigation flows need reproducible breach reasoning.
- Mid-flight policy retighteenings (e.g., `urgent` first-response cut from 30 min to 15 min) must NOT surprise tickets that were already running against the looser target.
- Lead overrides are deliberate, audited, scoped to one ticket — these MAY change the snapshot.

**Alternatives rejected**:
- Live policy lookup on every breach scan — fast (one query per scan) but produces non-reproducible breach reasoning.
- Snapshot stored on the breach-event row instead of the ticket row — works for breach evaluation but doesn't help the queue's "due-by" badge rendering, which would still need a live lookup.

---

## R-04 · SLA breach worker idempotency

**Decision**: `SlaBreachWatchWorker` runs every 60 s holding a Postgres advisory lock (per the spec 020 pattern) so only one instance scans at a time. It selects all non-`closed` tickets where (`first_response_due_utc < now() AND breach_acknowledged_at_first_response IS NULL`) OR (`resolution_due_utc < now() AND breach_acknowledged_at_resolution IS NULL`). For each row, it writes a `TicketSlaBreachEvent` row (PK `(ticket_id, breach_kind)`) and stamps the corresponding `breach_acknowledged_at_*` on the ticket row in the same transaction.

**Rationale**:
- The PK uniqueness + transactional acknowledgment-stamp give us strong idempotency: a duplicate worker run, a duplicate tick, a process crash + restart cannot produce two events for the same `(ticket_id, breach_kind)`.
- The 60-second cadence is low overhead and meets SC-005's `≤ 60 s p95` requirement with a safety margin.
- Reopen (FR-029) clears the relevant `breach_acknowledged_at_*` so a re-breach after reopen is detected fresh — the PK still allows the prior breach event to remain in audit (we only mark `superseded_by=new_event_id` on reopen).

**Alternatives rejected**:
- Event-driven breach detection (subscribe to a "deadline-passed" timer event) — Postgres has no native deadline-event primitive; would require a sidecar scheduler service, violating the modular-monolith ADR.
- Worker reads + emits without acknowledgment-stamp — produces duplicate events on every tick until the breach is humanly cleared.
- Re-issue breach events on every reopen even if no new breach — pollutes the audit log with non-events.

---

## R-05 · Market-code resolution failure path

**Decision**: When a ticket is submitted with `linked_entity_kind` set, the handler calls the corresponding `IXxxLinkedReadContract.ReadAsync(...)`. If the result returns `Availability=Unavailable` (cross-module read failed, the entity was hard-deleted by some module's super_admin path, etc.), the request fails with `400 support.ticket.market_code_unresolvable`. The customer is shown an actionable message ("the linked order is currently unavailable; please retry or open a standalone ticket"); 023 does NOT silently fall back to the customer's market-of-record.

**Rationale** (Clarification Q3):
- An EG buyer's ticket about a KSA quote MUST land in the KSA queue (KSA on-call agents, KSA SLA, KSA-language reading queue). Silent fallback to the buyer's EG market produces wrong routing on every dimension.
- Failing fast at submission is preferable to creating a ticket that's silently misrouted and then discovered as a routing bug days later.

**Alternatives rejected**:
- Silent fallback to customer-of-record — wrong routing as detailed above.
- Customer-pick from a constrained list — adds a UX hurdle every B2B cross-market submission has to clear; trades one bad UX for another.
- Auto-derive + admin-override post-creation — the override path becomes a load-bearing operational tool; ops never wants a load-bearing override.

---

## R-06 · Agent availability minimal toggle

**Decision** (Clarification Q1): A single boolean column `is_on_call` on `support.agent_availability` per `(agent_id, market_code)`. Default value is `false` (agent off-call until explicitly toggled on). Only `support.lead` and `super_admin` can toggle. The auto-assignment query (FR-019) joins on `agent_availability` filtered to `is_on_call=true`. No shift-window timestamps, no recurring schedules, no automatic on-call expiry in V1.

**Rationale**:
- V1's `auto_assignment_enabled=false` default already makes auto-assignment opt-in per market, so the bar for shift complexity is low.
- A boolean toggle is auditable, simple, and matches the operational reality of small launch teams.
- A full shift planner needs UI + backend + recurring schedule logic + automatic expiry — none of which has a launch deadline. Phase 1.5 (`1.5-h support-agent-shift-planner` proposal) carries it without retro-fitting any 023 field.

**Alternatives rejected**:
- Full shift entity (start/end timestamps, recurrence, expiry) — over-engineered for V1; the spec has no operational requirement for it.
- Reuse a hypothetical spec 015 operator availability — spec 015 is not chartered to deliver an availability surface, so the dependency would be an unbacked promise.
- Defer auto-assignment entirely — would force V1 ops to manually claim every ticket from day 1; the boolean keeps a one-step path to opt-in.

---

## R-07 · Redaction tombstone storage

**Decision** (Clarifications Q2 + Q5):
- **Attachment redaction (FR-012a)**: `super_admin` calls `RedactAttachmentAsync(ticket_id, attachment_id, reason_note)`. The handler updates the `TicketAttachment` row to set `state=redacted`, `redacted_at_utc`, `redacted_by_role=super_admin`, then deletes the underlying spec 015 storage object (storage abstraction returns success even if the object is already gone — idempotent). The `TicketAttachment` row remains; reads return a tombstone payload.
- **Message-body redaction (FR-011a)**: customer creates a special `category=redaction_request` ticket linking to the original ticket + message_id. The redaction-request ticket auto-routes to a `super_admin`-only triage queue (the FR-019 auto-assignment policy is bypassed). A `super_admin` who upholds the request calls `RedactMessageAsync(original_ticket_id, message_id, reason_note, originating_redaction_request_ticket_id)`. The handler updates the `TicketMessage` row to set `state=redacted`, `body=null`, `redacted_at_utc`, `redacted_by_role=super_admin`. The original body is forwarded to spec 028's encrypted audit-only storage in the same transaction (out of scope for 023's tables).
- Both operations emit dedicated events (`ticket.attachment.redacted`, `ticket.message.redacted`).
- Both operations are exempt from the append-only `BEFORE UPDATE` triggers via a row-level WHEN clause checking the actor role (`current_setting('app.actor_role') = 'super_admin'`) AND the update target columns (only `state`, `body`, `redacted_at_utc`, `redacted_by_role` are mutable in the redaction path).

**Rationale**:
- Asymmetric trigger policy: append-only by default; explicit super_admin-only redaction exception. This preserves Principle 25 audit + Principle 17 post-purchase integrity while still meeting privacy-rights obligations.
- Spec 028 owns the encrypted audit-only storage (privacy-ops runbook); 023 hands off and forgets, keeping its own footprint narrow.
- The `redacted_by_role` (NOT `actor_id`) on read response protects privacy-ops staff from doxxing.

**Alternatives rejected**:
- True hard-delete of the message / attachment row — destroys audit history (FR-005a violation).
- Customer self-service body edit within a 5-minute grace window — Clarification Q5 explicitly rejected this; allows gaming the audit trail.
- Storing the original body in 023's own table under a `body_pre_redaction` column — drags 023 into encryption-key management; spec 028 is purpose-built for it.

---

## R-08 · Non-assigned-agent reply rule

**Decision** (Clarification Q4): The handler-level authorization check distinguishes (a) the currently-active `TicketAssignment.agent_id` from (b) the actor. If they match, the reply / transition succeeds. If they don't match AND the actor's role is `support.agent`, return `403 support.ticket.action_requires_assignment`. If they don't match AND the actor's role is `support.lead` or `super_admin`, allow the action; reply endpoint additionally accepts a `lead_intervention=true` flag that records the lead's intervention without changing assignment.

**Rationale**:
- Accountability for customer-visible actions (reply + state transition) stays pinned to a single person.
- Peer collaboration via internal notes is unrestricted (FR-014a explicitly allows non-assigned agents to add notes), keeping investigation flow fluid.
- A lead who needs to intervene without taking over assignment can do so explicitly (`lead_intervention=true`), audited, without forcing a reassign-then-reply two-step.

**Alternatives rejected**:
- Strict-assigned-only — blocks healthy peer review and forces unnecessary reassignment churn.
- Fully fluid (any agent, any time) — blurs accountability; "who told the customer X?" becomes ambiguous.
- Auto-claim on action — operationally noisy; one agent reviewing another's ticket detail would accidentally take over by clicking around.

---

## R-09 · Reopen window math

**Decision**: On reopen (FR-029), the handler:

1. Validates `state=resolved` AND `now() - resolved_at_utc ≤ reopen_window_days * 1 day` AND `reopen_count < max_reopen_count`.
2. Transitions state to `in_progress`.
3. Increments `reopen_count`.
4. Stamps `reopened_at_utc = now()`.
5. Recomputes `first_response_due_utc = now() + first_response_target_minutes_snapshot`.
6. Recomputes `resolution_due_utc = now() + resolution_target_minutes_snapshot`.
7. Clears `breach_acknowledged_at_first_response` AND `breach_acknowledged_at_resolution`.
8. Routes assignment back to the original agent IF that agent is `is_on_call=true` for the ticket's market AND has fewer than the per-agent open-cap; otherwise unassigns the ticket back to the queue.
9. Writes audit row with `triggered_by=customer_reopen`.
10. Emits `ticket.reopened`.

**Rationale**:
- Recomputing both deadlines (not just resolution) reflects the operational truth that the customer's reopen IS a fresh first-message that the agent must respond to within the first-response window.
- Clearing the breach acknowledgments allows the breach worker to detect a re-breach if the second go-round also exceeds SLA.
- Routing back to the original agent maintains continuity when possible; falling back to the queue prevents a stale routing on agent off-shift.
- The cap (`max_reopen_count`) prevents a customer from indefinitely reopening; after 3 reopens (default) the customer must open a new ticket linked to the prior one.

**Alternatives rejected**:
- Recompute only `resolution_due_utc` — wrong; the customer is now waiting on a fresh first-response.
- No reopen cap — a malicious customer could DoS the queue.
- Always route back to queue regardless of original-agent availability — loses continuity.

---

## R-10 · Orphaned-assignment reclaim

**Decision**: The `OrphanedAssignmentReclaimWorker` runs nightly (00:30 UTC; advisory-lock guarded). It scans every active `TicketAssignment` row whose `agent_id` no longer has an active row in spec 004's identity store (i.e., the agent was offboarded in the last 24 h). For each, it stamps `superseded_at_utc = now()` on the assignment, leaves the ticket without an active assignment (back to the queue), writes an audit row with `triggered_by=agent_offboarded`, and emits `ticket.reassigned` (a synthetic system-event message is appended to the ticket noting the offboarding).

**Rationale**:
- Real-time reclaim on agent-offboard event would race with in-flight assignment writes (the agent's last-second claim during the offboard transaction).
- Nightly batch is operationally bounded (scans a few thousand rows max) and auditable as a single batch event.
- Falling back to the queue (rather than auto-reassigning to another agent) lets the on-shift `support.lead` triage the next morning with full context.

**Alternatives rejected**:
- Real-time event-driven reclaim — race condition + thundering-herd reassignment when an agent shift ends.
- Auto-reassign to round-robin — drops the lead's triage opportunity; a poorly-routed ticket on offboarded-agent's plate may need lead-level reassignment anyway.

---

## R-11 · Customer-account-lifecycle auto-close

**Decision**: 023 subscribes to spec 004's `customer.account_locked` and `customer.account_deleted` events via the existing `ICustomerAccountLifecycleSubscriber` interface from spec 020. On receipt, every non-`closed` ticket authored by that customer is transitioned to `closed` with `triggered_by=author_account_locked` and `reason_note='auto_closed:author_account_locked'`. The customer's reopen path is closed (their account is gone). A `support.lead` may manually reopen if dispute investigation requires it (subject to the manual-reopen path, audited).

**Rationale**:
- Account-locked customers can't reply, can't reopen, can't read; tickets in any non-closed state would sit there as zombies wasting agent attention.
- Manual reopen by a lead retains the option for legal / investigation needs.
- Re-using `ICustomerAccountLifecycleSubscriber` from spec 020 / 022 avoids a duplicate subscription on the bus.

**Alternatives rejected**:
- Leave in-flight tickets in their current state — zombie tickets pollute the queue.
- Hard-delete tickets — FR-005a violation.

---

## R-12 · Customer reply rate-limit envelope

**Decision** (FR-010): customer reply rate-limit is 30 / hour / actor on a single ticket. Agent reply rate-limit is the FR-039 admin envelope (60 / hour). Customer ticket creation is 5 / hour, 20 / day. Idempotency-Key is required on creation, reply, conversion, and reopen — duplicates within 24 h return the original 200.

**Rationale**:
- Customer-side caps defend against spam-bot abuse; 30 replies / hour on one ticket is a generous human-sustainable rate.
- Agent rate-limit is calibrated to defeat scripted abuse without throttling legitimate ops at peak.
- Idempotency on creation defeats duplicate-tap submissions from flaky mobile networks.

**Alternatives rejected**:
- No customer rate-limit on replies — abuse vector.
- Same envelope for customer + agent — agent is paid to be fast and may legitimately reply 60 times in an hour during a P1 incident.

---

## R-13 · Auto-close resolution-window worker

**Decision**: `AutoCloseResolutionWindowWorker` runs hourly (advisory-lock guarded). It selects all `state=resolved` tickets where `now() - resolved_at_utc > auto_close_after_resolved_days` (per-market schema; default 7 days). Each is transitioned to `closed` with `triggered_by=auto_close_resolution_window`. A system-event message is appended to the ticket. An audit row + `ticket.closed` event are emitted.

**Rationale**:
- Hourly is fine-grained enough; daily would mean a ticket sits in `resolved` 6 days + 23 hours past the threshold worst-case.
- Per-market `auto_close_after_resolved_days=0` disables auto-close for that market (rare; explicit operational decision).

**Alternatives rejected**:
- Daily — too coarse.
- Real-time event-driven — Postgres has no deadline event; sidecar scheduler not available.
- Customer-explicit close — already supported (the customer can NOT-reopen and let the worker close it); explicit "close my ticket" UX is a Phase 1.5 polish item.

---

## R-14 · Editorial AR/EN ICU keys

**Decision**: every system-generated string lives in `Modules/Support/Messages/support.en.icu` + `support.ar.icu`. Categories include: state labels (5), category labels (10), priority labels (4), reason codes (~52), breach badge labels (2), system-event message bodies (one per `triggered_by`), and the "edited-since-last-surface" + "media-pending" badges. AR strings are flagged for editorial review in `AR_EDITORIAL_REVIEW.md` (per the spec 022 pattern).

**Rationale**:
- Principle 4 mandates editorial-grade AR; ICU keys are the standard pattern from spec 003 onward.
- Centralising the AR-EN copy in two files makes editorial review tractable.

**Alternatives rejected**:
- Hardcoded English strings with translation deferred — Principle 4 violation.
- Auto-translation — Principle 4 violation.

---

## R-15 · Constitutional readiness summary

| Principle | Resolved by |
|---|---|
| P3 Experience Model | Tickets are auth-only (no public unauth surface). |
| P4 Bilingual + RTL | FR-032 + FR-033 + R-14. |
| P5 Market Configuration | `support_market_schemas` + `sla_policies` rows. |
| P6 Multi-vendor-ready | `vendor_id` slot reserved. |
| P9 B2B | `ICompanyAccountQuery` + B2B account-manager read role. |
| P17 Order & Post-purchase | Linked-entity contracts + return-conversion. |
| P19 Notifications | 16 events emitted; spec 025 subscribes. |
| P21 Operational Readiness | Constitutional-spec; entire scope. |
| P24 State Machines | TicketStateMachine.cs + reopen edge. |
| P25 Audit | FR-030 + FR-031 + 18 audit-event kinds. |

**Phase 0 status**: complete. All 5 clarifications integrated. No NEEDS CLARIFICATION markers remain.

**Next**: Phase 1 — `data-model.md`, `contracts/`, `quickstart.md` (already authored alongside this research).
