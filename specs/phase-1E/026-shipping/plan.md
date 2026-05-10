# Implementation Plan: 026 — Shipping

**Branch**: `phase-1E` | **Date**: 2026-05-10 | **Spec**: [spec.md](./spec.md)

## Summary

Vertical slice under `services/backend_api/Modules/Shipping/` implementing a generic provider-abstracted shipping module for KSA + EG. ADR-008 flipped to Accepted with SMSA + Aramex (KSA) and Bosta + Aramex (EG). 11 new tables under the `shipping` schema. Three state-change surfaces: Method lifecycle, Shipment state machine, ProviderRouting failover. Webhook receivers idempotent on `(provider_id, provider_tracking_id, event_kind, occurred_at)`. Provider credentials sourced from E1 Key Vault slots `shipping/<market>/<provider>/api-key`. Emits `shipping.status_changed` consumed by 025.

## Technical Context

**Language/Version**: C# 12 / .NET 9; Postgres 16; EF Core 9.
**Primary Dependencies**: Refit (Bosta, Aramex, SMSA HTTP clients), Polly (retry), Hangfire (workers), Azure.Storage.Blobs (label PDF storage).
**Storage**: 11 new tables under `shipping`. Label PDFs in Azure Blob Storage container `shipping-labels-<env>` with 90-day lifecycle policy (BR-13).
**Testing**: xUnit + WebApplicationFactory + Testcontainers Postgres + provider stubs + record-replay webhook fixtures.
**Target Platform**: Hosted in `ca-backend-api-*` ACA from E1.
**Project Type**: Vertical slice + provider folders.
**Performance Goals**: SC-1 (99% label_purchased within 60s), SC-2 (webhook→status latency < 60s p95).
**Constraints**: BR-3 fee snapshot at cart, BR-4 single-package, BR-7 cross-market block, BR-12 webhook precedence, BR-15 PII minimization at provider egress.
**Scale/Scope**: Single-package per order; ~30K shipments/month at launch (estimated); 4 providers (2 markets × 2 each).

## Constitution Check

| Principle | Posture | Status |
|---|---|---|
| 4 | AR+EN editorial gate on method publish (V-1). | PASS |
| 5 | Per-market provider routing + zones. | PASS |
| 11 | Inventory readiness consumed (read-only at v1). | PASS |
| 14 | `IShippingProvider` abstraction; provider swap = config change. | PASS |
| 17 | Shipping state is the fulfillment field of order; orthogonal from order/payment/refund states. | PASS |
| 24 | Explicit Shipment state machine. | PASS |
| 25 | Audit on every transition + admin action. | PASS |
| ADR-008 | Flipped to Accepted with v1 stack. | PASS |
| ADR-010 | KSA Central Postgres + PII-minimized provider egress. | PASS |
| Guardrails 1–4 | All satisfied (lint/format, OpenAPI contract diff, fingerprint, CODEOWNERS). | PASS |

No violations.

## Project Structure

