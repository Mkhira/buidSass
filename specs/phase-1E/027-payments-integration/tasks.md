# Tasks: 027 — Payments Integration

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/payments-contract.md](./contracts/payments-contract.md)
**Phase**: 1E — Integrations · Milestone 8
**Created**: 2026-05-10

## Phase 0 — Setup

- [X] T001 [P] `Modules/Payments/PaymentsModule.cs` (DI, Hangfire queue config, EF context, `ManyServiceProvidersCreatedWarning` suppression).
- [X] T002 [P] CODEOWNERS additions: `Modules/Payments/**` under `@payments-team`; `Modules/Payments/PciScope/**` requires both `@payments-team` AND `@security-team`.
- [X] T003 [P] EF entity types under `Modules/Payments/Domain/` for all 12 tables.
- [X] T004 [P] EF configurations under `Modules/Payments/Persistence/Configurations/`.
- [X] T005 Initial migration `Persistence/Migrations/0001_create_payments_schema.cs` (12 tables + V-5 partial-refund-sum trigger + V-1 schema constraints).
- [X] T006 [P] Add CI guard `scripts/ci/check-pci-scope.sh` (greps for cardholder-shaped column names + payload field names; blocks PR on match).
- [X] T007 [P] Implement `Modules/Payments/PciScope/EgressPayloadFilter.cs` (allow-listed-fields validator; CODEOWNERS-protected per T002).

## Phase 1 — Foundations + state machines + PCI scope

Covers AC-1, AC-2, AC-3, AC-4.

- [X] T008 Apply migration to Staging via deploy workflow; verify 12 tables (AC-1, AC-2).
- [X] T009 Run PCI scope schema-scan against the live schema; assert zero matches (AC-4).
- [X] T010 [P] `Modules/Payments/Providers/IPaymentProvider.cs` per contract §6.
- [X] T011 [P] `Modules/Payments/Domain/StateMachines/{PaymentStateMachine,RefundStateMachine,ReconciliationExceptionStateMachine}.cs`.
- [X] T012 Wire E1 KV-slot population: `scripts/payments/populate-kv-slots.sh` populates 21 slots (7 providers × 3 keys); each emits `secret.placeholder_replaced` (AC-3).
- [X] T013 [P] `Modules/Payments/PciScope/PciScopeMonitor.cs` (nightly Hangfire job; emits `pci_scope.config_changed` audit on detected change; alerts on PCI-scope-affecting drift).

## Phase 2 — Card payments (KSA + EG)

Covers AC-5, AC-6, AC-7, AC-8, AC-9, AC-10, AC-11, AC-12.

- [X] T014 [P] `Providers/HyperPay/HyperPayProvider.cs` (Refit + HMAC + Apple Pay + Mada + STC Pay + cards rails).
- [X] T015 [P] `Providers/Tap/TapProvider.cs` (KSA cards backup).
- [X] T016 [P] `Providers/Paymob/PaymobProvider.cs` (EG cards + Apple Pay + Meeza).
- [X] T017 [P] `Providers/Kashier/KashierProvider.cs` (EG cards backup; ledger via SFTP-CSV per research §4).
- [X] T018 [P] `Features/CreatePayment/{CreatePaymentCommand,Handler,Validator}` enforcing BR-3 idempotency via `idempotency_keys` table.
- [X] T019 [P] `Features/GetPaymentMethods/` with V-7 market eligibility filter.
- [X] T020 `Workers/PaymentDispatchWorker.cs` (5xx retry policy per BR-5; 4xx no retry).
- [X] T021 Verify AC-5 (Mada via HyperPay → captured within 10s).
- [X] T022 Verify AC-6 (Visa via Paymob → captured).
- [X] T023 Verify AC-7 (5xx retry sequence + dead-letter).
- [X] T024 Verify AC-8 (4xx never retries).
- [X] T025 Verify AC-9 (idempotent create returns original Payment).
- [X] T026 Verify AC-10 (Apple Pay via HyperPay).
- [X] T027 Verify AC-11 (STC Pay TTL behavior).
- [X] T028 Verify AC-12 (Meeza via Paymob).

