# Implementation Plan: 027 — Payments Integration

**Branch**: `phase-1E` | **Date**: 2026-05-10 | **Spec**: [spec.md](./spec.md)

## Summary

Vertical slice under `services/backend_api/Modules/Payments/` implementing a generic provider-abstracted payments module for KSA + EG. ADR-007 flipped to Accepted with the v1 stack: HyperPay + Tap (KSA cards), Paymob + Kashier (EG cards), Tabby + Tamara (KSA BNPL), Valu (EG BNPL), native COD + bank transfer. PCI scope SAQ-A. 12 new tables under the `payments` schema. Explicit Payment + Refund + Reconciliation state machines (Principle 24). Webhook receivers idempotent on `(provider_id, provider_message_id, event_kind)` PK; replay tool for incident response (BR-16). Daily reconciliation job at 03:00 KSA matches provider ledgers against internal payments and surfaces exceptions to an operator queue. Provider credentials sourced from E1 KV slots `payments/<market>/<provider>/{api-key,api-secret,webhook-signing-key}`.

## Technical Context

**Language/Version**: C# 12 / .NET 9; Postgres 16; EF Core 9.
**Primary Dependencies**: Refit (HyperPay, Paymob, Tap, Kashier, Tabby, Tamara, Valu HTTP clients), Polly (retry), Hangfire (workers + scheduler), CsvHelper (provider settlement-ledger CSV parsing where applicable).
**Storage**: 12 new tables under `payments`. No cardholder data ever stored (BR-1 / SAQ-A).
**Testing**: xUnit + WebApplicationFactory + Testcontainers Postgres + provider stubs (in-memory) + record-replay webhook fixtures from real provider sandboxes.
**Target Platform**: ACA backend container.
**Project Type**: Vertical slice + provider folders.
**Performance Goals**: SC-1 (99% authorize+capture < 10s p95), SC-2 (recon job < 30 min), SC-7 (webhook replay < 5 min for 1-hour window).
**Constraints**: PCI SAQ-A boundary at all times, BR-3 idempotency, BR-4 webhook idempotency, BR-5 retry-only-on-5xx, BR-6 retry-creates-new-row, BR-14 PII minimization at egress.
**Scale/Scope**: Estimated launch volume: 1k–5k payment attempts/day; daily reconciliation handles ~5k rows; 7 providers active.

## Constitution Check

| Principle | Posture | Status |
|---|---|---|
| 5 | Per-market routing + per-market method enabling. | PASS |
| 13 | Full method matrix supported (cards + Apple Pay + Mada + STC Pay + Meeza + BNPL × 3 + COD + bank transfer); generic abstraction layer; idempotency + retries + reconciliation + replay. | PASS |
| 17 | Payment state is order's payment-status field; orthogonal from order/fulfillment/refund states. | PASS |
| 24 | Explicit Payment + Refund + Reconciliation state machines. | PASS |
| 25 | Audit on every transition + admin action + reconciliation exception. | PASS |
| 28 | Implementation-ready spec. | PASS |
| ADR-007 | Flipped to Accepted with v1 stack. | PASS |
| ADR-010 | Metadata in KSA Central Postgres; provider egress documented; cardholder data NEVER stored. | PASS |
| Guardrail #1 | dotnet format + admin web lint. | PASS |
| Guardrail #2 | OpenAPI artifact updated; webhook signature contract-tested. | PASS |
| Guardrail #3 | Standard fingerprint. | PASS |
| Guardrail #4 | CODEOWNERS additions. | PASS |

No violations.

## Project Structure

