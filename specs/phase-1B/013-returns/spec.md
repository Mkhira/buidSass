# Feature Specification: Returns & Refunds (v1)

**Feature Number**: `013-returns`
**Phase Assignment**: Phase 1B · Milestone 4 · Lane A (backend)
**Created**: 2026-04-22
**Input**: constitution Principles 4, 5, 8, 13, 17, 18, 21, 22, 23, 24, 25, 27, 28, 29.

---

## Clarifications

### Session 2026-04-22

- Q1: Return policy scope? → **A: Line-level returns.** Customer selects specific order lines with qty and a reason code. Full-order returns are a special case (all lines, full qty).
- Q2: Approval flow? → **A: Two-step**: customer submits return request → admin reviews (approve/reject/partial) → on approval, return label (where applicable) + refund authorization. Configurable per market whether auto-approve for orders < N days.
- Q3: Refund destination? → **A: Original payment method by default** (reverse via IPaymentGateway). Exceptions (e.g. cancelled card) route to manual bank transfer with admin entering IBAN. Store credit is out of scope for Phase 1B.
- Q4: Return window? → **A: Per-market config** (KSA launch 14 days from delivery, EG launch 7 days); restricted categories (open pharma) may have 0-day window — product-level override.
- Q5: Restocking? → **A: Triggered by admin on inspection** — inspected + sellable units post to spec 008 as a stock movement with `reason=return`; defective units stay out of ATS (separate "quarantine" count).
- Q6: Partial refund math? → **A: Pro-rata line discounts + original tax rate** (stored in spec 012 invoice). Shipping fees refunded only on full-order returns or admin discretion.

---

## User Scenarios & Testing

### User Story 1 — Customer requests a return (P1)
Customer opens a delivered order within the return window, selects lines + qty + reason, optionally uploads photos.

**Acceptance Scenarios**:
1. *Given* an order delivered 5 days ago and a 14-day KSA policy, *when* the customer opens return flow, *then* all lines with `delivered_qty > returned_qty` are selectable.
2. *Given* a restricted product with 0-day window, *then* the line is disabled in the UI with an explanation.
3. *Given* a submitted request, *then* `return_state=pending_review` and customer sees an ack + tracking id `RET-KSA-202604-000021`.

---

### User Story 2 — Admin reviews the request (P1)
Admin opens queued requests, approves / partially approves / rejects.

**Acceptance Scenarios**:
1. *Given* `pending_review`, *when* admin approves with same qty, *then* state → `approved`, notification sent to customer; return label generation (if shipping-provider supports) queued.
2. *Given* admin rejects with reason, *then* state → `rejected`, customer notified.
3. *Given* admin partially approves (reduced qty or dropped a line), *then* state → `approved_partial`; customer notified; only approved qtys proceed.

---

### User Story 3 — Physical return + inspection (P1)
Items arrive at warehouse; admin inspects and categorizes.

**Acceptance Scenarios**:
1. *Given* `approved`, *when* admin records receipt of all units, *then* state → `received`.
2. *Given* inspection, admin marks each unit `sellable` or `defective`; sellable units produce an inventory movement `reason=return` (spec 008 stock +1 in ATS); defective units go to quarantine count.
3. *Given* all units inspected, state → `inspected`.

---

### User Story 4 — Refund execution (P1)
Approved return → refund issues to original payment method.

**Acceptance Scenarios**:
1. *Given* `inspected`, *when* refund runs, *then* `IPaymentGateway.Refund()` is called with the captured transaction id, amount, and idempotency key; on success, state → `refunded` and spec 011 order `refund_state` advances; credit note issued via spec 012.
2. *Given* refund fails (gateway error), *then* state → `refund_failed`, admin alert; retry supported.
3. *Given* COD order, *then* refund is marked `manual_pending_bank_transfer`; admin enters IBAN + `POST refunds/{id}/confirm-bank-transfer` advances state.

---

### User Story 5 — Credit note + invoice reconciliation (P1)
Spec 012 issues a credit note referencing the original invoice using the stored tax rate.

**Acceptance Scenarios**:
1. *Given* `refunded`, *when* credit note issues, *then* lines = refunded lines × refunded qty × original unit price + original tax rate; totals are negative of refunded portion.
2. *Given* restocking fees apply (admin config), *then* an extra positive line shows the fee on the credit note.

---

### User Story 6 — Admin force-refund without physical return (P2)
Rare: admin agrees to refund without requiring shipment back (e.g. low-value item, customer goodwill).

**Acceptance Scenarios**:
1. *Given* admin uses the force-refund action with reason, *then* state transitions `pending_review → refunded` directly (skipping `received/inspected`); audit row written; inventory NOT incremented.
2. *Given* audit log queried, *then* the skip is visible.

---

### User Story 7 — Customer tracks the return (P2)
Customer sees the return status on the original order detail and on a dedicated returns list.

**Acceptance Scenarios**:
1. *Given* a return in `approved`, *when* customer opens order, *then* a return card shows current state + next step.
2. *Given* refund completes, *then* the card shows the refund amount + credit-note link.

---

