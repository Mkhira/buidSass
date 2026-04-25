# Spec 011 Orders v1 — Definition of Done Checklist

DoD version: 1.0 (`docs/dod.md`). Constitution version: 1.0.0.

## Universal Core

- [x] **UC-1** — Acceptance scenarios pass.
  - 112 tests in `Tests/Orders.Tests/` (63 unit + 49 integration/contract).
  - All 8 user-stories from `spec.md` exercised by integration or contract tests.
- [ ] **UC-2** — Lint + format CI gates green.
  - Local `dotnet build` clean (0 errors). CI `lint-format` runs on PR open.
- [ ] **UC-3** — Contract drift check passes.
  - `openapi.orders.json` regenerated; CI `contract-diff` runs on PR open.
- [ ] **UC-4** — Context fingerprint in PR description.
  - To be added to PR description: output of `scripts/compute-fingerprint.sh`.
- [x] **UC-5** — Constitution + ADR-protected paths untouched. No edits to `.specify/memory/constitution.md` or ADR table.
- [ ] **UC-6** — Required code-owner approvals. Pending PR.
- [ ] **UC-7** — Signed commits + merge policy. Local commits use the configured signer; CI verifies.
- [x] **UC-8** — Spec header records constitution version (1.0.0) — present in `spec.md`.

## Applicability-Tagged Items

### [trigger: state-machine] — APPLIES

Four state machines (`OrderSm`, `PaymentSm`, `FulfillmentSm`, `RefundSm`) explicitly define:
- States: enumerated as constants and cross-checked by `IsValidTransition` switch arms.
- Transitions: documented in `data-model.md` SM-1..SM-4; tested in `StateMachinesTests`.
- Actors: persisted in `order_state_transitions.actor_account_id`; admin endpoints carry the JWT sub.
- Transition guards: switch-arm based; invalid transitions return `order.state.illegal_transition`.
- Failure / retry handling: webhook hook absorbs duplicates via PaymentSm self-transitions; `WebhookDedupTests` proves SC-005 (100 deliveries → 1 mutation).

### [trigger: audit-event] — APPLIES

Every admin mutation writes:
1. A row in `orders.order_state_transitions` (state-machine trace).
2. A row in `audit_log_entries` via `IAuditEventPublisher` (compliance audit, with actor + before/after JSON + reason).

`AdminAuditTests.StartPicking_WritesAuditRow` verifies the cross-store write end-to-end.

### [trigger: storage] — N/A. No file/object storage in spec 011.

### [trigger: pdf] — N/A. PDFs are spec 012's concern. The `payment.captured` outbox event is the seam.

### [trigger: user-facing-strings] — APPLIES

`Modules/Orders/Messages/orders.en.icu` + `orders.ar.icu` ship 38 keys covering reason codes and high-level status labels. The Arabic file has been generated; **a native Arabic editorial review is still required before merge** (Principle 4 — editorial-grade quality, not machine-translated). Track in PR description.

### [trigger: environment-aware] — APPLIES (limited)

Workers (`OutboxDispatcher`, `QuotationExpiryWorker`, `PaymentFailedRecoveryWorker`) are gated `!IsEnvironment("Test")` so test factories don't fight a background dispatcher. No SeedGuard bypass.

### [trigger: docker-surface] — N/A. No Dockerfile changes; backend image rebuilds from existing layout.

### [trigger: ships-a-seeder] — N/A. Cancellation policy seed lives inside the migration (`Orders_Initial`) using SQL `INSERT ... ON CONFLICT DO NOTHING`; no `ISeeder` implementation needed.

### [trigger: ui-surface] — N/A. Backend-only spec (Phase 1B Lane A). Customer/admin UIs are Phase 1C specs 014/015.

## Test summary

| Surface | Count | Status |
|---|---|---|
| Unit (`Tests/Orders.Tests/Unit/`) | 63 | ✅ pass |
| Integration (`Tests/Orders.Tests/Integration/`) | 49 (incl. 26 contract tests + 7 deep-review regression tests) | ✅ pass (Docker required) |
| **Total** | **112** | **✅ pass** |

## Constitution gate snapshot (from `plan.md`)

| Principle | Gate |
|---|---|
| 5 — Market-configurable | ✅ per-market sequencer + cancellation policies |
| 6 — Multi-vendor-ready | ✅ `owner_id`/`vendor_id` on Order |
| 9 — B2B | ✅ quotations CRUD + bank transfer + PO carried |
| 14 — Shipping abstraction | ✅ shipment via opaque providerId/methodCode |
| 17 — Order separation | ✅ four independent state machines |
| 21 — Operational readiness | ✅ admin fulfillment + finance export + audit |
| 22/23 — Stack + architecture | ✅ .NET 9, Postgres 16, modular monolith |
| 24 — State machines | ✅ each machine enumerated |
| 25 — Audit | ✅ every transition + admin mutation audited |
| 27 — UX quality (backend) | ✅ timeline, tracking link, action gating |
| 28 — AI-build standard | ✅ explicit transition tables + reason codes |

## Outstanding (PR-time, human signoff)

1. **AR editorial review** of `orders.ar.icu` — Principle 4.
2. **Code-owner approvals** per UC-6.
3. **Fingerprint header** in PR description per UC-4.
4. **CI runs** (`lint-format`, `contract-diff`, `verify-context-fingerprint`, `build-and-test`) — automatic on push.