```
services/backend_api/Modules/Payments/
├── PaymentsModule.cs                   # DI, Hangfire queue, EF context, ManyServiceProvidersCreatedWarning suppression
├── Domain/
│   ├── Payment.cs (aggregate)
│   ├── Refund.cs
│   ├── Chargeback.cs
│   ├── ProviderRouting.cs
│   ├── PaymentMethodMarketConfig.cs
│   ├── ReconciliationRun.cs + ReconciliationException.cs
│   ├── BankTransferReference.cs
│   ├── CodCollectionLog.cs
│   ├── IdempotencyKey.cs
│   ├── PciScopeEvent.cs
│   ├── StateMachines/{PaymentStateMachine,RefundStateMachine,ReconciliationExceptionStateMachine}.cs
│   └── Events/                         # PaymentCreated, PaymentCaptured, PaymentFailed, PaymentRefunded, ReconciliationExceptionOpened, etc.
├── Persistence/
│   ├── PaymentsDbContext.cs
│   ├── Configurations/
│   └── Migrations/0001_create_payments_schema.cs
├── Subscribers/
│   ├── OrderConfirmedSubscriber.cs     # defensive — Payment usually created at checkout
│   ├── OrderCancelledSubscriber.cs     # cascade refund if captured
│   └── RefundInitiatedSubscriber.cs    # consumes order.refund_initiated
├── Workers/
│   ├── PaymentDispatchWorker.cs        # provider-call worker
│   ├── DailyReconciliationJob.cs       # scheduled 03:00 KSA
│   ├── PendingExpirationReconciler.cs  # ages out pending_* states
│   ├── BankTransferTimeoutReconciler.cs
│   ├── ProviderHealthMonitor.cs
│   └── WebhookReplayWorker.cs
├── Providers/
│   ├── IPaymentProvider.cs             # CreatePayment, Refund, GetStatus, ValidateWebhookSignature, ParseWebhookEvent, FetchSettlementLedger, ReplayWebhooks
│   ├── HyperPay/HyperPayProvider.cs    # KSA cards + Apple Pay + Mada + STC Pay
│   ├── Tap/TapProvider.cs              # KSA cards backup
│   ├── Paymob/PaymobProvider.cs        # EG cards + Apple Pay + Meeza
│   ├── Kashier/KashierProvider.cs      # EG cards backup
│   ├── Tabby/TabbyProvider.cs          # KSA BNPL
│   ├── Tamara/TamaraProvider.cs        # KSA BNPL
│   └── Valu/ValuProvider.cs            # EG BNPL
├── Webhooks/
│   ├── HyperPayWebhookEndpoint.cs
│   ├── TapWebhookEndpoint.cs
│   ├── PaymobWebhookEndpoint.cs
│   ├── KashierWebhookEndpoint.cs
│   ├── TabbyWebhookEndpoint.cs
│   ├── TamaraWebhookEndpoint.cs
│   └── ValuWebhookEndpoint.cs
├── Features/                           # Vertical slice handlers
│   ├── CreatePayment/
│   ├── RetryPayment/
│   ├── GetPaymentMethods/
│   ├── Refund/
│   ├── CodMarkCaptured/
│   ├── CodMarkFailed/
│   ├── BankTransferMarkCaptured/
│   ├── ProviderRouting/{Get,Set,Failover}/
│   ├── Reconciliation/{ListRuns,ListExceptions,ResolveException}/
│   ├── WebhookReplay/
│   └── Chargebacks/
├── PciScope/                           # SAQ-A guardrails
│   ├── PciScopeMonitor.cs              # background scanner that flags PCI-scope-affecting config changes
│   └── EgressPayloadFilter.cs          # enforces BR-14 minimum-fields contract
├── Seeding/PaymentsV1Seeder.cs         # sample payments across success/failure/retry/recon-exception
└── Tests/{Unit,Integration,Contract}/

apps/admin_web/app/payments/
├── routing/
├── methods/
├── reconciliation/
├── exceptions/
├── webhook-replay/
├── refunds/
└── chargebacks/

CODEOWNERS:
  /services/backend_api/Modules/Payments/  @payments-team
  /apps/admin_web/app/payments/             @payments-team
```

**Structure decision**: vertical slice with one provider folder per provider impl + dedicated `PciScope/` namespace housing guardrail code. The `PciScope/` separation makes PCI-scope-affecting code reviewable as one set in code review (an explicit CODEOWNERS sub-rule could require security-team approval for any change in there if desired).

## Phase 0 — Research (research.md)

Topics: hosted-fields integration patterns per provider, BNPL redirect flow standardization, idempotency-key derivation strategy, reconciliation-ledger fetch patterns (CSV vs API per provider), PCI SAQ-A boundary verification approach, retry-creates-new-row pattern (rationale vs in-place), bank-transfer reference scheme, webhook-replay capability matrix.

## Phase 1 — Design (data-model.md, contracts/, quickstart.md)

12 tables. Three state machines. `IPaymentProvider` interface mirrors 025/026's provider abstraction. OpenAPI for `/payments/*`, `/admin/payments/*`, `/payments/webhooks/*`.

## Four Guardrails — coverage statement

1. dotnet format + admin web lint.
2. OpenAPI updated; webhook signatures contract-tested via record-replay; PCI scope CI sweep extends spec 003 secret-pattern guard with cardholder-shape patterns (PAN regex, CVV regex, track-data markers) — `scripts/ci/check-pci-scope.sh` rejects PRs adding cardholder-shaped columns or payload fields.
3. Standard fingerprint.
4. CODEOWNERS as listed; suggest adding `@security-team` as a co-CODEOWNER of `Modules/Payments/PciScope/**` for break-glass review.

## Cross-spec dependencies

- Hard upstream: 010, 012, E1.
- Soft upstream: 011 (bidirectional — order events drive payments; payments events feed order status).
- Downstream: 025 (notifications), 029 (load + PCI scope review + chaos drills).

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| PCI scope drift (a future PR adds cardholder-shaped column) | CI guard `check-pci-scope.sh` blocks; `PciScopeMonitor` runs nightly on the live schema and alerts |
| Provider API drift mid-launch | Refit interface + record-replay test fixtures + runbook for swapping in Tap/Kashier as backup |
| Reconciliation exception backlog | 90-day operator window + alert if backlog > 20 open exceptions for > 24h |
| BNPL credit-decision leakage to customer | Decision details NEVER returned to client — only "rejected" surface |
| Webhook handler downtime causing missed events | Replay tool (BR-16) + nightly reconciliation catches anything replay missed |
| Refund > captured race | DB constraint sums refunds and rejects over-amount; V-5 tested in unit + integration |
| Cardholder data accidentally logged | Logging middleware redacts based on field-name heuristics + the egress-payload filter prevents log entry creation in the first place |

## Phase 2 readiness

Plan is /speckit-tasks-ready. Eight phase groups: Foundations + State machines + PCI scope, Card payments, BNPL, COD + bank transfer, Retry + refund, Webhooks + replay, Reconciliation, Audit + PCI sweep + ADR-007 + load.
