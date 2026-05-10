# Tasks: 026 — Shipping

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/shipping-contract.md](./contracts/shipping-contract.md)
**Phase**: 1E — Integrations · Milestone 8
**Created**: 2026-05-10

## Phase 0 — Setup

- [ ] T001 [P] `Modules/Shipping/ShippingModule.cs` (DI, Hangfire queue config, EF context, `ManyServiceProvidersCreatedWarning` suppression).
- [ ] T002 [P] CODEOWNERS additions for `Modules/Shipping/**` and `apps/admin_web/app/shipping/**` under `@shipping-team`.
- [ ] T003 [P] EF entity types under `Modules/Shipping/Domain/` for all 11 tables.
- [ ] T004 [P] EF configurations under `Modules/Shipping/Persistence/Configurations/`.
- [ ] T005 Initial migration `Persistence/Migrations/0001_create_shipping_schema.cs` (11 tables + exclusion constraint on fee_tables).

## Phase 1 — Foundations

Covers AC-1, AC-2, AC-3.

- [ ] T006 Apply migration to Staging via deploy workflow; verify 11 tables (AC-1, AC-2).
- [ ] T007 [P] `Modules/Shipping/Providers/IShippingProvider.cs` per contract §6.
- [ ] T008 [P] `Modules/Shipping/Domain/StateMachines/{ShipmentStateMachine,MethodVersionStateMachine}.cs`.
- [ ] T009 Wire E1 KV-slot population: `scripts/shipping/populate-kv-slots.sh` replaces 4 placeholders with real provider keys (SMSA, Aramex KSA, Bosta, Aramex EG); each emits `secret.placeholder_replaced` (AC-3).

## Phase 2 — Quote + zones (read path)

Covers AC-4, AC-5, AC-6.

- [ ] T010 `Modules/Shipping/Quote/ZoneResolver.cs` (city-list-first + postal-code-prefix tie-breaker per research §1).
- [ ] T011 `Modules/Shipping/Quote/QuoteHandler.cs` resolving (market, address, weight, cart_total) → eligible methods + fees.
- [ ] T012 `Modules/Shipping/Features/Methods/CreateDraft|UpdateFeeTable/` with the exclusion constraint ensuring non-overlapping tiers.
- [ ] T013 Implement cart fee-snapshot (BR-3) by extending the spec 010 cart entity (read-only consumer here; cart owns the snapshot field).
- [ ] T014 [P] `apps/admin_web/app/shipping/methods/` (Lane B) — list + editor + review board.
- [ ] T015 [P] `apps/admin_web/app/shipping/zones/` — zone editor.
- [ ] T016 Verify AC-4: `POST /shipping/quote` returns exact fees per tier table; weight-boundary tests cover transitions.
- [ ] T017 Verify AC-5: out-of-zone address returns "no methods".
- [ ] T018 Verify AC-6: fee-table change with future `effective_at` does not affect in-flight carts.

## Phase 3 — Order → shipment + providers + webhooks

Covers AC-7, AC-8, AC-9, AC-10, AC-11, AC-12, AC-13.

- [ ] T019 [P] `Providers/Smsa/SmsaProvider.cs` (Refit + HMAC).
- [ ] T020 [P] `Providers/Aramex/AramexKsaProvider.cs` and `AramexEgProvider.cs`.
- [ ] T021 [P] `Providers/Bosta/BostaProvider.cs`.
- [ ] T022 [P] `Subscribers/OrderConfirmedSubscriber.cs` resolving provider + creating shipment via `IShippingProvider.CreateShipmentAsync`.
- [ ] T023 [P] `Subscribers/OrderCancelledSubscriber.cs` cascading label-void.
- [ ] T024 [P] `Subscribers/RefundInitiatedSubscriber.cs` (return-shipment trigger when applicable).
- [ ] T025 `Workers/LabelDispatchWorker.cs` (default queue) implementing 3-attempt retry per BR-5; on exhaustion → `pending_label_provider_failure` and alert.
- [ ] T026 [P] Label-PDF storage via Azure Blob `shipping-labels-<env>` with 90-day Hot tier + 180-day Cool tier lifecycle policy (research §2). SAS-signed URLs with 5-min TTL.
- [ ] T027 Webhook endpoints `Webhooks/{Smsa,AramexKsa,AramexEg,Bosta}WebhookEndpoint.cs` with HMAC validation + idempotency PK.
- [ ] T028 Webhook event-mapping logic per provider (canonical → internal state) with precedence rule (BR-12).
- [ ] T029 Verify AC-7: place paid order → `label_purchased` within 30s.
- [ ] T030 Verify AC-8: 5xx provider stub → retry sequence + dead-letter transition.
- [ ] T031 Verify AC-9: warehouse-staff "mark handed over" → state advance + audit.
- [ ] T032 Verify AC-10: invalid signature returns 401.
- [ ] T033 Verify AC-11: duplicate webhook is idempotent.
- [ ] T034 Verify AC-12: out-of-order webhooks do not regress state.
- [ ] T035 Verify AC-13: each transition publishes `shipping.status_changed` consumed by 025.

