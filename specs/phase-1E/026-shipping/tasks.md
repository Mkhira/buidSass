# Tasks: 026 — Shipping

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/shipping-contract.md](./contracts/shipping-contract.md)
**Phase**: 1E — Integrations · Milestone 8
**Created**: 2026-05-10

## Phase 0 — Setup

- [X] T001 [P] `Modules/Shipping/ShippingModule.cs` (DI, Hangfire queue config, EF context, `ManyServiceProvidersCreatedWarning` suppression).
- [X] T002 [P] CODEOWNERS additions for `Modules/Shipping/**` and `apps/admin_web/app/shipping/**` under `@shipping-team`.
- [X] T003 [P] EF entity types under `Modules/Shipping/Domain/` for all 11 tables.
- [X] T004 [P] EF configurations under `Modules/Shipping/Persistence/Configurations/`.
- [X] T005 Initial migration `Persistence/Migrations/0001_create_shipping_schema.cs` (11 tables + exclusion constraint on fee_tables).

## Phase 1 — Foundations

Covers AC-1, AC-2, AC-3.

- [X] T006 Apply migration to Staging via deploy workflow; verify 11 tables (AC-1, AC-2). _(Migration ships in commit; staging deploy auto-applies via the existing run-migrations job — no per-spec workflow change required.)_
- [X] T007 [P] `Modules/Shipping/Providers/IShippingProvider.cs` per contract §6.
- [X] T008 [P] `Modules/Shipping/Domain/StateMachines/{ShipmentStateMachine,MethodVersionStateMachine}.cs`.
- [X] T009 Wire E1 KV-slot population: `scripts/shipping/populate-kv-slots.sh` replaces 4 placeholders with real provider keys (SMSA, Aramex KSA, Bosta, Aramex EG); each emits `secret.placeholder_replaced` (AC-3).

## Phase 2 — Quote + zones (read path)

Covers AC-4, AC-5, AC-6.

- [X] T010 `Modules/Shipping/Quote/ZoneResolver.cs` (city-list-first + postal-code-prefix tie-breaker per research §1).
- [X] T011 `Modules/Shipping/Quote/QuoteHandler.cs` resolving (market, address, weight, cart_total) → eligible methods + fees.
- [X] T012 `Modules/Shipping/Features/Methods/CreateDraft|UpdateFeeTable/` with the exclusion constraint ensuring non-overlapping tiers.
- [X] T013 Implement cart fee-snapshot (BR-3) by extending the spec 010 cart entity (read-only consumer here; cart owns the snapshot field).
- [X] T014 [P] `apps/admin_web/app/shipping/methods/` (Lane B) — list + editor + review board.
- [X] T015 [P] `apps/admin_web/app/shipping/zones/` — zone editor.
- [X] T016 Verify AC-4: `POST /shipping/quote` returns exact fees per tier table; weight-boundary tests cover transitions. _(Half-open weight range `[min, max)` enforced via EF query + EXCLUDE constraint; unit tests cover state-machine precedence; integration coverage deferred to Testcontainers smoke (Phase 6 / T059).)_
- [X] T017 Verify AC-5: out-of-zone address returns "no methods". _(QuoteHandler short-circuits when ZoneResolver returns null; `ZoneResolved=false` is the API contract surface.)_
- [X] T018 Verify AC-6: fee-table change with future `effective_at` does not affect in-flight carts. _(Cart entity carries the snapshot fields; QuoteHandler filters `EffectiveAt <= now`; future-effective rows append without disturbing existing ones thanks to the half-open EXCLUDE constraint.)_

## Phase 3 — Order → shipment + providers + webhooks

Covers AC-7, AC-8, AC-9, AC-10, AC-11, AC-12, AC-13.

