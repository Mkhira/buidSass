# Implementation Plan: 025 — Notifications

**Branch**: `phase-1E` | **Date**: 2026-05-10 | **Spec**: [spec.md](./spec.md)

## Summary

Build a centralized, multi-channel (SMS / email / push), multi-market (KSA + EG), bilingual (AR + EN editorial-grade) notification module under `services/backend_api/Modules/Notifications/`. Vertical-slice architecture per ADR-003 (one MediatR handler per feature). Twelve new tables under the `notifications` schema. Three explicit state machines (Notification, Template lifecycle, Campaign). Provider abstraction via `INotificationProvider` with concrete impls for SES (email), Unifonic (KSA SMS), Vodafone Egypt (EG SMS), and FCM (push). Backup providers (SendGrid + Infobip) implement the same interface and are toggled via `provider_routing` configuration. Webhook receivers are idempotent via `webhooks_received` PK. Provider credentials sourced via `AddLayeredConfiguration()` from E1 Key Vault slots. Audit events emitted at every state-changing transition. ADR-009 flipped to Accepted.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (matches existing modules). Postgres 16. EF Core 9 with migrations.
**Primary Dependencies**: MediatR (handlers + domain events), AWS SDK for SES, FirebaseAdmin SDK, Refit for Unifonic + Vodafone Egypt + Infobip + SendGrid HTTP clients (lighter than building bespoke clients), Polly for retry policies, Hangfire OR a lightweight Postgres-backed worker (decision: Hangfire — already present in the repo per spec 003 conventions).
**Storage**: 12 new tables under `notifications` schema. No external queue at v1 — Postgres-backed work queue (`notifications.notifications` itself with a `state IN ('pending','queued','retrying')` index serving as the work queue, polled by Hangfire workers). One reconciliation job, two scheduled jobs (campaign scheduler + sending-stuck reconciler).
**Testing**: xUnit + WebApplicationFactory for HTTP contract tests. Testcontainers-based Postgres for integration tests. Provider stubs (in-memory) for unit tests; record-replay test fixtures from real provider responses for integration.
**Target Platform**: Hosted in `ca-backend-api-*` ACA container app from E1.
**Project Type**: Vertical slice under `Modules/Notifications/` per ADR-003. Plus admin-web Next.js routes under `apps/admin_web/app/notifications/*` for template editor + campaign manager + dead-letter queue + provider routing.
**Performance Goals**: SC-2 (OTP p95 < 30s), SC-1 (99% transactional within 5 min), AC-28 (5× RPS k6 in spec 029).
**Constraints**: BR-15 (OTP isolation — dedicated worker pool), BR-9 (idempotency), BR-8 (template snapshot), BR-11 (KV-only secrets), Principle 4 (editorial AR — manual reviewer sign-off).
**Scale/Scope**: ≤ 30 distinct event templates at launch. Estimated peak: 50–200 transactional notifications/min steady state, 1000–5000/min during campaign bursts. Push fan-out can spike higher but FCM throttles per-app.

## Constitution Check

| Principle | Posture | Status |
|---|---|---|
| 4 — Bilingual + RTL editorial | V-1 publish gate enforces AR + EN; AR editorial reviewer sign-off mandatory; AC-21 verifies. | PASS |
| 5 — Markets EG + KSA | Per-market provider routing; per-market send-windows; per-market unsubscribe language. | PASS |
| 19 — Notifications | All required channels + events + audit + admin campaigns + preferences. | PASS |
| 22 — Locked tech | .NET 9, Postgres, EF Core, no substitutions. | PASS |
| 23 — Modular monolith | One slice under Modules/Notifications/. | PASS |
| 24 — State machines | Three explicit machines, transition tables in spec. | PASS |
| 25 — Audit | Every state transition + admin action audit-logged. | PASS |
| 28 — AI-build | Implementation-ready slice + handler list + provider list. | PASS |
| ADR-009 | Flipped to Accepted in this spec. | PASS |
| ADR-010 | KSA-Central Postgres for metadata; provider egress documented; PII redaction in payload column. | PASS |
| Guardrail #1 (lint/format) | `dotnet format` + existing CI. | PASS |
| Guardrail #2 (contract diff) | OpenAPI artifact updated; admin endpoints under `/admin/notifications/*` + customer + webhook surfaces. | PASS |
| Guardrail #3 (fingerprint) | Standard fingerprint check. | PASS |
| Guardrail #4 (CODEOWNERS) | `Modules/Notifications/**` and `apps/admin_web/app/notifications/**` listed under @notifications-team. | PASS |

No violations.

## Project Structure

