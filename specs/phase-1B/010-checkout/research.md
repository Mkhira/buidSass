# Research — Checkout v1 (Spec 010)

**Date**: 2026-04-22

## R1 — Single-page vs multi-step
**Decision**: Server-side session row drives state; client renders as needed.
**Rationale**: Backend-owned state avoids "lost progress" bugs; frontend UX can evolve without changing the contract.

## R2 — Two-phase submit
**Decision**: `submit` runs the flow atomically inside one handler (idempotency key); payment authorization is one step in it. Session state records each transition for audit.
**Rationale**: True saga (distributed) not needed — single service boundary. Idempotency key covers retries.
**Alternative**: Outbox-based async submit — rejected because user-perceived latency would regress.

## R3 — Idempotency
**Decision**: `Idempotency-Key` header mandatory on `submit`; response cached in `checkout.idempotency_results` for 5 min.
**Rationale**: RESTful pattern; standard in payments industry.

## R4 — Pricing drift handling
**Decision**: If Issue hash ≠ last Preview hash, return `409 checkout.pricing_drift` with diff + require client confirmation via `POST /summary/accept-drift` before re-submit.
**Rationale**: Customer consent required for price changes; avoids silent surprise.

## R5 — Payment gateway abstraction
**Decision**: `IPaymentGateway { Authorize, Capture, Void, Refund, HandleWebhook }`. Stub impl for Dev. Real providers plug in per ADR-007.
**Rationale**: Principle 13 compliance. Simplest interface covers the happy path + recovery.

## R6 — Bank transfer semantics
**Decision**: Order created immediately in `payment.pending`; finance admin reconciles manually via dedicated endpoint (spec 011 exposes `AdminConfirmBankTransfer`).
**Rationale**: Common B2B flow in EG/KSA markets.

## R7 — Webhook security
**Decision**: Signature verification per provider's documented algorithm; `payment_webhook_events` unique key on `(provider, event_id)` for idempotency; HTTP 2xx regardless of internal outcome (providers retry on non-2xx).
**Rationale**: Industry standard.

## R8 — Shipping quote caching
**Decision**: Cache quotes per session for 10 min; re-quote on address change.
**Rationale**: Provider APIs often rate-limit quotes; 10 min is typical quote validity.

## R9 — COD eligibility
**Decision**: Market-configurable: (enabled: bool, `cap_minor`, `excludes_restricted: bool`). Defaults KSA 2000 SAR / EG 5000 EGP, restricted excluded.
**Rationale**: Reduces fraud + operational risk.

## R10 — Session expiry
**Decision**: 30 min inactivity; 25 min warning; expired sessions release reservations.
**Rationale**: Balances user patience with inventory hygiene.

## R11 — Guest auth requirement
**Decision**: Guest can fill session; `submit` returns 401 until auth. No anonymous purchase at launch.
**Rationale**: Principle 3 + legal (tax invoice, returns) need an account.

## R12 — Saga compensation on order-create failure
**Decision**: If payment authorized but order-create fails, schedule `Void` on gateway within 30 s + raise ops alert; audit row captures both operations.
**Rationale**: Rare but must be handled; compensation keeps the system honest.
