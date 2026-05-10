# Feature Specification: 026 — Shipping

**Feature Branch**: `phase-1E`
**Spec ID**: 026
**Created**: 2026-05-10
**Status**: Draft
**Phase**: 1E — Integrations · Milestone 8
**Input**: Implementation-plan §Phase 1E spec 026 (lines 601–614) — "provider settings, market rules, methods, fees, shipment state mapping, tracking webhooks. ADR-008 Accepted."

---

## Clarifications

### Session 2026-05-10

Five priority questions resolved (recommended-default basis per the agreed orchestrator workflow). All decisions sourced as `default` unless explicitly overridden by user input.

- Q: KSA primary shipping provider at v1 → A: **SMSA Express** (primary). Source: `default`. Rationale: largest KSA-domestic network with strong SLA on dental-grade temperature-controlled shipments where applicable; Aramex KSA reserved as backup. Recorded in `infra/azure/DECISIONS.md`.
- Q: KSA backup shipping provider at v1 → A: **Aramex KSA** (backup). Source: `default`. Failover manual at v1; auto-failover not enabled (matches 025 posture).
- Q: EG primary shipping provider at v1 → A: **Bosta** (primary). Source: `default`. Rationale: highest e-commerce delivery throughput in EG; Aramex EG reserved as backup.
- Q: EG backup shipping provider at v1 → A: **Aramex EG** (backup). Source: `default`.
- Q: Aggregator-layer (Shipox / Flixpro) introduction → A: **Deferred to Phase 2**. Source: `default`. Rationale: launch ships with two providers per market (one primary + one backup); aggregator complexity not justified at single-vendor launch. ADR-008 acceptance documents this scope decision.

ADR-008 transition: `Proposed` → **`Accepted`**. Stack at v1: SMSA + Aramex (KSA), Bosta + Aramex (EG). Aggregator layer explicitly out of scope.

---

## ADR & Constitution Traceability

| Source | Title | How 026 satisfies it |
|---|---|---|
| Principle 4 | Bilingual + RTL editorial | Shipping methods, fees, status copy in AR + EN; tracking page RTL-aware. |
| Principle 5 | Markets EG + KSA | Per-market provider routing + per-market zone configuration + per-market fee tables. |
| Principle 11 | Inventory | Shipment creation respects warehouse readiness signals from spec 008 (inventory). |
| Principle 14 | Shipping | Generic integration layer; provider abstraction; shipment creation, tracking, fee calc, region/zone, delivery estimates, replaceable providers, multi-warehouse-ready. |
| Principle 17 | Order & post-purchase | Shipment state is one of the four orthogonal status fields on `orders` (per spec 011); 026 owns fulfillment state. |
| Principle 24 | State machines | Explicit Shipment state machine (`pending → label_purchased → in_transit → out_for_delivery → delivered ∪ delivery_attempted → returned ∪ failed`) and ShippingMethod-availability rules. |
| Principle 25 | Audit | Method publish, fee changes, provider failover, shipment state transitions, return-to-sender events all audit-logged. |
| Principle 28 | AI-build | Implementation-ready: provider matrix, zone schema, fee-calc formula, audit events, webhook signatures. |
| Principle 29 | Required spec output | All twelve sections present. |
| ADR-008 | Shipping providers | Flipped to **Accepted** in this spec. |
| ADR-010 | Cloud + residency | Shipping metadata + tracking events persisted in KSA Central Postgres; provider egress documented; PII (recipient phone + address) treated as personal data and minimized at egress. |
| Spec 010 | cart-checkout | Provides shipping-fee quote to checkout; consumes ship-to address. **Hard dep**. |
| Spec 011 | order | Order placement triggers shipment creation; shipment state feeds into order's fulfillment status field. **Hard dep**. |
| Spec E1 | infrastructure-integration | KV slots `shipping/sa/<provider>/api-key`, `shipping/eg/<provider>/api-key`. **Hard prerequisite**. |
| Spec 025 | notifications | Emits `shipping.status_changed` events that 025 subscribes to. |

