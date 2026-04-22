# Quickstart — Checkout v1 (Spec 010)

## Prerequisites
- Branch `phase-1B-specs`.
- Specs 003, 004, 005, 007-a, 008, 009 merged. Spec 011 (orders) + 012 (invoices) being developed in parallel; stubs acceptable during development.

## 30-minute walk-through
1. **Primitives.** `IPaymentGateway` + `StubPaymentGateway`. `IShippingProvider` + `StubShippingProvider`. `CheckoutSessionStateMachine`. `IdempotencyStore`. `DriftDetector`.
2. **Persistence.** 5 tables; migration `Checkout_Initial`.
3. **Customer slices.** StartSession, SetAddress, GetShippingQuotes, SelectShipping, SelectPaymentMethod, Summary, Submit, ConfirmDrift.
4. **Webhook.** Signature verification + dedup + handler.
5. **Admin.** ListSessions + ForceExpire (audit-logged).
6. **Workers.** CheckoutExpiryWorker (1 min); PaymentReconciliationWorker (bank transfer).
7. **Tests.** 1000 concurrent submits (SC-003); idempotency (SC-002); webhook dedup (SC-007); COD cap matrix (SC-008); restricted gate (SC-005); drift + accept (SC-004).
8. **AR editorial on `checkout.ar.icu`.**

## DoD
- [ ] 25 FRs → ≥ 1 contract test each.
- [ ] 9 SCs → measurable check.
- [ ] Submit p95 ≤ 2 s (excluding gateway).
- [ ] Fingerprint + constitution check.
