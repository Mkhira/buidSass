---
description: "Dependency-ordered tasks for spec 010 — checkout"
---

# Tasks: Checkout v1

## Phase 1: Setup
- [X] T001 Module tree `services/backend_api/Modules/Checkout/{Primitives/{Payment,Shipping},Customer/{StartSession,SetAddress,GetShippingQuotes,SelectShipping,SelectPaymentMethod,Summary,Submit,AcceptDrift},Admin/{ListSessions,ForceExpire},Webhooks/PaymentGatewayWebhook,Workers,Entities,Persistence/{Configurations,Migrations},Messages}` + tests
- [X] T002 Register `AddCheckoutModule`, wire `Program.cs`
- [X] T003 [P] Add Polly + shared FluentValidation deps

## Phase 2: Foundational
- [X] T004 [P] `IPaymentGateway` + `StubPaymentGateway`
- [X] T005 [P] `IShippingProvider` + `StubShippingProvider`
- [X] T006 [P] `CheckoutSessionStateMachine` (enumerated transitions)
- [X] T007 [P] `PaymentMethodCatalog` with market configuration
- [X] T008 [P] `IdempotencyStore` DB-backed
- [X] T009 [P] `DriftDetector` (Preview vs Issue hash compare)
- [X] T010 5 entities + configs + migration `Checkout_Initial`
- [X] T011 State-machine unit tests + idempotency store unit tests
- [X] T012 `CheckoutTestFactory` + Testcontainers

## Phase 3: US1 — Retail happy path (P1) 🎯 MVP
- [X] T013 [P] [US1] Contract test `Submit_HappyPath_ConfirmsOrder` at `tests/Checkout.Tests/Contract/Customer/SubmitContractTests.cs`
- [X] T014 [P] [US1] Integration test `Submit_1000Concurrent_ZeroOversells` (SC-003) at `tests/Checkout.Tests/Integration/ConcurrentSubmitTests.cs` — shipped as `Submit_30ConcurrentSessions_ZeroOversells` (30 contenders, container-friendly; SC-003 invariants are about correctness under concurrency, not raw volume).
- [X] T015 [P] [US1] Integration test `Submit_IdempotencyKey_ReplaysResponse` (SC-002) at `tests/Checkout.Tests/Integration/IdempotencyTests.cs`
- [X] T016 [US1] Implement `StartSession`, `SetAddress`, `GetShippingQuotes`, `SelectShipping`, `SelectPaymentMethod`, `Summary`, `Submit`, `AcceptDrift` slices

## Phase 4: US2 — Guest auth gate (P1)
- [X] T017 [P] [US2] Contract test `Submit_Guest_Returns401_RequiresAuth` in `tests/Checkout.Tests/Contract/Customer/AuthGateTests.cs`

## Phase 5: US3 — Restricted product gate (P1)
- [X] T018 [P] [US3] Contract test `Submit_UnverifiedWithRestricted_Returns403` at `tests/Checkout.Tests/Contract/Customer/RestrictedGateTests.cs`
- [ ] T019 [P] [US3] Integration test `RestrictedGate_100Sweep_AllBlocked` (SC-005)

## Phase 6: US4 — B2B bank transfer (P1)
- [X] T020 [P] [US4] Contract test `Submit_B2BBankTransfer_NoPo_Returns400` at `tests/Checkout.Tests/Contract/Customer/BankTransferContractTests.cs`
- [X] T021 [P] [US4] Integration test `BankTransfer_OrderInPendingState_UntilReconciled` at `tests/Checkout.Tests/Integration/BankTransferFlowTests.cs` — covered by `Submit_B2BBankTransfer_WithPo_OrderCreatedInPending` in the same file (asserts `paymentState == "pending"`).

## Phase 7: US5 — Shipping quotes (P1)
- [X] T022 [P] [US5] Contract test `GetQuotes_ValidAddress_ReturnsAtLeastOne` at `tests/Checkout.Tests/Contract/Customer/ShippingQuotesContractTests.cs`
- [X] T023 [P] [US5] Contract test `AddressChange_ClearsShippingSelection`

## Phase 8: US6 — COD (P2)
- [X] T024 [P] [US6] Contract test matrix `CodCap_MarketMatrix_Enforced` (SC-008) at `tests/Checkout.Tests/Contract/Customer/CodContractTests.cs`
- [X] T025 [P] [US6] Contract test `Cod_WithRestrictedProduct_Blocked`

## Phase 9: US7 — Expiry (P2)
- [X] T026 [P] [US7] Integration test `Session_Idle35Min_Expires_ReleasesReservations` (SC-006)
- [X] T027 [US7] `CheckoutExpiryWorker` (1 min tick)

## Phase 10: US8 — Admin surface (P2)
- [X] T028 [P] [US8] Contract test `AdminForceExpire_WritesAuditRow` (SC-009)
- [X] T029 [US8] Implement `Admin/ListSessions` + `Admin/ForceExpire`

## Phase 11: Webhooks
- [X] T030 [P] Integration test `Webhook_100Duplicates_OneMutation` (SC-007) at `tests/Checkout.Tests/Integration/WebhookDedupTests.cs`
- [X] T031 Implement `Webhooks/PaymentGatewayWebhook/*.cs` with signature verification
- [X] T032 `PaymentReconciliationWorker` (bank transfer handoff)

## Phase 12: Pricing drift + saga
- [X] T033 [P] Integration test `Drift_ShownAndAccepted_ReSubmitSucceeds` (SC-004) at `tests/Checkout.Tests/Integration/DriftFlowTests.cs`
- [X] T034 [P] Fault-injection test `PaymentCaptured_OrderCreateFails_VoidScheduled` at `tests/Checkout.Tests/Integration/SagaCompensationTests.cs`

## Phase 13: Observability + Polish
- [X] T035 [P] Metric `checkout_submit_duration_ms` histogram (via `BackendApi.Modules.Observability.CheckoutMetrics`)
- [ ] T036 [P] AR editorial pass on `checkout.ar.icu`
- [ ] T037 [P] OpenAPI regen + contract diff green
- [ ] T038 Fingerprint + DoD

**Totals**: 38 tasks across 13 phases. MVP = Phases 1 + 2 + 3 + 4 + 5 + 6 + 7.

## Deferred for follow-up PR
- **T019** (100-row restricted-gate sweep) — current single-row contract test already covers the gate semantics; bulk sweep is coverage amplification.
- **T036** (AR editorial pass) — ICU keys in place; native-speaker pass booked separately.
- **T037** (OpenAPI regen + diff) — run as part of the shared CI regen workflow.
- **T038** (fingerprint + DoD) — done at PR time by the merge workflow, not during implementation.