## Phase 3 — BNPL

Covers AC-13, AC-14, AC-15, AC-16.

- [X] T029 [P] `Providers/Tabby/TabbyProvider.cs`.
- [X] T030 [P] `Providers/Tamara/TamaraProvider.cs`.
- [X] T031 [P] `Providers/Valu/ValuProvider.cs`.
- [X] T032 BNPL `pending_external_redirect` flow handling in `CreatePaymentCommand` + per-provider redirect URL composition.
- [X] T033 `Workers/PendingExpirationReconciler.cs` ages out `pending_external_redirect` past TTL.
- [X] T034 Verify AC-13, AC-14, AC-15, AC-16.

## Phase 4 — COD + bank transfer

Covers AC-17, AC-18, AC-19, AC-20.

- [X] T035 [P] `Features/CodMarkCaptured/` and `CodMarkFailed/` (warehouse-operator-only endpoints).
- [X] T036 [P] `Features/BankTransferMarkCaptured/` (reference-text-search-against-statement workflow).
- [X] T037 [P] `Workers/BankTransferTimeoutReconciler.cs` (72h auto-expire; emits `payment.expired` and triggers spec 011 cancellation).
- [X] T038 Verify AC-17 (COD eligibility), AC-18 (operator-mark flow), AC-19 (bank transfer reference + match), AC-20 (72h expiry).

## Phase 5 — Retry + refund

Covers AC-21, AC-22, AC-23.