---

## Goal

Deliver a centralized, multi-provider, multi-market shipping abstraction that:

1. Calculates accurate shipping fees per market × zone × method × cart-weight at checkout time.
2. Creates shipments at order placement; persists labels and tracking numbers.
3. Receives provider tracking webhooks and maps them to a unified Shipment state machine.
4. Lets admins configure shipping methods + zones + fees + provider routing without code changes.
5. Provides delivery-attempt + re-delivery handling.
6. Emits `shipping.status_changed` events for downstream consumers (notifications, analytics).
7. Architected so swapping a provider is a config change, not a code change (Principle 14).

026 is **backend-heavy with admin-UI surfaces** for shipping-method config, zone-and-fee management, shipment tracking lookup, and exception/return handling.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Customer sees an accurate shipping fee at checkout (Priority: P1)

A customer in Riyadh adds 3 items to cart and proceeds to checkout. Their address determines the zone (`KSA-Riyadh`); the cart weight (sum of item weights) determines the fee tier; the customer selects "Standard 2-3 day" method. The displayed fee matches the per-method × zone × weight-tier configured rate exactly.

**Why this priority**: Inaccurate fees at checkout cause cart abandonment and trust loss. Without it, the platform cannot accept paid orders.

**Independent Test**: Configure one method (Standard) for KSA-Riyadh zone with rate `0–5kg = 25 SAR`, `5–10kg = 35 SAR`. Add 3 items totaling 4.2kg; checkout shows 25 SAR. Add a 4th item totaling 7.1kg; fee updates to 35 SAR.

**Acceptance Scenarios**:

1. **Given** a cart with shipping address in `KSA-Riyadh` and aggregate weight 4.2kg, **When** the customer enters checkout, **Then** the system resolves zone=`KSA-Riyadh`, eligible methods (Standard + Express), and displays exact fees per the configured tier.
2. **Given** a cart with an address outside any configured zone, **When** the customer attempts checkout, **Then** the system shows "shipping not available to this address" and offers a contact-support CTA; cart cannot proceed.
3. **Given** a method has a per-customer-tier discount (e.g., B2B free shipping over 1000 SAR), **When** the customer's eligibility resolves to that tier, **Then** the displayed fee reflects the discount with a transparent breakdown.
4. **Given** the cart weight changes mid-checkout (item swap), **When** the cart updates, **Then** the fee recalculates within 500ms and reflects the new tier.

---

### User Story 2 — Order placement triggers shipment creation with provider label and tracking (Priority: P1)

A customer completes payment for an order. The order module publishes `order.confirmed`. The shipping module receives this event, calls the configured provider for that market+method (e.g., SMSA for KSA-Standard), receives back a shipping label PDF + tracking number, persists both, and transitions the shipment to `label_purchased` state. The customer immediately sees the tracking number in the order detail page.

**Why this priority**: Without shipment creation, paid orders cannot reach customers. Tight coupling to order placement is required for operational continuity.

**Independent Test**: Place a paid order on Staging with a KSA address; verify within 30s that the shipment row exists with state=`label_purchased`, label_pdf_url is populated, tracking_number is non-empty, and the customer-visible order detail shows the tracking number.

**Acceptance Scenarios**:

1. **Given** an `order.confirmed` event with `order_id` + ship-to address + selected method, **When** the shipping module's `OrderConfirmedSubscriber` consumes it, **Then** it resolves the provider, calls `CreateShipment`, persists the response, and emits `shipping.label_purchased`.
2. **Given** the provider returns a label-creation failure (5xx or business error), **When** the worker handles the failure, **Then** the shipment state is `failed_to_create_label`, the order's fulfillment_status is set to `shipping_creation_failed`, and an operations alert fires; the order remains capturable but no label is issued automatically.
3. **Given** a shipment is in `label_purchased`, **When** the warehouse staff marks "ready for handover" via the admin UI, **Then** the state transitions to `handed_to_carrier` and a `shipping.handed_to_carrier` audit event fires.
4. **Given** a label PDF is generated, **When** an admin opens the shipment detail page, **Then** the label PDF is downloadable and printable in A4/A6 sizes.