## Phase 4 — Method config + reviewer gate

Covers AC-14, AC-15, AC-16.

- [ ] T036 [P] `Features/Methods/SubmitForReview|Approve|Reject|Archive/` with V-1 publish gate.
- [ ] T037 Verify AC-14: empty AR or EN name rejects publish.
- [ ] T038 Verify AC-15: reviewer ≠ author enforced at API level.
- [ ] T039 Verify AC-16: future `effective_at` does not retroactively change in-flight carts.

## Phase 5 — Disputes, re-delivery, failover

Covers AC-17, AC-18, AC-19, AC-20.

- [ ] T040 [P] `Features/Shipments/Dispute|CreateReDelivery|VoidLabel/`.
- [ ] T041 [P] `Features/ProviderRouting/Get|Set|Failover/`.
- [ ] T042 [P] `Workers/ReattemptQueuedLabelsWorker.cs` (re-tries `pending_label_provider_failure` shipments against current routing).
- [ ] T043 [P] `Workers/ProviderHealthMonitor.cs` (5-min sliding window; emits `provider.degraded`; auto-failover only if `auto_failover_enabled=true`).
- [ ] T044 [P] `Workers/SlaBreachMonitor.cs` (alerts when shipment age > SLA × 2).
- [ ] T045 [P] `apps/admin_web/app/shipping/{shipments,provider-routing,exception-queue}/` (Lane B).
- [ ] T046 Verify AC-17, AC-18 (3-attempt → return_to_sender_initiated).
- [ ] T047 Verify AC-19: manual failover swap + new shipments use swapped provider.
- [ ] T048 Verify AC-20: `ReattemptQueuedLabelsWorker` re-tries against current provider.

## Phase 6 — Audit + compliance + load + ADR-008

Covers AC-21, AC-22, AC-23, AC-24, AC-25, AC-26.

- [ ] T049 [P] Audit-event emitters at every state-changing transition + admin config change (BR-10).
- [ ] T050 [P] PII redaction layer (BR-15): payload sent to providers carries only required fields; `ship_to_address_redacted_jsonb` masks phone to last-4. CI guard `scripts/ci/check-shipping-no-pii.sh`.
- [ ] T051 Extend secret-pattern guard for SMSA + Aramex + Bosta keys (AC-24).
- [ ] T052 Verify AC-21: query audit-log; assert one row per transition kind.
- [ ] T053 Verify AC-22: 90-day query returns provider attribution.
- [ ] T054 Verify AC-23: PII sweep on payload-builder code paths returns zero leakage.
- [ ] T055 Update ADR-008 in `CLAUDE.md` from `Proposed` to `Accepted`; record SMSA + Aramex (KSA), Bosta + Aramex (EG); note aggregator deferral. Bump fingerprint (AC-25).
- [ ] T056 Verify AC-26: end-to-end test confirms 025 receives `shipping.status_changed` and dispatches localized notification.
- [ ] T057 Hand off to spec 029 for k6 load test (5× RPS); track shipment-creation latency.

## Phase 7 — Polish

- [ ] T058 [P] `Modules/Shipping/Seeding/ShippingV1Seeder.cs` (sample methods AR+EN, zones, fee tables, sample shipments across states).
- [ ] T059 [P] OpenAPI tests for all customer + admin + webhook endpoints in `Tests/Contract/`.
- [ ] T060 Final spec-compliance check: re-read AC-1..AC-26; file gaps as P1 issues.

---

## AC → Task traceability

| AC | Tasks |
|---|---|
| AC-1 | T005, T006 |
| AC-2 | T005, T006 |
| AC-3 | T009 |
| AC-4 | T010, T011, T016 |
| AC-5 | T010, T017 |
| AC-6 | T013, T018 |
| AC-7 | T019, T022, T025, T029 |
| AC-8 | T025, T030 |
| AC-9 | T031 |
| AC-10 | T027, T032 |
| AC-11 | T027, T033 |
| AC-12 | T028, T034 |
| AC-13 | T028, T035, T056 |
| AC-14 | T036, T037 |
| AC-15 | T036, T038 |
| AC-16 | T013, T039 |
| AC-17 | T040, T046 |
| AC-18 | T028, T046 |
| AC-19 | T041, T047 |
| AC-20 | T042, T048 |
| AC-21 | T049, T052 |
| AC-22 | T053 |
| AC-23 | T050, T054 |
| AC-24 | T051 |
| AC-25 | T055 |
| AC-26 | T056 |

Every AC mapped. 60 tasks; 26 marked `[P]`.
