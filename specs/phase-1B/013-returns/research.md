# Research — Returns & Refunds v1 (Spec 013)

**Date**: 2026-04-22

## R1 — Line-level granularity
**Decision**: Customer selects specific order lines + qty. "Full return" is the same flow with all lines / full qty.
**Rationale**: Common e-commerce pattern; aligns with spec 012 credit-note line-level math.
**Alternative**: Full-order only — rejected; too blunt for multi-line orders.

## R2 — Approval flow
**Decision**: Two-step (customer submit → admin review). Admin may approve, reject, or partially approve.
**Rationale**: Protects against abuse; matches KSA/EG retail norms.
**Alternative**: Auto-approve everything < N days — reserved as a per-market toggle, disabled at launch.

## R3 — Refund destination
**Decision**: Original payment method via `IPaymentGateway.Refund` by default; manual bank transfer fallback for COD + expired cards.
**Rationale**: Least friction for customer + provider chargeback safety.
**Alternative**: Store credit default — rejected (Phase 1B; customer trust).

## R4 — Return windows
**Decision**: Per-market config (KSA 14 d, EG 7 d launch values) with per-product override (`zero_window`) for restricted categories.
**Rationale**: Regulatory + category norms (sealed pharma can't be returned).

## R5 — Inspection workflow
**Decision**: Admin flags each unit `sellable` or `defective`. Only sellable units post a +1 inventory movement to spec 008.
**Rationale**: Protects ATS accuracy; defective units require disposal or supplier claim.

## R6 — Refund math
**Decision**: Pro-rata: `refund_line_amount = original_unit_price × qty × (1 + original_tax_rate)`; line-level discounts pro-rated by qty ratio. Shipping refunded on full-order returns or admin discretion.
**Rationale**: Simple, explainable, audit-proof.

## R7 — Idempotency
**Decision**: Every admin state mutation keyed by `(return_id, action)`; duplicates return 200 with the same state. Inventory movements keyed by `(return_id, line_id)`.
**Rationale**: Protects against double-clicks and worker replays.

## R8 — COD refund
**Decision**: Manual bank transfer. Admin UI captures IBAN + beneficiary name; refund state goes `pending_manual_transfer → refunded` after admin confirms dispatch.
**Rationale**: COD has no card to refund to.

## R9 — Photos
**Decision**: Up to 5 photos per return, stored in Azure Blob Storage (`returns/{id}/{fileId}.jpg`). JPEG/PNG/HEIC, ≤ 5 MB each.
**Rationale**: Evidence for defect claims; helps admin review.

## R10 — Notifications
**Decision**: Emit events only in Phase 1B; spec 019 (Phase 1D) consumes them.
**Rationale**: Keeps scope tight; no coupling to notification infra yet.

## R11 — Over-refund guard
**Decision**: Before calling gateway, verify `already_refunded + new_amount ≤ captured_total`. Block with `400 refund.over_refund_blocked` otherwise.
**Rationale**: Prevents accidents + fraud.

## R12 — Restocking fees
**Decision**: Per-market config; applied as an extra line on the credit note; default 0%.
**Rationale**: Some markets charge; default off to avoid surprise.
