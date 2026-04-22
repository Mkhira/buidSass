# Feature Specification: Checkout (v1)

**Feature Number**: `010-checkout`
**Phase Assignment**: Phase 1B · Milestone 4 · Lane A (backend)
**Created**: 2026-04-22
**Input**: constitution Principles 3, 5, 8, 9, 10, 11, 13, 14, 17, 18, 22, 23, 24, 25, 27, 28, 29; ADR-007, ADR-008.

---

## Clarifications

### Session 2026-04-22

- Q1: Checkout shape — multi-step or single-page? → **A: Single-page with server-side session.** Client may render steps; backend maintains a `checkout_sessions` row with state `init → addressed → shipping_selected → payment_selected → submitted → confirmed | failed | expired`.
- Q2: Payment abstraction? → **A: `IPaymentGateway` interface** with provider-specific implementations deferred to Phase 1B-final (ADR-007 pending). At launch a `StubPaymentGateway` always-succeed in Dev, with a real provider swap-in later this phase. Payment workflow + idempotency spec'd here regardless of provider.
- Q3: Shipping abstraction? → **A: `IShippingProvider`** interface; launch with one provider per market stub, real providers per ADR-008 later in phase. Shipping quotes computed at checkout via the interface.
- Q4: Session TTL? → **A: 30 min from last activity**; inactive sessions release reservations + expire. Client gets a warning at 25 min.
- Q5: Address book? → **A: Accounts have saved addresses (from spec 004).** Guest checkout captures a one-shot address on the session.
- Q6: Order placement transaction? → **A: Two-phase.** (1) `submit` → lock reservations, re-check pricing (`Issue` mode), call payment gateway; on success → (2) `confirm` → create order (spec 011), convert reservations, emit events. On payment failure, session returns to `payment_selected`.

---

## User Scenarios & Testing

### User Story 1 — Retail consumer completes purchase (P1)
A KSA shopper pays with Mada card and receives order confirmation within 5 s of tap.

**Acceptance Scenarios**:
1. *Given* a cart with 2 lines + a saved delivery address, *when* checkout submit fires with card details, *then* payment gateway authorizes, order is created, reservations convert to deductions, invoice number issued (spec 012), and the response returns within 5 s end-to-end.
2. *Given* payment returns `declined`, *then* session returns to `payment_selected` with `payment.declined` reason; reservations remain active.
3. *Given* ATS drops between cart and submit, *then* `409 checkout.inventory_lost`; session returns to `init` with stockChanged flags.

---

### User Story 2 — Unauthenticated guest → forced login (P1)
A guest tries to submit. System returns `401 checkout.requires_auth` (Principle 3). Once logged in or registered, session resumes.

**Acceptance Scenarios**:
1. *Given* guest with filled session, *when* submit fires without JWT, *then* `401 checkout.requires_auth` returned with `nextStep: "login"`.
2. *Given* post-login, *when* the same session id is re-submitted, *then* session is re-owned by the account (the guest cart was already merged by spec 009).

---

### User Story 3 — Restricted product at submit (P1)
User tries to check out a restricted product while unverified. Submit fails deterministically.

**Acceptance Scenarios**:
1. *Given* an unverified customer with a restricted line, *when* submit fires, *then* `403 checkout.restricted_not_allowed` returned with `reasonCode=catalog.restricted.verification_required`.
2. *Given* a verified customer, *when* submit fires, *then* the line is accepted and order proceeds.

---

### User Story 4 — B2B purchase with PO + bank transfer (P1)
A clinic pays by bank transfer using a PO number.

**Acceptance Scenarios**:
1. *Given* a B2B account, *when* payment method `bank_transfer` is selected + PO number set on the cart, *then* submit creates an order in `payment.pending` state with bank transfer instructions; inventory converts but payment stays pending until reconciliation.
2. *Given* reconciliation confirms the transfer (admin action), *then* order advances `payment.confirmed` + shipping flow starts.
3. *Given* the PO is missing, *then* `400 checkout.b2b.po_required`.

---

### User Story 5 — Shipping quote + method selection (P1)
Session moves from `addressed → shipping_selected`. System returns quotes from configured providers for the market + address.