---

### User Story 3 — Customer tracks an in-transit shipment in real-time (Priority: P1)

A customer opens the order detail page and sees a tracking timeline: "Label purchased — 2026-05-10 14:30 KSA → Handed to carrier — 14:45 → In transit — 16:02 → Out for delivery — 2026-05-12 09:11 → Delivered — 11:33". The timeline is populated from provider tracking webhooks mapped onto the unified Shipment state machine.

**Why this priority**: Tracking is a top-three trust signal post-purchase. Customers who can self-serve tracking generate fewer support tickets.

**Independent Test**: Send synthetic provider tracking webhooks for a known shipment in sequence (handed → in_transit → out_for_delivery → delivered); confirm the customer-visible timeline updates within 60s of each webhook; confirm `shipping.status_changed` is published per transition.

**Acceptance Scenarios**:

1. **Given** the provider sends a webhook with status `in_transit`, **When** the webhook handler validates the signature and dedupes via `(provider_id, provider_tracking_id, event_kind)`, **Then** the shipment transitions and `shipping.status_changed` is published.
2. **Given** a webhook arrives out of order (e.g., `delivered` before `out_for_delivery`), **When** the handler resolves precedence, **Then** the more-advanced state wins (`delivered > out_for_delivery > in_transit > ...`); earlier statuses are recorded as historical events but do not regress the state.
3. **Given** a duplicate webhook arrives, **When** the handler queries `webhooks_received`, **Then** it is ignored (idempotent); no double-publish of `shipping.status_changed`.
4. **Given** a webhook references an unknown tracking_number, **When** the handler runs, **Then** the event is logged but does not 5xx (provider would retry); a daily reconciliation job catches orphans and alerts.

---

### User Story 4 — Admin configures a new shipping method + zone + fee table (Priority: P2)

An admin opens the shipping config in admin web, creates a new "Express Same-Day" method for the KSA-Riyadh zone with a flat 75 SAR fee for any cart weight, sets the active window to "08:00–14:00 KSA local for same-day eligibility", saves, and confirms it appears at checkout for matching addresses + cutoffs.

**Why this priority**: Operational shipping config must be admin-editable (Principle 14 — replaceable provider, configurable methods). Hardcoding methods would block growth.

**Independent Test**: Create a new method via admin API; place a test cart with a KSA-Riyadh address at 09:00; confirm the new method appears in the eligible-methods list with the configured fee.

**Acceptance Scenarios**:

1. **Given** an admin creates a `ShippingMethod` with name (AR + EN), zone, fee table, eligibility (e.g., cart-min, eligibility-window), **When** they publish, **Then** the method is active and appears in checkout lookups for matching addresses.
2. **Given** an admin updates a fee table, **When** they save with `effective_at` in the future, **Then** the new rates apply only after `effective_at`; in-flight checkouts use the rate at the time of cart creation.
3. **Given** an admin disables a method, **When** they save, **Then** new checkouts no longer show that method; existing in-flight orders proceed unaffected.
4. **Given** a publish gate runs, **When** AR or EN name is empty, **Then** the publish is rejected per Principle 4.

---

### User Story 5 — Customer reports a missing delivery; operator investigates and re-delivers (Priority: P2)

A customer's tracking shows `delivered` but they did not receive the package. They report via the support channel; an operator opens the shipment record, contacts the carrier, marks the shipment as `delivery_disputed`, and either issues a re-delivery (new shipment) or initiates a return-to-sender flow.

**Why this priority**: Delivery exceptions are a major source of customer dissatisfaction. Without an explicit dispute → re-delivery / return path, the platform cannot resolve disputes operationally.

**Independent Test**: Mark a known `delivered` shipment as `delivery_disputed` via operator UI; trigger a re-delivery; confirm a new shipment row exists linked to the original; confirm `shipping.delivery_disputed` audit event fires.