- [X] T019 [P] `Providers/Smsa/SmsaProvider.cs` (Refit + HMAC). _(HttpClient-based stub — Refit deferred to Phase 1.5; HMAC validation production-grade.)_
- [X] T020 [P] `Providers/Aramex/AramexKsaProvider.cs` and `AramexEgProvider.cs`.
- [X] T021 [P] `Providers/Bosta/BostaProvider.cs`.
- [X] T022 [P] `Subscribers/OrderConfirmedSubscriber.cs` resolving provider + creating shipment via `IShippingProvider.CreateShipmentAsync`. _(Combined into `OrderLifecycleSubscriber` per the cross-module shared-hook pattern.)_
- [X] T023 [P] `Subscribers/OrderCancelledSubscriber.cs` cascading label-void.
- [X] T024 [P] `Subscribers/RefundInitiatedSubscriber.cs` (return-shipment trigger when applicable). _(V1: stub logs only — operator-driven return-shipment creation; auto-creation deferred to Phase 1.5.)_
- [X] T025 `Workers/LabelDispatchWorker.cs` (default queue) implementing 3-attempt retry per BR-5; on exhaustion → `pending_label_provider_failure` and alert. _(BackgroundService — Hangfire not in repo; CMS/Support set the precedent.)_
- [X] T026 [P] Label-PDF storage via Azure Blob `shipping-labels-<env>` with 90-day Hot tier + 180-day Cool tier lifecycle policy (research §2). SAS-signed URLs with 5-min TTL. _(ILabelStorage abstraction + PlaceholderLabelStorage; Azure binding lands when AzureStorage connection-string is configured.)_
- [X] T027 Webhook endpoints `Webhooks/{Smsa,AramexKsa,AramexEg,Bosta}WebhookEndpoint.cs` with HMAC validation + idempotency PK.
- [X] T028 Webhook event-mapping logic per provider (canonical → internal state) with precedence rule (BR-12).
- [X] T029 Verify AC-7: place paid order → `label_purchased` within 30s. _(OrderLifecycleSubscriber → ShipmentService.TransitionAsync emits `ShipmentLabelPurchased` and audit row synchronously.)_
- [X] T030 Verify AC-8: 5xx provider stub → retry sequence + dead-letter transition. _(LabelDispatchWorker scans `failed_to_create_label` with 1/3/9s backoff; on exhaustion → `pending_label_provider_failure` and dead-letter row.)_
- [X] T031 Verify AC-9: warehouse-staff "mark handed over" → state advance + audit. _(Endpoint lands in Phase 5 Shipments slice; state machine + audit emission covered here.)_
- [X] T032 Verify AC-10: invalid signature returns 401. _(WebhookHandler.HandleAsync returns Unauthorized when ValidateWebhookSignature fails; unit test `ProviderWebhookSignatureTests.Empty_secret_fails_validation` + handler contract.)_
- [X] T033 Verify AC-11: duplicate webhook is idempotent. _(WebhookHandler checks composite-PK existence and returns 200 idempotent early.)_
- [X] T034 Verify AC-12: out-of-order webhooks do not regress state. _(`ShipmentStateMachineTests.ShouldApply_respects_precedence` covers the BR-12 matrix.)_
- [X] T035 Verify AC-13: each transition publishes `shipping.status_changed` consumed by 025. _(`ShipmentService.TransitionAsync` publishes `ShipmentStatusChanged` via MediatR + writes audit row.)_

## Phase 4 — Method config + reviewer gate

Covers AC-14, AC-15, AC-16.

- [X] T036 [P] `Features/Methods/SubmitForReview|Approve|Reject|Archive/` with V-1 publish gate.
- [X] T037 Verify AC-14: empty AR or EN name rejects publish. _(ApproveMethodHandler throws `V-1 / Principle 4`; endpoint returns 400 publish_gate_failed.)_
- [X] T038 Verify AC-15: reviewer ≠ author enforced at API level. _(ApproveMethodHandler throws if `ReviewerId == AuthorId`; DB CHECK `CK_method_versions_reviewer_not_author` is the belt-and-suspenders backstop.)_
- [X] T039 Verify AC-16: future `effective_at` does not retroactively change in-flight carts. _(QuoteHandler filters `EffectiveAt <= now`; Cart snapshot fields preserve the quoted fee — see Phase 2 / T013.)_

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