**Acceptance Scenarios**:
1. *Given* a saved KSA address + Riyadh city, *when* shipping quotes requested, *then* ≥ 1 quote returned with `{ providerId, methodCode, etaDays, feeMinor }`.
2. *Given* the customer selects a method, *then* session stores selection + fee; pricing total updates.
3. *Given* address changes, *then* shipping selection clears; quotes must be re-requested.

---

### User Story 6 — Cash on delivery (P2, KSA + EG)
Applicable where configured. Submit creates order in `payment.pending_cod` state.

**Acceptance Scenarios**:
1. *Given* COD is enabled for market + total ≤ configured cap, *when* submit fires, *then* order is created with `payment_method=cod` + shipping starts.
2. *Given* total > COD cap, *then* `400 checkout.cod_cap_exceeded`.
3. *Given* restricted product present, *then* `403 checkout.cod_restricted_product`.

---

### User Story 7 — Session expiry recovery (P2)
Shopper steps away for 35 min, comes back.

**Acceptance Scenarios**:
1. *Given* a session idle 35 min, *when* reloaded, *then* session is `expired`; reservations released; cart still exists; a fresh session can start.
2. *Given* a session idle 25 min, *when* reloaded, *then* server returns an `expiresAt` hint; client shows warning.

---

### User Story 8 — Admin visibility + support recovery (P2)
Admin views active + recent sessions for a customer to help recover from a stuck state.

**Acceptance Scenarios**:
1. *Given* admin opens customer support view, *when* checkout sessions requested, *then* last 5 sessions shown with status + last error.
2. *Given* admin forces an expire, *then* reservations released + event `checkout.admin_expired` emitted.

---

### Edge Cases
1. Double-submit (client retries while server is mid-processing) → idempotency key required on `submit`; second call returns the same outcome.
2. Payment gateway timeout → webhook reconciliation path owns final state; client shown `payment.pending_webhook` while waiting ≤ 60 s.
3. Price drift between cart Preview and checkout Issue → drift shown as `pricing_drift` diff; customer must confirm `acceptedTotalMinor` before re-submit.
4. Two payment methods attempted in one session → session enforces one active selection.
5. Partial shipping coverage (address unserviceable by any provider) → `400 checkout.address_unserviceable` with suggestion to change address.
6. B2B without PO required field → `400 checkout.b2b.po_required`.
7. Session created in one market, cart switched to another → `409 checkout.market_mismatch`; session invalidated.
8. Concurrent submits on same session (two tabs) → first wins; second gets `409 checkout.already_submitted` with the order id.
9. Payment success but order-create failure (rare) → saga compensation — refund initiated via spec 013; operator alert + audit row.
10. Shipping quote cache drift — shipping quotes valid for 10 min; re-quoted if older.

---

