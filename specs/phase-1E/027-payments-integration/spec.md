# Feature Specification: 027 — Payments Integration

**Feature Branch**: `phase-1E`
**Spec ID**: 027
**Created**: 2026-05-10
**Status**: Draft
**Phase**: 1E — Integrations · Milestone 8
**Input**: Implementation-plan §Phase 1E spec 027 (lines 616–629) — "ADR-007 primary + backup per market live; BNPL (Tabby/Tamara KSA + Valu EG); reconciliation job; webhook replay. ADR-007 Accepted."

---

## Clarifications

### Session 2026-05-10

Five priority questions resolved (recommended-default basis per the agreed workflow). Sources: `default` unless otherwise noted.

- Q: KSA primary card provider (Apple Pay + Mada + STC Pay + Visa/MC) → A: **HyperPay** (primary). Source: `default`. Rationale: native Mada + STC Pay rails; PCI SAQ-A-friendly hosted-fields integration; established KSA bank acceptance. ADR-007 v1 KSA card stack. Backup: **Tap Payments**.
- Q: EG primary card provider → A: **Paymob** (primary). Source: `default`. Rationale: dominant EG e-commerce gateway; Visa/MC/Meeza coverage; Apple Pay rolling. Backup: **Kashier**.
- Q: BNPL providers at v1 → A: **Tabby + Tamara (KSA)** locked from implementation plan; **Valu (EG)** primary. No EG BNPL backup at v1 (single Valu integration; documented gap). Source: `default`. Rationale: matches implementation-plan §616–629 verbatim.
- Q: PCI scope at v1 → A: **SAQ-A** for both markets (hosted fields / tokenization only; no PAN, no CVV, no full track data ever touches the platform). Apple Pay + Mada + STC Pay use provider-hosted card-vault flows. SAQ-A-EP reserved for any future redirect-host scenario. Source: `default` per ADR-007 v1.0 scope.
- Q: Reconciliation cadence + exception-queue retention → A: **Daily reconciliation at 03:00 KSA**; exceptions retained 90 days operator-active + 365 days audit. Source: `default`. Rationale: daily matches provider ledger drop cadence; 90-day operator window matches 025/026 dead-letter posture; 365-day audit retention matches `audit_log_entries` policy.