```
services/backend_api/Modules/Shipping/
├── ShippingModule.cs                   # DI, Hangfire queue config, EF context, ManyServiceProvidersCreatedWarning suppression
├── Domain/
│   ├── Shipment.cs (aggregate)
│   ├── ShippingMethod.cs + ShippingMethodVersion.cs
│   ├── ShippingZone.cs
│   ├── FeeTable.cs
│   ├── ShipmentEvent.cs
│   ├── ShipmentDispute.cs
│   ├── ProviderRouting.cs
│   ├── DeadLetterLabel.cs
│   ├── MarketSchema.cs
│   ├── StateMachines/{ShipmentStateMachine,MethodVersionStateMachine}.cs
│   └── Events/                         # ShipmentLabelPurchased, ShipmentStatusChanged, etc.
├── Persistence/
│   ├── ShippingDbContext.cs
│   ├── Configurations/                 # EF configs (one per entity)
│   └── Migrations/0001_create_shipping_schema.cs
├── Subscribers/
│   ├── OrderConfirmedSubscriber.cs
│   ├── OrderCancelledSubscriber.cs
│   └── RefundInitiatedSubscriber.cs
├── Workers/
│   ├── LabelDispatchWorker.cs          # default queue
│   ├── ReattemptQueuedLabelsWorker.cs
│   ├── DeadLetterArchiver.cs
│   ├── SlaBreachMonitor.cs             # detects shipments stale > SLA × 2
│   └── ProviderHealthMonitor.cs        # 5-min sliding window
├── Providers/
│   ├── IShippingProvider.cs
│   ├── Smsa/SmsaProvider.cs
│   ├── Aramex/AramexKsaProvider.cs + AramexEgProvider.cs
│   └── Bosta/BostaProvider.cs
├── Webhooks/{Smsa,AramexKsa,AramexEg,Bosta}WebhookEndpoint.cs
├── Quote/                              # Vertical slice
│   ├── QuoteHandler.cs
│   └── ZoneResolver.cs                 # address → zone resolver
├── Features/                           # Admin endpoints
│   ├── Methods/{CreateDraft,SubmitForReview,Approve,Reject,Archive,UpdateFeeTable}/
│   ├── Zones/{Create,Update,List}/
│   ├── Shipments/{Get,List,MarkHandedOver,Dispute,CreateReDelivery,VoidLabel}/
│   ├── ProviderRouting/{Get,Set,Failover}/
│   ├── DeadLetterLabels/{List,Retry,Discard}/
│   └── Tracking/{GetByNumber}/
├── Seeding/ShippingV1Seeder.cs         # Sample methods, zones, fee tables AR+EN, sample shipments
└── Tests/{Unit,Integration,Contract}/

apps/admin_web/app/shipping/            # Lane B
├── methods/
├── zones/
├── shipments/
├── provider-routing/
├── exception-queue/

CODEOWNERS:
  /services/backend_api/Modules/Shipping/  @shipping-team
  /apps/admin_web/app/shipping/             @shipping-team
```

**Structure decision**: vertical slice + provider folders + dedicated Quote vertical (high read traffic; isolated from write paths). Hangfire shared with 025; default queue here, no priority isolation needed (no OTP-equivalent latency budget on shipping).

## Phase 0 — Research (research.md)
Topics: zone-resolution strategy (postal code regex vs city-list vs polygon), label-PDF storage pattern (blob + signed URLs), webhook signature handling per provider, fee-table tier-overlap detection, address validation provider integration deferral, multi-package deferral.

## Phase 1 — Design (data-model.md, contracts/, quickstart.md)
11 tables. `IShippingProvider` interface mirrors 025. OpenAPI for `/shipping/quote`, `/shipping/track/{n}`, `/admin/shipping/*`, `/shipping/webhooks/{provider}`.

## Four Guardrails — coverage statement
1. `dotnet format` + admin-web eslint/prettier.
2. OpenAPI artifact updated; provider-webhook signature validation contract-tested.
3. Standard fingerprint.
4. CODEOWNERS as listed.

## Cross-spec dependencies
- Hard upstream: 010, 011, E1. Soft: 008.
- Downstream: 025, 027, 029.

## Risks and mitigations
| Risk | Mitigation |
|---|---|
| Provider API drift mid-launch | Refit interface + record-replay test fixtures + manual provider-status monitoring runbook |
| Address coverage gaps in zones | Daily "uncovered-address" alert; admin can extend zone postal codes without engineering |
| Label PDF storage cost | 90-day lifecycle policy on blob container; archive tier post-90d for accounting reference |
| Webhook out-of-order causing state regression | BR-12 precedence rule + integration tests for the precedence matrix |

## Phase 2 readiness
Plan is /speckit-tasks-ready. Six phase groups: Foundations, Quote+Zones, Order→Shipment+Providers+Webhooks, Admin Methods/Routing, Disputes/Re-delivery/Failover, Audit+Compliance+Load.