## Requirements (FR-)
- **FR-001**: System MUST maintain a `checkout_sessions` table with an explicit state machine (see data-model).
- **FR-002**: Session MUST bind to a cart id + market code + (account_id OR cart_token).
- **FR-003**: Session MUST capture: shipping address, billing address (may default to shipping), shipping provider selection, payment method selection, coupon (passthrough to cart), B2B metadata (passthrough).
- **FR-004**: Session TTL MUST be 30 min from last activity; expiry MUST release reservations.
- **FR-005**: System MUST expose `IPaymentGateway` with methods `Authorize`, `Capture`, `Void`, `Refund`, `HandleWebhook`.
- **FR-006**: System MUST expose `IShippingProvider` with methods `Quote`, `CreateShipment`, `Track`.
- **FR-007**: Submit MUST be idempotent via `Idempotency-Key` header; duplicate submits within 5 min return cached outcome.
- **FR-008**: Submit flow MUST: (1) lock cart, (2) call pricing Issue mode (spec 007-a), (3) verify all reservations, (4) call payment gateway, (5) on success invoke spec 011 to create order + spec 012 to issue invoice + spec 008 to convert reservations, (6) release cart lines, (7) return order summary.
- **FR-009**: Restricted product eligibility MUST be re-checked at submit via spec 005 restriction endpoint.
- **FR-010**: Payment methods MUST be market-configurable: KSA launch = `card`, `mada`, `apple_pay`, `stc_pay`, `bank_transfer`, `cod`; EG launch = `card`, `apple_pay`, `bank_transfer`, `cod`, `bnpl` (if provider available at go-live).
- **FR-011**: COD eligibility MUST be configurable: enabled markets + total cap + restricted-product exclusion.
- **FR-012**: Bank transfer flow MUST create the order in `payment.pending` and queue reconciliation; admin surface re-confirms.
- **FR-013**: System MUST capture a `pricing_drift` diff when `Issue` result differs from last Preview; customer MUST confirm the new total before final submit.
- **FR-014**: Shipping quotes MUST cache per session for 10 min; beyond that, re-quote.
- **FR-015**: Each state transition MUST record an audit row (Principle 24, 25).
- **FR-016**: Admin MUST be able to view active sessions per customer + force expire (audit-logged).
- **FR-017**: Payment webhook endpoint MUST verify provider signature + handle out-of-order delivery; dedupe by provider-supplied event id.
- **FR-018**: All monetary values MUST be in minor units; currency derived from market.
- **FR-019**: Session MUST support guest flow: may fill addresses + shipping + payment method but `submit` requires auth (FR forces Principle 3 alignment).
- **FR-020**: B2B accounts MUST be able to submit with `bank_transfer` + PO; PO MUST be preserved from cart to order.
- **FR-021**: Session MUST expose a `summary` endpoint returning fully priced + finalized totals for the UI review step.
- **FR-022**: Payment failure MUST leave reservations intact; session returns to `payment_selected`.
- **FR-023**: On submit success, cart MUST be marked `status=merged` (consumed) and a fresh cart created lazily on the next add.
- **FR-024**: Session MUST NOT be mutable after `submitted` (except via `confirm` or `fail` by system).
- **FR-025**: A `CheckoutExpiryWorker` MUST expire idle sessions every 1 min.

### Key Entities
- **CheckoutSession** — session row with state + selections.
- **PaymentAttempt** — per-attempt record linked to session and (on success) order.
- **ShippingQuote** — cached quote per session.
- **PaymentWebhookEvent** — received webhook, deduped.
- **AddressSnapshot** — immutable copy of address used on session.

---

## Success Criteria (SC-)
- **SC-001**: Submit p95 ≤ 2 s (excluding gateway round-trip); full end-to-end ≤ 5 s in success path.
- **SC-002**: Idempotent submit: duplicate key within 5 min returns byte-identical response.
- **SC-003**: Inventory integrity: 1000 concurrent submits across overlapping products produce 0 oversells.
- **SC-004**: Pricing integrity: `Issue` hash matches last Preview hash UNLESS drift was surfaced and confirmed (SC verified by fault injection).
- **SC-005**: Restricted product blocked at submit for unverified users 100 % of the time (SC test sweep).
- **SC-006**: Session expiry worker releases expired reservations within 1 min.
- **SC-007**: Payment webhook dedup: 100 duplicate deliveries → 1 state mutation.
- **SC-008**: COD cap enforcement: parameterized test over 2 markets × 5 totals = 10 cases, 100 % correct.
- **SC-009**: Admin force-expire writes an audit row with actor + reason.

---

## Dependencies
- Spec 004 identity + RBAC + addresses.
- Spec 005 catalog + restriction evaluator.
- Spec 007-a pricing Preview + Issue.
- Spec 008 inventory reservations + convert.
- Spec 009 cart.
- Spec 011 orders (downstream).
- Spec 012 tax invoices (downstream).
- ADR-007, ADR-008 (provider choices; stubs at launch).

## Assumptions
- Stub payment + shipping providers ship at launch; real providers wired via their ADRs in this same phase.
- Webhook endpoint is reachable from providers (ingress configured).

## Out of Scope
- Wallet / store credit as payment method — Phase 1.5.
- Split-payment (card + COD) — Phase 1.5.
- Multi-address shipping (one order, multiple destinations) — Phase 2.
- Pickup-from-store — Phase 1.5.
- Subscription / recurring orders — Phase 2.