**Acceptance Scenarios**:

1. **Given** a `delivered` shipment, **When** an operator marks it `delivery_disputed`, **Then** the state moves to `delivery_disputed` and an audit event fires; the customer-visible status updates.
2. **Given** a `delivery_disputed` shipment, **When** the operator triggers "create re-delivery", **Then** a new shipment is created with a `parent_shipment_id` link; the original shipment is `re_delivered_pending`.
3. **Given** the carrier confirms the dispute and offers a refund, **When** the operator marks "carrier-refunded", **Then** the shipment is `closed_with_refund` and the order's refund-status is updated via the order module.
4. **Given** a delivery attempt fails (carrier reports `attempted_no_one_home`), **When** the webhook arrives, **Then** the state moves to `delivery_attempted` (with attempt-count increment); after 3 failed attempts, the state is `return_to_sender_initiated`.

---

### User Story 6 — Operator handles a provider outage with manual failover (Priority: P2)

The KSA primary provider (SMSA) is returning 5xx for label-creation requests. New orders queue up in `pending_label`. An operator triggers manual failover via the admin UI; new shipments route to the backup (Aramex KSA); the queued orders are re-attempted against the backup.

**Why this priority**: Provider outages are inevitable. Without an explicit failover path, the platform stalls during incidents.

**Independent Test**: Inject a 5xx stub for SMSA in Staging; confirm shipments queue without crashing; trigger failover; confirm new shipments use Aramex; trigger a re-attempt-queue worker that succeeds against Aramex.

**Acceptance Scenarios**:

1. **Given** SMSA returns 5xx repeatedly, **When** the worker exhausts the per-shipment retry budget (3 attempts), **Then** the shipment transitions to `pending_label_provider_failure` and an operations alert fires.
2. **Given** an operator triggers failover for `(sa, standard)`, **When** they confirm, **Then** `provider_routing` is updated; new shipments use Aramex; an audit event `provider.failover` fires.
3. **Given** failed shipments are pending, **When** the operator clicks "retry pending against current provider", **Then** the worker re-attempts against the (now-Aramex) provider.
4. **Given** the backup provider also fails, **When** retries exhaust, **Then** the shipment moves to `dead_letter_label`; an operator must intervene; auto-cascade is forbidden at v1.

---

### User Story 7 — Auditor reviews 90-day shipment history with provider attribution (Priority: P3)

An auditor requests the last 90 days of shipment history filtered by market, provider, and final state, with provider tracking-id attribution and webhook-event log for traceability.

**Independent Test**: Run audit query for a 90-day window; verify provider attribution + webhook trail.

**Acceptance Scenarios**:

1. Provider attribution per shipment present.
2. Webhook event log preserved per shipment.
3. Audit-log row preserved beyond shipment's hot-window retention (audit ≥ 365 days).

---

### Edge Cases

- **Address validation failure**: provider rejects the ship-to as unreachable → shipment state `failed_to_create_label_invalid_address`; admin notified; customer prompted to fix address before re-trying.
- **Customer cancels order before label**: order state `cancelled` blocks label creation; if a label was already purchased, void the label via provider API and emit `shipping.label_voided`.
- **Refund initiated for delivered order**: order module triggers refund; shipping module is informed via `order.refund_initiated`; if the customer is asked to ship the item back, a return-shipment is created with the customer as origin.
- **Multi-package shipment**: at v1 an order maps to ONE shipment with one tracking number; multi-package shipments deferred to Phase 2 (multi-warehouse).
- **Customer changes address after label purchase**: provider-dependent; some allow address-update requests, others don't. Admin UI exposes a "request address change" action that calls provider API; if denied, an exception path triggers a manual support flow.
- **Cross-market shipping**: out of scope at v1. Address country must match account market. Cross-market deferred to Phase 2.
- **Shipping fee exceeds product value**: warning shown to admin during method config (>50% of typical cart); not blocked, just warned.
- **Provider tracking-number reuse**: provider returns a previously-seen tracking number for a different order → integrity error → alert + manual reconciliation.
- **Dimensional weight vs actual weight**: provider may bill on the higher of the two; v1 fee calculation uses actual weight only; dim-weight surcharge accepted as provider-side cost; reconciliation handled monthly per ops runbook.
- **Carrier strike or extended delivery delay**: shipment age > documented SLA × 2 → automatic alert + suggested customer outreach via 025; audit event `shipping.sla_breach` fires.
- **Aggregator layer requested**: out of scope at v1; aggregator (Shipox / Flixpro) deferred to Phase 2.