- [X] T039 [P] `Features/RetryPayment/` (creates NEW Payment row per BR-6).
- [X] T040 [P] `Features/Refund/` enforcing V-5 (sum ≤ captured) via DB trigger + app-level pre-check.
- [X] T041 [P] `Subscribers/OrderCancelledSubscriber.cs` cascading refund when Payment is captured.
- [X] T042 Verify AC-21 (retry creates new row; latest reflects in order's payment_status).
- [X] T043 Verify AC-22 (full + partial refund flows).
- [X] T044 Verify AC-23 (over-refund 422 + audit).

## Phase 6 — Webhooks + replay

Covers AC-24, AC-25, AC-26, AC-27.

- [X] T045 [P] `Webhooks/HyperPayWebhookEndpoint.cs` and the other six provider endpoints; HMAC validation; idempotency PK.
- [X] T046 [P] `Features/WebhookReplay/{ReplayCommand,Handler}` per-provider capability check.
- [X] T047 [P] `Workers/WebhookReplayWorker.cs` (Hangfire-driven replay execution).
- [X] T048 Verify AC-24 (signature fail-closed 401).
- [X] T049 Verify AC-25 (idempotent re-delivery).
- [X] T050 Verify AC-26 (replay tool processes missed events with idempotency).
- [X] T051 Verify AC-27 (non-replay-supporting provider returns "not supported").

## Phase 7 — Reconciliation

Covers AC-28, AC-29, AC-30, AC-31, AC-32.

- [X] T052 [P] `Workers/DailyReconciliationJob.cs` (scheduled cron `0 0 * * *` UTC ≈ 03:00 KSA).
- [X] T053 [P] Per-provider settlement-ledger fetcher: API path for HyperPay/Tap/Paymob/Tabby/Tamara, SFTP-CSV path for Kashier/Valu (research §4).
- [X] T054 `ReconciliationMatcher` engine: matches internal payments against ledger rows by `(provider_id, provider_message_id)`; categorizes mismatches.
- [X] T055 [P] `Features/Reconciliation/{ListRuns,ListExceptions,ResolveException}/`.
- [X] T056 Admin UI surfaces: `apps/admin_web/app/payments/reconciliation/` and `exceptions/`.
- [X] T057 Verify AC-28 (daily run completes within 30 min).
- [X] T058 Verify AC-29, AC-30, AC-31 (each exception kind generated by synthetic injection).
- [X] T059 Verify AC-32 (operator resolves exception with documented action; audit captures resolution).

## Phase 8 — Failover + chargebacks + audit + compliance + ADR-007

Covers AC-33, AC-34, AC-35, AC-36, AC-37, AC-38, AC-39, AC-40.

- [X] T060 [P] `Features/ProviderRouting/{Get,Set,Failover}/` and `Workers/ProviderHealthMonitor.cs`.
- [X] T061 [P] `Features/Chargebacks/{List,Update}/`.
- [X] T062 [P] Audit-event emitters at every state transition + admin action + reconciliation event + PCI scope event (per data-model.md audit table).
- [X] T063 [P] PII egress sweep test in `Tests/Integration/EgressPayloadFilterTests.cs`; assert every provider impl satisfies the BR-14 allow-list.
- [X] T064 Extend secret-pattern guard (spec 003 + 025/026 extensions) for HyperPay + Tap + Paymob + Kashier + Tabby + Tamara + Valu credentials (AC-36).
- [X] T065 Verify AC-33 (manual failover).
- [X] T066 Verify AC-34 (auto-failover defaults to false in seed).
- [X] T067 Verify AC-35 (PII egress sweep returns zero violations).
- [X] T068 Verify AC-36 (KV-only secrets; CI guard rejects appsettings).
- [X] T069 Verify AC-37 (365-day audit query returns every state transition + admin action).
- [X] T070 Update ADR-007 in `CLAUDE.md` from `Proposed` to `Accepted` with the v1 stack documented (HyperPay+Tap KSA cards, Paymob+Kashier EG cards, Tabby+Tamara KSA BNPL, Valu EG BNPL, COD + bank transfer native, PCI scope SAQ-A). Bump fingerprint (AC-38).
- [X] T071 Verify AC-39 (025 receives `payment.captured`/`failed`/`refunded` and dispatches localized notifications).
- [X] T072 Verify AC-40 (011 + 012 consume `payment.captured`).

## Phase 9 — Polish

- [X] T073 [P] `Modules/Payments/Seeding/PaymentsV1Seeder.cs` (sample payments across `pending_authorization → captured`, `failed → retry → captured`, `pending_external_redirect → captured`, `pending_collection_on_delivery → captured`, reconciliation-exception scenarios).
- [X] T074 [P] OpenAPI tests for all customer + admin + webhook endpoints in `Tests/Contract/`.
- [X] T075 Hand off to spec 029 for k6 load test (5× RPS) + chaos drills + PCI scope review.
- [X] T076 Final spec-compliance check: re-read AC-1..AC-40; file gaps as P1 issues.

---

## AC → Task traceability

| AC | Tasks |
|---|---|
| AC-1 | T005, T008 |
| AC-2 | T005, T008 |
| AC-3 | T012 |
| AC-4 | T006, T009, T013 |
| AC-5 | T014, T020, T021 |
| AC-6 | T016, T020, T022 |
| AC-7 | T020, T023 |
| AC-8 | T020, T024 |
| AC-9 | T018, T025 |
| AC-10 | T014, T026 |
| AC-11 | T014, T027 |
| AC-12 | T016, T028 |
| AC-13 | T029, T032, T034 |
| AC-14 | T030, T032, T034 |
| AC-15 | T031, T032, T034 |
| AC-16 | T032, T034 |
| AC-17 | T019, T038 |
| AC-18 | T035, T038 |
| AC-19 | T036, T038 |
| AC-20 | T037, T038 |
| AC-21 | T039, T042 |
| AC-22 | T040, T043 |
| AC-23 | T040, T044 |
| AC-24 | T045, T048 |
| AC-25 | T045, T049 |
| AC-26 | T046, T047, T050 |
| AC-27 | T046, T051 |
| AC-28 | T052, T057 |
| AC-29 | T054, T058 |
| AC-30 | T054, T058 |
| AC-31 | T054, T058 |
| AC-32 | T055, T059 |
| AC-33 | T060, T065 |
| AC-34 | T060, T066 |
| AC-35 | T007, T063, T067 |
| AC-36 | T064, T068 |
| AC-37 | T062, T069 |
| AC-38 | T070 |
| AC-39 | T071 |
| AC-40 | T072 |

Every AC mapped. 76 tasks; 35 marked `[P]`.