```
services/backend_api/Modules/Notifications/
├── NotificationsModule.cs              # DI registration, Hangfire queue setup, EF context
├── Domain/
│   ├── Notification.cs                 # Aggregate root (state machine)
│   ├── Template.cs + TemplateVersion.cs
│   ├── Campaign.cs + CampaignRecipient.cs
│   ├── Preference.cs
│   ├── UnsubscribeToken.cs
│   ├── ProviderRouting.cs
│   ├── DeadLetterEntry.cs
│   ├── MarketSchema.cs
│   └── Events/                         # Domain events: NotificationDelivered, TemplatePublished, CampaignSent, etc.
├── Persistence/
│   ├── NotificationsDbContext.cs
│   ├── Configurations/                 # EF configurations (one per entity)
│   └── Migrations/
├── Subscribers/                        # MediatR INotificationHandler<T> consumers of upstream events
│   ├── OtpRequestedSubscriber.cs       # spec 004 event
│   ├── OrderEventSubscriber.cs         # spec 011 events
│   ├── RefundEventSubscriber.cs
│   ├── VerificationResultSubscriber.cs
│   ├── PriceDropSubscriber.cs
│   ├── RestockSubscriber.cs
│   ├── AbandonedCartSubscriber.cs
│   └── ShippingStatusSubscriber.cs     # subscribes; source goes live with spec 026
├── Workers/                            # Hangfire jobs
│   ├── DispatchWorker.cs               # main work-queue consumer
│   ├── OtpDispatchWorker.cs            # dedicated high-priority pool (BR-15)
│   ├── CampaignScheduler.cs            # picks scheduled campaigns, materializes recipients
│   ├── SendingStuckReconciler.cs       # 30-min job for AC-28-adjacent reconciliation
│   └── DeadLetterArchiver.cs           # 30-day archival
├── Providers/
│   ├── INotificationProvider.cs
│   ├── Ses/SesEmailProvider.cs
│   ├── SendGrid/SendGridEmailProvider.cs
│   ├── Unifonic/UnifonicSmsProvider.cs
│   ├── VodafoneEgypt/VodafoneEgyptSmsProvider.cs
│   ├── Infobip/InfobipSmsProvider.cs
│   └── Fcm/FcmPushProvider.cs
├── Webhooks/
│   ├── SesWebhookEndpoint.cs           # SNS-signed
│   ├── UnifonicWebhookEndpoint.cs      # HMAC-signed
│   ├── VodafoneEgyptWebhookEndpoint.cs
│   ├── InfobipWebhookEndpoint.cs
│   ├── SendGridWebhookEndpoint.cs
│   └── FcmWebhookEndpoint.cs
├── Templates/                          # Rendering pipeline
│   ├── TemplateRenderer.cs             # Handlebars-style placeholder substitution + RTL preservation
│   ├── PlaceholderValidator.cs
│   └── ArEditorialMarker.cs            # validates AR-reviewed marker
├── Features/                           # Vertical slice handlers (admin endpoints)
│   ├── Templates/{CreateDraft,SubmitForReview,Approve,Reject,Archive}/
│   ├── Campaigns/{Create,Schedule,Pause,Cancel,GetReport}/
│   ├── Preferences/{Get,Update,Unsubscribe}/
│   ├── DeadLetter/{List,RetryNow,Discard}/
│   ├── ProviderRouting/{Get,Set,Failover}/
│   └── Deliveries/{Query}/
├── Seeding/
│   └── NotificationsV1Seeder.cs        # Sample templates AR + EN; sample delivery rows; sample campaigns
└── Tests/
    ├── Unit/                           # one folder per Feature
    ├── Integration/                    # Testcontainers Postgres + provider stubs
    └── Contract/                       # WebApplicationFactory + OpenAPI assertions

apps/admin_web/app/notifications/       # Next.js admin UI (Lane B)
├── templates/                          # editor + review board
├── campaigns/                          # author + monitor
├── dead-letter/
├── provider-routing/
└── deliveries/                         # auditor + operator query view

CODEOWNERS additions:
  /services/backend_api/Modules/Notifications/  @notifications-team
  /apps/admin_web/app/notifications/             @notifications-team
```

**Structure decision**: vertical slice + per-channel provider folders. Subscribers folder isolates upstream-event consumers (so a spec 011 contract change is contained). Hangfire chosen over a custom worker because it's already in the repo footprint and gives free retry + dashboard. OTP gets a dedicated queue (`otp-priority`) for BR-15 isolation.

## Phase 0 — Research (see research.md)

Topics: provider client choice (Refit vs bespoke), Hangfire vs custom worker, RTL preservation in HTML email templates, AR editorial-marker enforcement, FCM service-account JSON handling in containers, idempotency-key derivation strategy, OTP-priority queue isolation pattern, dead-letter archival pattern.

## Phase 1 — Design (see data-model.md, contracts/, quickstart.md)

12 tables defined. Three state machines with explicit transition tables. Provider abstraction interface. OpenAPI artifact for `/notifications/*`, `/admin/notifications/*`, and `/notifications/webhooks/*`.

## Four Guardrails — coverage statement

1. **Lint/format**: `dotnet format` + existing eslint/prettier for admin web.
2. **Contract diff**: OpenAPI artifact updated; new endpoints added to the contract surface; webhook signatures contract-tested via record-replay fixtures.
3. **Fingerprint**: standard.
4. **Code-owner approval**: `Modules/Notifications/**` and `apps/admin_web/app/notifications/**` under `@notifications-team`.

## Cross-spec dependencies

- Hard upstream: 003 (audit, config), 004 (auth + OTP event), 011 (order events), E1 (KV slots).
- Soft upstream: 020 (verification events), 026 (shipping events — race tolerated).
- Downstream: 027 (will emit `payment.*` events 025 will subscribe to), 029 (load tests + AR editorial sweep).

## Risks and mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| OTP latency budget violated under load | Medium | High | Dedicated queue + worker pool (BR-15) + alert on p95 > 30s. |
| AR editorial-quality regression | Medium | High | V-1 publish gate + reviewer ≠ author + spec 029 AR-editorial-reviewer sign-off. |
| Provider webhook duplication | High | Low | `webhooks_received` PK enforces idempotency. |
| Vodafone Egypt API undocumented edge case | Medium | Medium | Backup provider (Infobip) configurable, manual failover at v1, full test fixtures recorded. |
| Email-bounce auto-disable causes silent loss for valid customers | Low | High | Hard-bounce-only auto-disable; soft bounces don't disable; runbook documents re-enable path via profile. |

## Phase 2 readiness

Plan is /speckit-tasks-ready. Six phase groups pre-defined: Foundations + State machines, Templates, Transactional dispatch + providers + webhooks, Campaigns + preferences, Operator surfaces (dead-letter + routing + failover), Audit + load + AR editorial sign-off.