---

## User Roles

| Role | Responsibilities | Permissions |
|---|---|---|
| **Customer** | View order's shipment status + tracking timeline; report dispute. | Read own shipments via order. |
| **Warehouse staff** (admin) | Mark shipments as handed-to-carrier; print labels; record physical-handover timestamps. | Update shipment state {label_purchased → handed_to_carrier}. |
| **Shipping operator** (admin) | Configure methods, zones, fees, provider routing; trigger failover; resolve disputes; create re-deliveries; void labels. | Full CRUD on shipping config + ops actions. |
| **Auditor** | Read-only shipment + audit history. | Read shipments + audit-log; no writes. |
| **System (event subscriber)** | Consume order events; create shipments; consume provider webhooks. | Internal. |

---

## Business Rules

1. **BR-1 — Per-market provider routing.** Every market × method has a primary + optional backup provider. Drift requires admin action.
2. **BR-2 — Locale completeness on method publish.** Method names + descriptions MUST have AR + EN populated.
3. **BR-3 — Fee table snapshot at cart creation.** A cart's quoted fee is locked at cart-creation (cart entity stores fee snapshot per spec 010); subsequent fee-table changes do not retroactively change in-flight checkouts.
4. **BR-4 — One shipment per order at v1.** Multi-package shipments deferred to Phase 2.
5. **BR-5 — Retry budget.** Label creation: 3 attempts (1s/3s/9s). Tracking-poll: continuous, idempotent.
6. **BR-6 — Webhook idempotency.** `(provider_id, provider_tracking_id, event_kind, occurred_at)` composite PK in `shipping.webhooks_received`.
7. **BR-7 — Address country must match account market.** No cross-market shipping at v1.
8. **BR-8 — Hard-delete forbidden.** Methods, zones, fee tables, shipments, webhook records soft-deleted only.
9. **BR-9 — Provider credentials from Key Vault only.** No provider key in `appsettings*.json`.
10. **BR-10 — Audit every state transition + admin config change.**
11. **BR-11 — No auto-cascade across providers.** If primary fails, backup engages only after explicit operator action OR after `auto_failover_enabled=true` (clarify-locked default: false at v1, manual failover).
12. **BR-12 — Out-of-order webhook precedence rule.** State precedence: `delivered > delivery_attempted > out_for_delivery > in_transit > handed_to_carrier > label_purchased > pending`. Lower-precedence webhooks recorded as history but do not regress state.
13. **BR-13 — Label PDF retention.** Labels retained 90 days post-delivery; archived afterwards. Tracking numbers retained indefinitely.
14. **BR-14 — Dimensional vs actual weight.** v1 uses actual weight; dim-weight reconciliation manual per runbook.
15. **BR-15 — PII minimization at egress.** Only the recipient name, masked phone, and address are sent to the provider; no national-id / DOB / loyalty data.

---

## User Flow

### Flow 1 — Checkout fee quote
```
Customer adds items to cart → cart aggregates weight + total
  → checkout step: customer enters/selects shipping address
  → shipping.GetEligibleMethodsAndFees(market, address, weight, cart_total)
  → returns list of (method_id, fee, eta_window) for matching zones
  → customer selects method → fee snapshot stored on cart per BR-3
```

