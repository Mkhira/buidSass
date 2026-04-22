---
description: "Dependency-ordered tasks for spec 010 — checkout"
---

# Tasks: Checkout v1

## Phase 1: Setup
- [ ] T001 Module tree `services/backend_api/Modules/Checkout/{Primitives/{Payment,Shipping},Customer/{StartSession,SetAddress,GetShippingQuotes,SelectShipping,SelectPaymentMethod,Summary,Submit,AcceptDrift},Admin/{ListSessions,ForceExpire},Webhooks/PaymentGatewayWebhook,Workers,Entities,Persistence/{Configurations,Migrations},Messages}` + tests
- [ ] T002 Register `AddCheckoutModule`, wire `Program.cs`
- [ ] T003 [P] Add Polly + shared FluentValidation deps

## Phase 2: Foundational
- [ ] T004 [P] `IPaymentGateway` + `StubPaymentGateway`
- [ ] T005 [P] `IShippingProvider` + `StubShippingProvider`
- [ ] T006 [P] `CheckoutSessionStateMachine` (enumerated transitions)
- [ ] T007 [P] `PaymentMethodCatalog` with market configuration
- [ ] T008 [P] `IdempotencyStore` DB-backed
- [ ] T009 [P] `DriftDetector` (Preview vs Issue hash compare)
- [ ] T010 5 entities + configs + migration `Checkout_Initial`
- [ ] T011 State-machine unit tests + idempotency store unit tests
- [ ] T012 `CheckoutTestFactory` + Testcontainers

## Phase 3: US1 — Retail happy path (P1) 🎯 MVP
- [ ] T013 [P] [US1] Contract test `Submit_HappyPath_ConfirmsOrder` at `tests/Checkout.Tests/Contract/Customer/SubmitContractTests.cs`
- [ ] T014 [P] [US1] Integration test `Submit_1000Concurrent_ZeroOversells` (SC-003) at `tests/Checkout.Tests/Integration/ConcurrentSubmitTests.cs`
- [ ] T015 [P] [US1] Integration test `Submit_IdempotencyKey_ReplaysResponse` (SC-002) at `tests/Checkout.Tests/Integration/IdempotencyTests.cs`
- [ ] T016 [US1] Implement `StartSession`, `SetAddress`, `GetShippingQuotes`, `SelectShipping`, `SelectPaymentMethod`, `Summary`, `Submit`, `AcceptDrift` slices

## Phase 4: US2 — Guest auth gate (P1)
- [ ] T017 [P] [US2] Contract test `Submit_Guest_Returns401_RequiresAuth` in `tests/Checkout.Tests/Contract/Customer/AuthGateTests.cs`

## Phase 5: US3 — Restricted product gate (P1)
- [ ] T018 [P] [US3] Contract test `Submit_UnverifiedWithRestricted_Returns403` at `tests/Checkout.Tests/Contract/Customer/RestrictedGateTests.cs`
- [ ] T019 [P] [US3] Integration test `RestrictedGate_100Sweep_AllBlocked` (SC-005)

## Phase 6: US4 — B2B bank transfer (P1)
- [ ] T020 [P] [US4] Contract test `Submit_B2BBankTransfer_NoPo_Returns400` at `tests/Checkout.Tests/Contract/Customer/BankTransferContractTests.cs`
- [ ] T021 [P] [US4] Integration test `BankTransfer_OrderInPendingState_UntilReconciled` at `tests/Checkout.Tests/Integration/BankTransferFlowTests.cs`

## Phase 7: US5 — Shipping quotes (P1)
- [ ] T022 [P] [US5] Contract test `GetQuotes_ValidAddress_ReturnsAtLeastOne` at `tests/Checkout.Tests/Contract/Customer/ShippingQuotesContractTests.cs`
- [ ] T023 [P] [US5] Contract test `AddressChange_ClearsShippingSelection`

## Phase 8: US6 — COD (P2)
- [ ] T024 [P] [US6] Contract test matrix `CodCap_MarketMatrix_Enforced` (SC-008) at `tests/Checkout.Tests/Contract/Customer/CodContractTests.cs`
- [ ] T025 [P] [US6] Contract test `Cod_WithRestrictedProduct_Blocked`

## Phase 9: US7 — Expiry (P2)
- [ ] T026 [P] [US7] Integration test `Session_Idle35Min_Expires_ReleasesReservations` (SC-006)
- [ ] T027 [US7] `CheckoutExpiryWorker` (1 min tick)

## Phase 10: US8 — Admin surface (P2)
- [ ] T028 [P] [US8] Contract test `AdminForceExpire_WritesAuditRow` (SC-009)
- [ ] T029 [US8] Implement `Admin/ListSessions` + `Admin/ForceExpire`

## Phase 11: Webhooks
- [ ] T030 [P] Integration test `Webhook_100Duplicates_OneMutation` (SC-007) at `tests/Checkout.Tests/Integration/WebhookDedupTests.cs`
- [ ] T031 Implement `Webhooks/PaymentGatewayWebhook/*.cs` with signature verification
- [ ] T032 `PaymentReconciliationWorker` (bank transfer handoff)

## Phase 12: Pricing drift + saga
- [ ] T033 [P] Integration test `Drift_ShownAndAccepted_ReSubmitSucceeds` (SC-004)
- [ ] T034 [P] Fault-injection test `PaymentCaptured_OrderCreateFails_VoidScheduled` at `tests/Checkout.Tests/Integration/SagaCompensationTests.cs`

## Phase 13: Observability + Polish
- [ ] T035 [P] Metric `checkout_submit_duration_ms` histogram
- [ ] T036 [P] AR editorial pass on `checkout.ar.icu`
- [ ] T037 [P] OpenAPI regen + contract diff green
- [ ] T038 Fingerprint + DoD

**Totals**: 38 tasks across 13 phases. MVP = Phases 1 + 2 + 3 + 4 + 5 + 6 + 7.