ADR-007 transition: `Proposed` → **`Accepted`**. v1 stack:
- KSA cards: HyperPay primary, Tap Payments backup.
- KSA Apple Pay + Mada + STC Pay: HyperPay (rails through HyperPay's cards interface).
- KSA BNPL: Tabby + Tamara (concurrent — both surfaced to customer; no primary/backup).
- EG cards: Paymob primary, Kashier backup.
- EG Apple Pay + Meeza: Paymob.
- EG BNPL: Valu.
- COD: native (no provider).
- Bank transfer: native (manual reconciliation against bank statement; no provider integration at v1).
- PCI scope: **SAQ-A** for both markets.

---

## ADR & Constitution Traceability

| Source | Title | How 027 satisfies it |
|---|---|---|
| Principle 5 | Markets EG + KSA | Per-market provider routing; per-market payment-method enabling; per-market BNPL eligibility. |
| Principle 13 | Payment | Apple Pay, Visa, MasterCard, Mada, STC Pay, bank transfer, COD, BNPL all supported. Generic abstraction layer. Retries + failed-payment recovery + pending states + reconciliation + idempotency all required. |
| Principle 17 | Order & post-purchase | Payment state is one of the four orthogonal status fields on `orders` (per spec 011); 027 owns payment state machine. |
| Principle 24 | State machine | Explicit Payment state machine covering authorization, capture, retry, refund, reconciliation. |
| Principle 25 | Audit | Every payment attempt + provider failover + reconciliation exception + manual operator action audit-logged. |
| Principle 28 | AI-build | Implementation-ready: provider matrix, state machine, webhook signatures, reconciliation flow all enumerated. |
| Principle 29 | Required spec output | All twelve sections present. |
| ADR-007 | Payment providers | Flipped to **Accepted** in this spec; provider stack locked. PCI scope SAQ-A. |
| ADR-010 | Cloud + residency | Payment metadata + reconciliation ledger persisted in KSA Central Postgres; provider egress carries minimum-required fields; PCI cardholder data NEVER stored. |
| Spec 010 | cart-checkout | Selects payment method; passes payment intent to 027. **Hard dep**. |
| Spec 012 | tax-and-invoice | Captures payment-confirmed amount; invoice references payment-id. **Hard dep**. |
| Spec E1 | infrastructure-integration | KV slots `payments/<market>/<provider>/{api-key,api-secret,webhook-signing-key}`. **Hard prerequisite**. |
| Spec 025 | notifications | Subscribes to `payment.captured`, `payment.failed`, `payment.refunded` for customer notifications. |
| Spec 011 | order | Payment state feeds order's payment-status field. |

---

## Goal

Deliver a centralized, multi-provider, multi-market, multi-method payments module that:

1. Tokenizes card data via provider-hosted fields (PCI SAQ-A boundary).
2. Authorizes + captures payments through configured providers, with idempotency and retries.
3. Supports the full method matrix: cards, Apple Pay, Mada, STC Pay (KSA), Meeza (EG), BNPL (Tabby/Tamara KSA, Valu EG), COD, bank transfer.
4. Provides daily reconciliation against provider ledgers with an operator-facing exception queue.
5. Receives provider webhooks idempotently with replay capability for incident response.
6. Tracks an explicit payment state machine; emits domain events that drive order + invoice + notification flows.
7. Architected so adding a provider is a config + adapter change, never a core-flow change (Principle 13).

027 is **backend-heavy with admin-UI surfaces** for payment-method config, provider routing, reconciliation review, exception handling, and webhook replay.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Customer pays via Mada at checkout (Priority: P1)

A customer in KSA selects Mada at checkout. The platform tokenizes the card via HyperPay's hosted fields. 027 creates a `Payment` row in `pending_authorization`, calls HyperPay to authorize+capture, receives `captured`, transitions the payment, fires `payment.captured`, and the order proceeds to fulfillment.

**Why this priority**: Cards (incl. Mada) are the volume-dominant payment method at launch. Without it, the platform cannot accept paid orders.

**Independent Test**: Place a paid order on Staging using a Mada test card via HyperPay sandbox; observe `Payment` row transitions through `pending_authorization → captured` within 10 seconds; observe `payment.captured` event published; observe order's `payment_status` updates accordingly.

**Acceptance Scenarios**:

1. **Given** a customer at the payment step with method=Mada, **When** the hosted-fields submission succeeds, **Then** 027 creates a `Payment` with idempotency key derived from `(order_id, method, attempt_id)` and calls the provider.
2. **Given** the provider returns `captured`, **When** the response handler runs, **Then** the payment transitions to `captured`, `payment.captured` event publishes with the captured amount and currency, and an audit row records the actor and provider message-id.
3. **Given** the provider returns a transient `5xx`, **When** the retry policy runs (3 attempts with backoff), **Then** on success the payment captures; on exhaustion the payment moves to `failed` with reason `provider_unavailable` and the customer sees a retry CTA.
4. **Given** a duplicate `(order_id, method, attempt_id)` arrives (e.g., customer double-clicks), **When** the create-payment handler runs, **Then** the second request is idempotent — no duplicate provider call, no duplicate Payment row.

---

### User Story 2 — Customer pays via Tabby BNPL (Priority: P1)

A KSA customer selects Tabby BNPL. The platform redirects to Tabby's hosted flow; on customer approval, Tabby calls our webhook with the approved installment plan; 027 confirms the payment and transitions the order.

**Why this priority**: BNPL is a top-three checkout option in KSA; required by Principle 13 v1.

**Independent Test**: Trigger a Tabby BNPL flow with the sandbox-approved customer profile; verify the redirect+webhook+confirmation cycle completes within 60s; verify `payment.captured` (with `method=bnpl_tabby`) fires; verify the customer sees the installment schedule on the order page.

**Acceptance Scenarios**:

1. **Given** a customer selects Tabby BNPL, **When** they submit, **Then** 027 creates a Payment in `pending_external_redirect` and returns the Tabby checkout URL.
2. **Given** Tabby's webhook arrives with `status=authorized` (signature-validated), **When** the handler dedupes via `webhooks_received` PK, **Then** the Payment transitions to `captured` and `payment.captured` publishes; `subtype=bnpl_tabby`.
3. **Given** Tabby rejects the customer (declined), **When** the webhook arrives with `status=rejected`, **Then** the Payment moves to `failed` with reason `bnpl_declined`; the customer is offered a fallback method.
4. **Given** the redirect TTL elapses without webhook (10 min default), **When** the timeout reconciler runs, **Then** the Payment is `expired` and the cart is preserved for retry.

---

### User Story 3 — Customer's payment fails; retry succeeds on the second attempt (Priority: P1)

A customer's first payment attempt fails (insufficient funds). They click "try another method" or "retry"; the platform creates a new Payment row referencing the same order; the second attempt succeeds.

**Why this priority**: Failed-payment recovery is required by Principle 13. Without it, transient declines lose otherwise-completable orders.

**Independent Test**: Force a sandbox decline on attempt 1; observe `Payment.state=failed`; trigger retry with a different sandbox-success card; observe a NEW `Payment` row in `captured`; both rows reference the same order.

**Acceptance Scenarios**:

1. **Given** a Payment is `failed` for an order, **When** the customer triggers retry, **Then** a new Payment row is created (NOT a state-change on the failed row); the order's `payment_status` reflects the latest attempt.
2. **Given** an order has 3 failed attempts within 24 hours, **When** the customer attempts a 4th, **Then** the system suggests an alternative method (BNPL, COD where eligible) and may apply a soft rate-limit on cards (configurable; default 5/24h).
3. **Given** a payment was authorized but not captured (rare on KSA flows but possible), **When** the capture step fails, **Then** the Payment transitions `authorized → capture_failed`; an operator-visible alert triggers; auto-void after 24h if not resolved.

---

### User Story 4 — Customer pays via COD (Priority: P2)

A customer selects COD at checkout (where eligible per market and order constraints). The order is placed with `payment_method=cod`; the Payment is created in `pending_collection_on_delivery`; on successful delivery (per spec 026), the courier confirms cash collection; an operator marks the payment `captured`.

**Why this priority**: COD remains a top-three method in EG; required by Principle 13.

**Independent Test**: Place a COD order for an EG address; observe `Payment.state=pending_collection_on_delivery`; simulate delivery confirmation + cash receipt; operator marks captured; observe `payment.captured` event with `method=cod`.

**Acceptance Scenarios**:

1. **Given** a customer at checkout with COD eligibility resolved (per market + cart-total + address), **When** they choose COD, **Then** the Payment is created in `pending_collection_on_delivery` and the order proceeds.
2. **Given** the courier delivers and collects cash, **When** the operator marks "cash received" via admin UI (or per future courier-integration auto-flow, deferred to 1.5), **Then** the Payment transitions to `captured`.
3. **Given** the courier reports cash not collected (customer refused / unavailable), **When** the operator records that, **Then** the Payment moves to `failed` with reason `cod_collection_failed` and the order returns to the warehouse-return flow (spec 026).

---

### User Story 5 — Customer pays via bank transfer with manual reconciliation (Priority: P2)

A customer selects bank transfer at checkout. The platform displays bank details and a unique payment reference. Customer transfers; the operator matches the bank statement entry against the reference; marks the Payment captured.

**Why this priority**: Bank transfer is required for B2B and certain large-cart consumer flows; Principle 13 lists it.

**Independent Test**: Create a bank-transfer Payment; verify the unique reference is displayed and stored; manually mark "received" via admin; observe `payment.captured`.

**Acceptance Scenarios**:

1. **Given** a customer selects bank transfer, **When** the order is placed, **Then** the Payment is `pending_bank_transfer` and a unique reference (UUID + market prefix) is shown.
2. **Given** the operator reconciles a bank statement entry against the reference, **When** they mark the Payment captured, **Then** the state transitions and `payment.captured` publishes.
3. **Given** the bank transfer is not received within the configured window (default 72h), **When** the timeout reconciler runs, **Then** the Payment is `expired` and the order is cancelled (per spec 011 cancellation flow).

---

### User Story 6 — Operator runs daily reconciliation against provider ledgers (Priority: P1)

Every day at 03:00 KSA, a job pulls the previous day's settlement ledger from each active provider (HyperPay, Paymob, Tabby, Tamara, Valu, etc.), matches each ledger row against the `payments` table by provider message-id, and surfaces exceptions: orphan provider rows, missing-on-provider rows, amount-mismatch rows.

**Why this priority**: Reconciliation is required by Principle 13 and is the only assurance that the platform's internal ledger matches what providers actually settled. Without it, settlement drift is invisible until it's a financial problem.

**Independent Test**: Inject one provider-ledger row with no matching internal Payment; run the reconciliation job; verify a `ReconciliationException` row is created with reason `orphan_provider_row`; operator opens the exception queue and resolves with documented action.

**Acceptance Scenarios**:

1. **Given** the daily reconciliation job runs, **When** it pulls each provider's ledger via API or signed CSV, **Then** every internal Payment with `state=captured` from yesterday is matched 1:1 with a provider ledger row by `(provider_id, provider_message_id)`.
2. **Given** an internal `captured` Payment has NO matching provider row, **When** the matcher runs, **Then** an exception is raised with reason `missing_on_provider`; alert fires; operator must investigate.
3. **Given** a provider ledger row has NO matching internal Payment, **When** the matcher runs, **Then** an exception is raised with reason `orphan_provider_row`.
4. **Given** an amount mismatch is found, **When** the matcher runs, **Then** an exception is raised with reason `amount_mismatch` showing both amounts; operator decides correction.
5. **Given** an exception is opened, **When** the operator marks resolved with documented action (`refund_issued`, `internal_correction`, `provider_correction_requested`, `accepted_loss`), **Then** the resolution is audit-logged with operator id.

---

### User Story 7 — Operator replays a missed webhook after a downstream incident (Priority: P2)

A backend incident caused webhook handler downtime for 30 minutes. After recovery, an operator triggers webhook replay against the incident window for affected providers; the system fetches missing webhook events from each provider's API (where supported) and processes them through the normal handler with idempotency protection.

**Why this priority**: Webhook replay is required by Principle 13. Without it, missed webhooks during incidents cause silent payment-state staleness.

**Independent Test**: Stop the webhook handler for 5 minutes during a synthetic stream of webhook traffic; restart; trigger replay for the lost window; verify all events are re-processed; verify duplicates are no-ops via `webhooks_received` idempotency.

**Acceptance Scenarios**:

1. **Given** a configured provider supports webhook replay (API endpoint that re-fetches events for a window), **When** an operator triggers replay for `(provider, time_window)`, **Then** events are re-fetched and processed by the same handler; `webhooks_received` PK enforces idempotency.
2. **Given** a provider does NOT support replay, **When** the operator attempts replay, **Then** the system returns "not supported" and recommends manual reconciliation via the next daily reconciliation run.
3. **Given** a replay runs, **When** it completes, **Then** an audit event `payment.webhook_replay` records the operator, time window, provider, and re-processed-event count.

---

### User Story 8 — Auditor reviews 365-day payment history with PCI-scope evidence (Priority: P3)

An auditor requests evidence of PCI scope (SAQ-A) compliance: that no PAN, CVV, or full track data is stored. The auditor queries `payments` schema and confirms cardholder fields are absent; reviews provider-egress payload structure; confirms hosted-fields tokenization.

**Independent Test**: Schema scan for cardholder columns returns zero hits; provider-payload code paths reviewed; tokenization-only flow demonstrated end-to-end.

**Acceptance Scenarios**:

1. **Given** the schema scan, **When** the auditor checks for PAN/CVV-shaped columns, **Then** zero columns store cardholder data.
2. **Given** the egress code paths, **When** reviewed, **Then** only tokens + customer-id + amount + currency + reference flow to providers; no card data leaves hosted fields.
3. **Given** an audit-log query, **When** executed for the past 365 days, **Then** every payment-state transition is preserved.

---

### Edge Cases

- **Customer abandons hosted-fields page mid-tokenization**: Payment stays `pending_authorization`; reconciler ages it out at 30 min → `expired`; cart preserved.
- **Provider returns `captured` but our DB write fails**: idempotency layer plus a "captured_but_unrecorded" reconciliation exception catches this on next reconciliation cycle.
- **Webhook arrives before initial-API response**: handler tolerates out-of-order; idempotency ensures consistency; audit captures both timestamps.
- **BNPL provider auto-declines based on internal credit**: Payment moves to `failed` with reason `bnpl_declined`; customer never sees the credit-decision detail (privacy).
- **Payment captured but order subsequently cancelled before fulfillment**: spec 011 publishes `order.cancelled`; 027 subscribes and triggers a refund via the same provider; refund-failed cases land in exception queue.
- **Refund amount > captured amount**: rejected by 027; partial refund up-to-captured allowed; over-refund attempts return 422 with audit.
- **Currency mismatch (cart in SAR, provider settles in USD)**: out of scope at v1 — providers selected for v1 settle in market currency; FX deferred to Phase 1.5 if needed.
- **Provider sends a "chargeback" event**: `payment.chargeback` audit event; Payment state moves to `chargeback_received`; operator-driven dispute workflow (manual at v1).
- **3DS challenge required**: hosted-fields flow handles 3DS in-provider; our system sees only the post-3DS result. No native 3DS handling code at v1.
- **STC Pay timeout**: STC Pay's flow uses an OTP/notification-driven confirmation; 5-min TTL; on timeout, Payment `expired`.
- **COD address change post-placement**: spec 026 allows; 027's COD record updates the delivery target via `Payment.delivery_target_updated_at`; cash-collection still gated by 026's delivery confirmation.

---

## User Roles

| Role | Responsibilities | Permissions |
|---|---|---|
| **Customer** | Select payment method; complete tokenization or external redirect; view own payment history. | Read own payments; trigger retry; cannot read provider message-ids (privacy). |
| **Payments operator** (admin) | Configure providers + routing; resolve reconciliation exceptions; trigger refunds + webhook replay; review chargebacks. | Full CRUD on payment routing + ops actions. |
| **Finance auditor** | Read-only access to payments + reconciliation + audit-log. | Read all payment + reconciliation rows; export. |
| **Warehouse / delivery operator** (per spec 026) | For COD: mark cash received / refused. | Update Payment state for COD only via specific endpoint. |
| **System (event subscriber)** | Consume order events; create payments; consume provider webhooks; run reconciliation. | Internal. |

---

## Business Rules

1. **BR-1 — PCI scope is SAQ-A.** No PAN, CVV, or full track data is stored at any time. Hosted-fields / tokenization only. Verified by schema scan (AC-4) + payload-builder review (AC-35).
2. **BR-2 — Per-market provider routing.** Every (market, method) has a primary + optional backup provider configured.
3. **BR-3 — Idempotency mandatory.** Every payment-creation call carries an idempotency key derived from `(order_id, method, attempt_id)`. Duplicate calls return the original Payment.
4. **BR-4 — Webhook idempotency.** `(provider_id, provider_message_id, event_kind)` composite PK in `payments.webhooks_received`.
5. **BR-5 — Retries on transient failures only.** 5xx + network timeouts retry up to 3 attempts with backoff. 4xx (e.g., declined, invalid card) do NOT retry — those are user-actionable.
6. **BR-6 — Each retry is a new Payment row.** Failed Payments are immutable; retry creates a fresh row referencing the same order. The order's `payment_status` reflects the latest attempt.
7. **BR-7 — Reconciliation runs daily at 03:00 KSA.** Exception queue retains 90 days operator-active; audit log retains ≥ 365 days.
8. **BR-8 — Refund constraints.** Refund amount ≤ captured amount. Multiple partial refunds allowed; sum ≤ captured. Over-refund attempts rejected (422).
9. **BR-9 — COD eligibility per market + cart.** Configurable per market: max cart total, address eligibility, customer trust score (deferred to 1.5; default-on at v1).
10. **BR-10 — Bank transfer manual reconciliation.** Operator action required to capture; 72h auto-expire if no match.
11. **BR-11 — Hard-delete forbidden.** Payments, refunds, exceptions, webhook records soft-deleted only.
12. **BR-12 — Provider credentials in KV only.** No secret in `appsettings*.json`; sourced via E1's `AddLayeredConfiguration()`.
13. **BR-13 — No auto-cascade across providers.** Failover requires explicit operator action OR `auto_failover_enabled=true` (v1 default: false; matches 025/026).
14. **BR-14 — PII minimization at provider egress.** Only tokens + amount + currency + recipient minimum (name + masked phone) + order reference.
15. **BR-15 — Audit every state transition + admin action + reconciliation exception.**
16. **BR-16 — Webhook replay only when supported.** Per-provider capability matrix; manual reconciliation fallback.
17. **BR-17 — Soft rate limit on cards.** Default 5 attempts per (customer, 24h); customizable; protects against brute-force scenarios.
18. **BR-18 — Chargebacks are operator-managed at v1.** Audit-logged; manual workflow; no automated dispute response.
19. **BR-19 — FX out of scope.** Each provider settles in market currency. FX deferred to 1.5.
20. **BR-20 — `payment.captured` is the single source of truth for revenue recognition.** Order/invoice modules subscribe; no other event signals payment success.

---

## User Flow

### Flow 1 — Card payment (Mada/Visa/MC) via HyperPay (KSA)
```
Customer at checkout selects Mada → 027 returns hosted-fields config (provider=hyperpay, public_key, session_id)
Customer enters card in hosted iframe → provider tokenizes → returns token to 027
027 creates Payment(state=pending_authorization, idempotency_key)
027 calls hyperpay.AuthorizeAndCapture(token, amount, currency, order_ref)
On success → Payment.state=captured → publish payment.captured → spec 011 advances order
On 4xx → Payment.state=failed with declined reason → customer retry
On 5xx → retry policy → on success/exhaustion as above
Webhook arrives → idempotent confirmation
```

### Flow 2 — BNPL Tabby (KSA)
```
Customer selects Tabby → Payment(state=pending_external_redirect)
027 returns Tabby checkout URL → customer redirects
On Tabby webhook (signed) → 027 transitions Payment per status (authorized/declined/etc.)
On captured → publish payment.captured → order advances
On TTL expiry without webhook → Payment.state=expired, cart preserved
```

### Flow 3 — COD
```
Customer selects COD (eligibility checked) → Payment(state=pending_collection_on_delivery)
Order placed → fulfillment flow runs (spec 026)
On delivery + courier confirms cash → operator marks captured (manual at v1)
Payment.state=captured → publish payment.captured → invoice issued
```

### Flow 4 — Bank transfer
```
Customer selects bank transfer → Payment(state=pending_bank_transfer, reference=UUID-prefix)
Customer transfers manually with reference
Operator reviews bank statement → matches reference → marks captured
Payment.state=captured → publish payment.captured
72h timeout without match → state=expired → order cancelled
```

### Flow 5 — Daily reconciliation
```
03:00 KSA scheduler runs ReconciliationJob
For each provider: pull yesterday's ledger
Match each ledger row to internal Payment by (provider_id, provider_message_id)
Mismatches → ReconciliationException with reason
Operator reviews queue in admin → resolves with documented action
```

### Flow 6 — Webhook replay
```
Operator opens admin → selects (provider, time_window)
027 calls provider's events-API for the window
Each event reprocessed via the standard webhook handler (idempotent)
Audit event payment.webhook_replay records the operation
```

### Flow 7 — Refund
```
order.refund_initiated published (spec 011) OR operator triggers refund manually
027 resolves the original Payment + provider
027 calls provider.RefundAsync(amount, reason)
Refund row created; on provider confirmation → Refund.state=completed
Publish payment.refunded → spec 025 notifies customer
```

---

## UI States

Customer-facing payment screens (Lane B): hosted-fields integration; redirect-and-return; method-selector; failed-with-retry; pending-bank-transfer-instructions; cod-confirmation. All AR + EN, RTL-aware.

Admin-facing surfaces (Lane B): provider routing; reconciliation queue with exception drill-down; webhook replay tool; refund tool; chargeback log. Per Principle 27 every UI has loading / empty / error / success / restricted states.

---

## Data Model

### New tables under `payments` schema

| # | Table | Purpose |
|---|---|---|
| 1 | `payments` | Per-attempt payment record |
| 2 | `refunds` | Per-refund record (1:N from payments) |
| 3 | `webhooks_received` | Idempotency on `(provider_id, provider_message_id, event_kind)` |
| 4 | `provider_routing` | Per-(market, method) primary + backup configuration |
| 5 | `payment_methods_market_config` | Per-market enabled methods + eligibility |
| 6 | `reconciliation_runs` | One row per daily run (start, end, totals) |
| 7 | `reconciliation_exceptions` | Per-exception detail; operator-resolution workflow |
| 8 | `chargebacks` | Per-chargeback record |
| 9 | `pci_scope_events` | Audit-of-record for PCI compliance evidence (config changes affecting PCI scope) |
| 10 | `bank_transfer_references` | Per-Payment unique references for bank transfer matching |
| 11 | `cod_collection_log` | Per-COD-Payment delivery + cash-receipt record |
| 12 | `idempotency_keys` | Cache table for create-payment idempotency lookups |

All inherit the four mandatory columns.

### Payment state machine

```
pending_authorization
  ├─→ authorized → capture_failed → expired (auto-void after 24h) | captured
  ├─→ captured (synchronous capture, common for KSA cards)
  ├─→ failed (4xx, terminal)
  └─→ expired (no provider response within window)

pending_external_redirect (BNPL, some Apple Pay flows)
  ├─→ captured (provider webhook authorized)
  ├─→ failed (declined)
  └─→ expired (TTL elapsed)

pending_collection_on_delivery (COD)
  ├─→ captured (operator confirms cash received)
  └─→ failed (cod_collection_failed)

pending_bank_transfer
  ├─→ captured (operator matches statement entry)
  └─→ expired (72h timeout)

captured
  ├─→ refunded (full refund processed) | partially_refunded (1+ partial refunds) | chargeback_received (provider chargeback)
```

### Refund state machine (separate from Payment.state)
```
pending → completed | failed
```

### Reconciliation exception resolution
```
open → resolved (`refund_issued`, `internal_correction`, `provider_correction_requested`, `accepted_loss`)
```

### Audit-event additions

`payment.created`, `payment.authorized`, `payment.captured`, `payment.failed`, `payment.expired`, `payment.refunded` (with amount), `payment.partially_refunded`, `payment.chargeback`, `payment.webhook_replay`, `provider.degraded`, `provider.failover`, `reconciliation.run_started`, `reconciliation.run_completed`, `reconciliation.exception_opened`, `reconciliation.exception_resolved`, `pci_scope.config_changed` (any change to KV slot mapping, hosted-fields domain, or scope-affecting config), `secret.placeholder_replaced` (E1-inherited).

Retention ≥ 365 days for audit events.

### Cross-references

- `payments.payments.order_id` → `orders.orders.id` (spec 011).
- E1 KV slots populated by 027 (12 slots, three keys per market × provider per channel as enumerated in E1's data-model.md):
  - KSA cards: `payments/sa/hyperpay/{api-key, api-secret, webhook-signing-key}`
  - KSA cards backup: `payments/sa/tap/{...}`
  - KSA BNPL: `payments/sa/tabby/{...}`, `payments/sa/tamara/{...}`
  - EG cards: `payments/eg/paymob/{...}`
  - EG cards backup: `payments/eg/kashier/{...}`
  - EG BNPL: `payments/eg/valu/{...}`

---

## Validation Rules

- **V-1** PCI scope: schema scan for cardholder columns must return zero matches (BR-1).
- **V-2** Idempotency: every payment-create call MUST carry `idempotency_key`; duplicates return the original.
- **V-3** Webhook signature: fail-closed (401) on mismatch.
- **V-4** Retries on 5xx + network only; 4xx never retry.
- **V-5** Refund amount ≤ captured amount; multiple partial refunds: sum ≤ captured.
- **V-6** Provider routing: primary ≠ backup.
- **V-7** Per-method market eligibility: e.g., Mada only valid for `market=sa`; Meeza only `market=eg`; Tabby/Tamara only `sa`; Valu only `eg`.
- **V-8** PII egress filter: only token + amount + currency + minimum recipient fields permitted in provider payloads (BR-14).

---

## API / Service Requirements

### S-1 Customer endpoints
| Endpoint | Method | Auth |
|---|---|---|
| `/payments/methods?market=&cart_total=` | GET | customer JWT |
| `/payments` | POST | customer JWT (creates Payment + returns hosted-fields config or redirect URL) |
| `/payments/{id}/retry` | POST | customer JWT (creates a NEW Payment for the same order) |
| `/payments/{id}` | GET | customer JWT (own payments only) |
| `/payments/me/history` | GET | customer JWT |

### S-2 Admin endpoints (under `/admin/payments/...`)
| Endpoint | Method | Permission |
|---|---|---|
| `/provider-routing` | GET, PUT | `payments-operator` |
| `/provider-routing/{market}/{method}:failover` | POST | `payments-operator` |
| `/method-config` | GET, PUT | `payments-operator` |
| `/payments` | GET (filterable) | `payments-operator`, `auditor` |
| `/payments/{id}:refund` | POST `{amount, reason}` | `payments-operator` |
| `/payments/{id}/cod:mark-captured` | POST | `warehouse-operator` |
| `/payments/{id}/cod:mark-failed` | POST | `warehouse-operator` |
| `/payments/{id}/bank-transfer:mark-captured` | POST | `payments-operator` |
| `/reconciliation/runs` | GET | `payments-operator`, `auditor` |
| `/reconciliation/exceptions` | GET (filterable) | `payments-operator`, `auditor` |
| `/reconciliation/exceptions/{id}:resolve` | POST `{action, notes}` | `payments-operator` |
| `/webhook-replay` | POST `{provider, from, to}` | `payments-operator` |
| `/chargebacks` | GET, PATCH | `payments-operator` |

### S-3 Webhook endpoints
`/payments/webhooks/{hyperpay|tap|paymob|kashier|tabby|tamara|valu}` — signature-validated, idempotent.

### S-4 Internal subscribers
- `OrderConfirmedSubscriber` (creates Payment if not pre-created at checkout — defensive)
- `OrderCancelledSubscriber` (triggers refund if Payment is `captured`)
- `RefundInitiatedSubscriber` (consumes `order.refund_initiated`)

### S-5 Emitted events
- `payment.captured` → consumed by spec 011 (order), spec 012 (invoice), spec 025 (notification)
- `payment.failed`, `payment.refunded`, `payment.partially_refunded`, `payment.expired`, `payment.chargeback` → consumed by spec 025

### S-6 Provider abstraction
`IPaymentProvider` interface with `CreatePaymentAsync`, `RefundAsync`, `GetPaymentStatusAsync` (poll), `ValidateWebhookSignature`, `ParseWebhookEvent`, `FetchSettlementLedger(date_range)`, `ReplayWebhooks(date_range)` (capability-flagged). One impl per provider under `Modules/Payments/Providers/{HyperPay,Tap,Paymob,Kashier,Tabby,Tamara,Valu}/`.

---

## Acceptance Criteria

### Foundations
- **AC-1**: Migrations for 12 tables in `payments` schema apply clean.
- **AC-2**: All 12 tables carry the four mandatory columns.
- **AC-3**: 21 ADR-007-aligned KV slots populated (7 providers × 3 keys each: HyperPay, Tap, Tabby, Tamara for KSA; Paymob, Kashier, Valu for EG; each provider's `api-key` + `api-secret` + `webhook-signing-key`). E1's data-model.md §2 reserved a 12-placeholder-slot baseline; 027 extends within the same `payments/<market>/<provider>/<key>` taxonomy with 9 additional slots (no taxonomy change). Each population emits `secret.placeholder_replaced` audit event.
- **AC-4**: PCI scope evidence: schema scan returns zero PAN/CVV-shaped columns (V-1).

### Card payments (KSA + EG)
- **AC-5**: Mada test card via HyperPay completes `pending_authorization → captured` within 10s; `payment.captured` event published.
- **AC-6**: Visa test card via Paymob (EG) completes similarly.
- **AC-7**: 5xx provider response triggers retry sequence (3 attempts, 1s/3s/9s); on exhaustion, Payment `failed` with reason `provider_unavailable`.
- **AC-8**: 4xx (declined) does NOT retry (BR-5).
- **AC-9**: Idempotent create (V-2): duplicate request returns the original Payment without a second provider call.

### Apple Pay / Mada / STC Pay (KSA) / Meeza (EG)
- **AC-10**: Apple Pay flow via HyperPay completes captured.
- **AC-11**: STC Pay flow completes (provider OTP-driven); 5-min TTL behavior verified.
- **AC-12**: Meeza via Paymob completes captured.

### BNPL
- **AC-13**: Tabby BNPL: redirect → webhook → captured within 60s.
- **AC-14**: Tamara BNPL: same flow.
- **AC-15**: Valu BNPL (EG): same flow.
- **AC-16**: BNPL decline → Payment `failed` with reason `bnpl_declined`; customer is offered fallback methods.

### COD + bank transfer
- **AC-17**: COD eligibility resolves per market + cart-total; ineligible carts hide COD.
- **AC-18**: COD: courier-confirmation → operator marks captured → state advances + event publishes.
- **AC-19**: Bank transfer: unique reference shown to customer; operator reconciliation marks captured.
- **AC-20**: Bank transfer 72h timeout → Payment `expired` → order cancelled.

### Retry + refund
- **AC-21**: Failed Payment → customer retry creates a NEW Payment row (BR-6); both reference same order.
- **AC-22**: Refund: full refund → Payment.state=`refunded`; partial refund → `partially_refunded` with sum tracked.
- **AC-23**: Refund attempt > captured rejects with 422 + audit (V-5).

### Webhooks + replay
- **AC-24**: Webhook signature fail-closed (401 on invalid).
- **AC-25**: Idempotent webhook re-delivery via PK does not re-publish events.
- **AC-26**: Operator triggers replay for a window; provider events re-fetched and processed; idempotency holds; `payment.webhook_replay` audit emitted.
- **AC-27**: Provider that doesn't support replay returns "not supported" with operator-visible explanation.

### Reconciliation
- **AC-28**: Daily reconciliation runs at 03:00 KSA; covers all active providers.
- **AC-29**: Orphan provider row → exception with reason `orphan_provider_row`.
- **AC-30**: Missing-on-provider Payment → exception `missing_on_provider`.
- **AC-31**: Amount-mismatch → exception `amount_mismatch` with both amounts.
- **AC-32**: Operator resolves an exception with one of four documented actions; resolution is audit-logged.

### Failover + ops
- **AC-33**: Manual provider failover: primary↔backup swap; new payments use the swapped provider; `provider.failover` audit.
- **AC-34**: Auto-failover defaults to `false`; verified per (market, method) row in `provider_routing`.

### Compliance, PII, residency
- **AC-35**: PII egress sweep: every provider call carries only the BR-14 minimum payload; CI guard scans payload-builder code paths and returns zero violations.
- **AC-36**: Provider credentials all read from KV via `AddLayeredConfiguration()`; CI guard rejects appsettings drift.
- **AC-37**: Audit-log query for 365 days returns every state transition + admin action.

### Cross-spec
- **AC-38**: ADR-007 flipped to `Accepted` in `CLAUDE.md` with the v1 stack documented (HyperPay+Tap KSA cards, Paymob+Kashier EG cards, Tabby+Tamara KSA BNPL, Valu EG BNPL, COD + bank transfer native, PCI scope SAQ-A). Fingerprint bumped.
- **AC-39**: Spec 025 receives `payment.captured`, `payment.failed`, `payment.refunded` and dispatches localized notifications.
- **AC-40**: Spec 011 (order) and spec 012 (invoice) consume `payment.captured` correctly (verified via end-to-end test).

---

## Success Criteria

- **SC-1**: 99% of authorize-and-capture attempts on healthy providers complete within 10s p95 in steady state.
- **SC-2**: Daily reconciliation completes within 30 min and surfaces all exceptions in the operator queue.
- **SC-3**: Reconciliation exception resolution: 95% of exceptions resolved within 48h.
- **SC-4**: Zero PAN/CVV/track-data in any storage or egress (continuous CI sweep).
- **SC-5**: Failover MTTR ≤ 10 min from operator decision to first new-routing payment.
- **SC-6**: Payment-failure recovery: ≥ 30% of failed payments succeed on a retry attempt.
- **SC-7**: Webhook replay completes within 5 min for a 1-hour incident window.
- **SC-8**: Refund settlement: 95% complete within 48h of refund initiation.
- **SC-9**: Cardholder data leakage incidents: zero, in any 12-month window.
- **SC-10**: Audit completeness: every payment state transition produces exactly one audit row (zero gaps in weekly completeness check).

---

## Phase Assignment & Dependencies

**Phase 1E — spec 027.** Hard dep: 010 (cart-checkout), 012 (tax-invoice), E1. Soft dep: 011 (order — bidirectional events).

Downstream: 025 (payment notifications), 029 (load + chaos drills, PCI scope review).

---

## Assumptions

- v1 PCI scope is SAQ-A (no PAN/CVV/track storage; hosted-fields only).
- Each provider settles in market currency; FX deferred to 1.5.
- Multi-currency cart deferred.
- 3DS handled in-provider; no native 3DS code at v1.
- Chargebacks are operator-managed at v1; no automated dispute response.
- Webhook replay capability matrix: Tabby + Tamara + HyperPay support replay; Paymob support TBD; COD + bank-transfer don't apply.
- Bank-transfer matching at v1 is manual (no bank-API integration).

---

## Open Items

All five clarify items resolved (Clarifications section above). No open items blocking `/speckit-plan`.