### Flow 2 — Order confirmed → shipment created
```
order.confirmed published (spec 011)
  → OrderConfirmedSubscriber consumes
  → resolves provider for (market, method)
  → builds NotificationDispatch-equivalent ShipmentDispatch (PII-minimized per BR-15)
  → calls IShippingProvider.CreateShipmentAsync
  → persists label PDF (Azure Blob) + tracking_number + carrier_account
  → state: pending → label_purchased
  → emits shipping.label_purchased event (consumed by 025 for notification)
```

### Flow 3 — Tracking webhook → state transition
```
Provider POSTs webhook → /shipping/webhooks/{provider}
  → signature validated against KV-stored secret
  → idempotency check via webhooks_received PK
  → parse event_kind → map to internal Shipment.State per provider's mapping table
  → resolve precedence per BR-12
  → if state advances: transition + emit shipping.status_changed
  → audit event recorded
```

### Flow 4 — Method config publish
```
Operator drafts method (AR+EN names, zone, fee table, eligibility)
  → submits → state=in_review
  → reviewer approves → published; previous published of same (market, name) auto-archived
  → cache invalidates → next checkout uses new method
```

### Flow 5 — Provider failover
```
Operator opens routing page → selects (market, method)
  → swaps primary↔backup → confirms
  → audit event provider.failover
  → "retry pending labels against current provider" worker reprocesses queued shipments
```

### Flow 6 — Delivery dispute → re-delivery
```
Customer reports missing delivery → support flow flags shipment
  → Operator marks delivery_disputed
  → Operator decides: re-deliver (new shipment with parent link) | refund (close_with_refund) | leave open
```

---

## UI States

Admin: **Shipping Config**, **Provider Routing**, **Shipment Detail/Tracking** (operator + customer-facing variant), **Exception Queue** (failed labels, disputes). Each surface has loading / empty / error / restricted states per Principle 27. AR + EN editorial copy for all customer-facing strings.

---

## Data Model

### New tables under `shipping` schema

| # | Table | Purpose |
|---|---|---|
| 1 | `shipping_methods` | Method definitions (id, name_ar, name_en, market, eligibility_jsonb, active, created_by) |
| 2 | `shipping_method_versions` | Versioned (state lifecycle), snapshot reference for in-flight carts |
| 3 | `shipping_zones` | Zone definitions (zone_id, market_code, region, postal_codes_jsonb, city_list_jsonb) |
| 4 | `fee_tables` | Per-method × zone fee tiers (method_version_id, zone_id, weight_min_kg, weight_max_kg, fee_amount, currency, effective_at) |
| 5 | `shipments` | Per-order shipment (id, order_id, market_code, method_version_id, provider_id, provider_tracking_id, label_pdf_blob_url, state, carrier_account, ship_to_address_redacted_jsonb, parent_shipment_id, attempts, eta_min, eta_max, created_at, etc.) |
| 6 | `shipment_events` | Per-shipment webhook event history (shipment_id, provider_event_kind, internal_state_at_event, occurred_at, raw_payload_redacted) |
| 7 | `webhooks_received` | Idempotency PK `(provider_id, provider_tracking_id, event_kind, occurred_at)` |
| 8 | `provider_routing` | Per-(market, method) primary + backup provider config (mirrors 025 pattern) |
| 9 | `dead_letter_labels` | Label-creation failures awaiting operator review |
| 10 | `market_schemas` | Per-market shipping config (default zones, postal-code regex, eligible carriers, default eta windows) |
| 11 | `shipment_disputes` | Operator-recorded delivery disputes + resolution |

All inherit four mandatory columns (created_at, updated_at, deleted_at, market_code where applicable).

### Shipment state machine

```
pending
  ↓ (label creation success)
label_purchased
  ↓ (warehouse handover)
handed_to_carrier
  ↓ (provider webhook)
in_transit
  ↓ (provider webhook)
out_for_delivery
  ↓ (provider webhook)
delivered ──→ (terminal)

Side branches:
in_transit / out_for_delivery → delivery_attempted (attempt_count++)
  → after 3 attempts → return_to_sender_initiated → returned_to_sender
delivered → delivery_disputed (operator action) → re_delivered_pending → (new shipment) ∪ closed_with_refund
pending → failed_to_create_label / pending_label_provider_failure → dead_letter_label (operator-only resolution)
any active → label_voided (cancellation cascade)
```