### Edge Cases
1. Customer requests return after window closed → `400 return.window.expired`.
2. Partial qty already returned in an earlier RMA → available qty = `delivered_qty - already_returned_qty`.
3. Tax rate changed between purchase and refund → use original rate (stored in spec 012 invoice).
4. Payment method no longer valid (card closed) → refund routes to manual bank transfer path.
5. Shipping provider can't generate a return label → manual drop-off instructions shown.
6. Restocking causes negative inventory if double-posted → idempotency on `(return_id, line_id, unit_serial)` prevents duplicates.
7. Customer uploads > 10 photos → 413 with message.
8. Admin rejects after approval by accident → reversal path is new RMA; no "un-approve" action.
9. Order cancelled pre-delivery → NOT a return; covered by spec 011 cancel path.

---

## Requirements (FR-)
- **FR-001**: System MUST allow customer to submit a return request for a delivered order within the market's return window.
- **FR-002**: System MUST enforce per-product zero-window overrides for restricted categories.
- **FR-003**: System MUST assign return number `RET-{MARKET}-{YYYYMM}-{SEQ6}`.
- **FR-004**: System MUST maintain a `return_state` machine (see data-model.md SM-1).
- **FR-005**: Customer MUST be able to select lines + qty + reason code + optional photos (max 5).
- **FR-006**: System MUST expose admin endpoints: list, detail, approve, reject, approve-partial, mark-received, record-inspection, issue-refund, force-refund.
- **FR-007**: System MUST call `IPaymentGateway.Refund()` with the captured transaction id + idempotency key.
- **FR-008**: System MUST emit `refund.completed` event which triggers spec 012 credit note.
- **FR-009**: System MUST emit `inventory.return_movement` to spec 008 for sellable units only.
- **FR-010**: System MUST advance spec 011 `order.refund_state` on every RMA state change.
- **FR-011**: System MUST support COD manual bank-transfer refund path with admin-entered IBAN.
- **FR-012**: System MUST audit every admin action (Principle 25).
- **FR-013**: System MUST surface return tracking on the order detail (customer-facing).
- **FR-014**: System MUST compute refund amounts pro-rata at the original tax rate.
- **FR-015**: System MUST support admin-configured restocking fees with transparent display to the customer on review.
- **FR-016**: System MUST provide an admin CSV export of returns.
- **FR-017**: System MUST expose `GET /v1/customer/returns` listing.
- **FR-018**: System MUST notify customer on every state transition (spec 019 in Phase 1D; event emitted now).
- **FR-019**: System MUST support idempotent admin actions (e.g. duplicate "approve" click).
- **FR-020**: System MUST persist photos in object storage with metadata (file size, mime, SHA).
- **FR-021**: System MUST handle refund gateway failures via a retry queue with exponential backoff.
- **FR-022**: System MUST prevent refunding more than was captured (over-refund guard).
- **FR-023**: System MUST expose `POST /v1/admin/refunds/{id}/confirm-bank-transfer` for manual refund path.
- **FR-024**: Return requests MUST be rejected if the order is not yet delivered.

### Key Entities
- **ReturnRequest** / **ReturnLine**
- **Refund** / **RefundLine**
- **Inspection** / **InspectionLine**
- **ReturnPhoto**
- **ReturnPolicy** (per market + per product override)

---

## Success Criteria (SC-)
- **SC-001**: Customer return submit → admin sees it in < 5 s.
- **SC-002**: Refund execution p95 < 2 s (gateway call excluded) once inspection complete.
- **SC-003**: Refund amount numerically equals `sum(refundLine.unit_price × qty × (1 + tax_rate))` for 1000 parameterized cases (tolerance 0 minor units).
- **SC-004**: State machine invariant: fuzz 10 k transitions → 0 illegal accepted.
- **SC-005**: Idempotency: duplicate admin approve clicks → 1 state mutation (SC test).
- **SC-006**: Over-refund guard: attempted `amount > captured - already_refunded` → `400 refund.over_refund_blocked` for all 100 crafted cases.
- **SC-007**: Inventory restocking idempotency: replayed inspection event → 0 duplicate movements.
- **SC-008**: AR editorial pass on return reason codes + statuses.
- **SC-009**: Credit note reconciliation: `refund_amount` == `|credit_note.grand_total|` for 100/100 sampled.

---

## Dependencies
- Spec 004 identity + addresses.
- Spec 008 inventory (restocking movement).
- Spec 011 orders (delivered status, refund_state advance).
- Spec 012 tax invoices (credit note).
- Spec 013 relies on spec 010's `IPaymentGateway.Refund`.
- Spec 014 (storefront return UI) and spec 015/016 (admin return queue) are Phase 1C consumers.

## Assumptions
- Photos: ≤ 5 per request, ≤ 5 MB each, JPEG/PNG/HEIC.
- Restocking fees configurable per market (default 0%).
- Single-currency refund (matches order currency).

## Out of Scope
- Store credit / wallet refunds — Phase 1.5.
- Exchange (swap item) — Phase 1.5.
- Warranty RMA flow — Phase 2.
- Automated carrier return label (beyond what spec 010 shipping provider exposes) — Phase 1.5.