### Audit events (additive on `audit_log_entries`)

`shipping.method_published`, `shipping.method_archived`, `shipping.fee_table_updated`, `shipping.label_purchased`, `shipping.handed_to_carrier`, `shipping.status_changed`, `shipping.delivery_disputed`, `shipping.re_delivery_created`, `shipping.label_voided`, `shipping.label_creation_failed`, `shipping.dead_letter`, `shipping.sla_breach`, `provider.degraded`, `provider.failover`, `secret.placeholder_replaced` (E1-inherited).

### Cross-references

- `shipping.shipments.order_id` → `orders.orders.id` (spec 011).
- `shipping.shipments.method_version_id` → snapshot (BR-3).
- E1 Key Vault slots populated by 026: `shipping/sa/smsa/api-key`, `shipping/sa/aramex/api-key`, `shipping/eg/bosta/api-key`, `shipping/eg/aramex/api-key`. Each emits `secret.placeholder_replaced`.

---

## Validation Rules

- **V-1** Method publish requires AR + EN names non-empty + reviewer ≠ author.
- **V-2** Shipment create requires resolved provider + non-empty ship-to.
- **V-3** Webhook signature validate fail-closed.
- **V-4** Idempotency on `(provider_id, provider_tracking_id, event_kind, occurred_at)`.
- **V-5** Provider routing: primary ≠ backup; failover_threshold ∈ [10,90].
- **V-6** Address country must match account market (BR-7).
- **V-7** Fee-table consistency: tier ranges non-overlapping per (method, zone, currency); minimum tier starts at 0kg.

---

## API / Service Requirements

### S-1 Customer-facing

| Endpoint | Method | Auth |
|---|---|---|
| `/shipping/quote` | POST | customer JWT (or guest with cart token) |
| `/shipping/track/{tracking_number}` | GET | guest (with order-id challenge) or customer JWT |

### S-2 Admin (under `/admin/shipping/...`)

Methods: `methods`, `methods/{id}:submit/:approve/:reject/:archive`, `zones`, `fee-tables`, `provider-routing`, `provider-routing/{market}/{method}:failover`, `shipments` (filterable), `shipments/{id}:mark-handed-over`, `shipments/{id}:dispute`, `shipments/{id}:create-re-delivery`, `dead-letter-labels`, `dead-letter-labels/{id}:retry`.

### S-3 Webhook endpoints

`/shipping/webhooks/{smsa|aramex-ksa|aramex-eg|bosta}` — signature-validated, idempotent.

### S-4 Internal subscribers

`OrderConfirmedSubscriber` (consumes spec 011 `order.confirmed`), `OrderCancelledSubscriber` (cascade label-void), `RefundInitiatedSubscriber` (return-shipment trigger when applicable).

### S-5 Provider abstraction

`IShippingProvider` interface mirrors 025's `INotificationProvider`: `CreateShipmentAsync`, `VoidLabelAsync`, `GetTrackingAsync` (poll), `ValidateWebhookSignature`, `ParseTrackingEvent`. One impl per provider under `Modules/Shipping/Providers/{Smsa,Aramex,Bosta}/`.

---

## Acceptance Criteria

### Foundations
- **AC-1**: Migrations for 11 tables in `shipping` schema apply clean.
- **AC-2**: All 11 tables carry the four mandatory columns.
- **AC-3**: Four ADR-008 KV slots populated; `secret.placeholder_replaced` events fire.

### Quote + checkout
- **AC-4**: `POST /shipping/quote` returns eligible-method list with exact fees per the configured tier table; weight thresholds resolved correctly across tier boundaries.
- **AC-5**: Out-of-zone address returns "shipping not available" with no methods.
- **AC-6**: Cart fee snapshot at cart-creation persists despite mid-checkout fee-table change (BR-3).

### Order → shipment
- **AC-7**: `order.confirmed` triggers shipment creation; label_pdf and tracking_number persisted; state=`label_purchased` within 30s.
- **AC-8**: Provider 5xx during label creation triggers retry sequence (1s/3s/9s); on exhaustion, state=`pending_label_provider_failure` and alert fires.
- **AC-9**: Warehouse staff mark "handed to carrier" → state advances + audit event.

### Tracking + webhooks
- **AC-10**: Webhook signature validated; invalid signature returns 401.
- **AC-11**: Idempotent webhook re-delivery does not double-publish `shipping.status_changed`.
- **AC-12**: Out-of-order webhook precedence (BR-12) preserved; state never regresses.
- **AC-13**: Each state transition publishes `shipping.status_changed` consumed by 025 to update notification subscribers.

### Method config
- **AC-14**: New method publish blocked when AR or EN name empty (V-1).
- **AC-15**: Reviewer ≠ author enforced.
- **AC-16**: Fee-table update with `effective_at` future does not affect in-flight carts.

### Disputes + re-delivery
- **AC-17**: Operator marks delivery_disputed → state advances + audit; re-delivery creates a new shipment linked via `parent_shipment_id`.
- **AC-18**: 3 failed delivery attempts → `return_to_sender_initiated` automatically.

### Provider failover
- **AC-19**: Manual failover swap updates routing; `provider.failover` audit; new shipments use the swapped provider.
- **AC-20**: Re-attempt-pending worker re-tries failed labels against the current routing.

### Audit + retention + compliance
- **AC-21**: Every state-changing action audit-logged.
- **AC-22**: 90-day delivery + audit query returns provider attribution.
- **AC-23**: PII minimization: `ship_to_address_redacted_jsonb` masks phone to last-4; payload sent to provider trims to required fields only.
- **AC-24**: All provider credentials read from KV; CI guard rejects appsettings drift.

### Cross-spec
- **AC-25**: ADR-008 flipped to Accepted with the v1 stack (SMSA + Aramex (KSA), Bosta + Aramex (EG)) recorded in `CLAUDE.md`.
- **AC-26**: 025 receives `shipping.status_changed` events and dispatches the configured notification template per market locale (verified via end-to-end test).

---

## Success Criteria

- **SC-1**: 99% of `order.confirmed` events result in `label_purchased` within 60 seconds in steady state.
- **SC-2**: Tracking-webhook to customer-visible-status latency < 60s p95.
- **SC-3**: Quote-fee inaccuracy rate (vs configured table) = 0 in audit sweep.
- **SC-4**: Zero PAN/CVV/national-id leakage in payloads sent to providers (CI sweep + manual audit).
- **SC-5**: Operator can configure a new method (AR+EN, zone, fee tiers, eligibility, peer review) end-to-end in under 20 minutes.
- **SC-6**: Failover MTTR ≤ 10 minutes from operator decision to first new-routing shipment created.
- **SC-7**: Dead-letter label rate < 0.5% over a 7-day window in steady state.

---

## Phase Assignment & Dependencies

**Phase 1E — spec 026.** Hard dep: 010 (cart-checkout), 011 (order), E1. Soft dep: 008 (inventory) for warehouse-readiness signals (read-only at v1).

Downstream: 025 (status notifications), 027 (refund-initiated may trigger return shipment), 029 (load tests).

---

## Assumptions

- Single-package per order at v1.
- Actual weight (not dim weight) for v1 fee calc.
- Cross-market shipping out of scope.
- Aggregator layer (Shipox/Flixpro) deferred to Phase 2.
- Cold-chain / temperature-controlled shipments out of scope at v1 (some dental items may require this — flagged for Phase 1.5 if regulatory requirement emerges).

---

## Open Items

All five clarify items resolved (Clarifications section above). No open items blocking `/speckit-plan`.
