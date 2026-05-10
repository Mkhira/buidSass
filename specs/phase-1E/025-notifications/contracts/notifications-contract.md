# Contract: 025 — Notifications

**Phase**: 1 (Design — Contracts)
**Date**: 2026-05-10
**Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md) · **Data model**: [../data-model.md](../data-model.md)

This document defines the public contract surfaces of the notifications module: customer endpoints, admin endpoints, provider webhook endpoints, internal event subscribers, and the `INotificationProvider` interface.

Contract version: **1.0.0** (025 ratified, ADR-009 Accepted).

## §1 — Customer endpoints

| Endpoint | Method | Auth | Request | Response |
|---|---|---|---|---|
| `/notifications/me` | GET | customer JWT | `?channel=...&from=...&to=...&limit=50` | `{ items: [{id, channel, event_kind, state, delivered_at, ...}], next_cursor }` |
| `/notifications/me/preferences` | GET | customer JWT | — | `{ preferences: [{channel, category, enabled}], market_code }` |
| `/notifications/me/preferences` | PATCH | customer JWT | `[{channel, category, enabled}]` | `{ preferences: [...] }` — rejects setting `transactional` to `enabled=false` (422) |
| `/notifications/unsubscribe?token=<s>` | GET | none (token-validated) | — | HTML confirmation page in customer locale |
| `/notifications/unsubscribe` | POST | none (token-validated) | `{token}` | `{ status: 'opted_out' }` or `{ error: 'expired' \| 'invalid' }` |

## §2 — Admin endpoints (under `/admin/notifications/...`)

| Endpoint | Method | Permission | Notes |
|---|---|---|---|
| `/templates` | GET | `template-reader` | Filter by event_kind, state |
| `/templates` | POST | `template-author` | Create draft |
| `/templates/{id}` | GET | `template-reader` | |
| `/templates/{id}` | PATCH | `template-author` | Draft only |
| `/templates/{id}:submit` | POST | `template-author` | draft → in_review |
| `/templates/{id}:approve` | POST | `template-reviewer` | Reviewer ≠ author + `ar_editorial_reviewed=true` |
| `/templates/{id}:reject` | POST | `template-reviewer` | in_review → draft with comment |
| `/templates/{id}:archive` | POST | `template-author` or `template-reviewer` | published → archived |
| `/templates/{id}:render-preview` | POST | `template-reader` | `{sample_payload, locale}` → rendered output |
| `/campaigns` | GET, POST | `campaign-manager` | |
| `/campaigns/{id}` | GET, PATCH | `campaign-manager` | PATCH only on draft |
| `/campaigns/{id}:schedule` | POST | `campaign-manager` | `{send_at}` |
| `/campaigns/{id}:pause` | POST | `campaign-manager` | sending → paused |
| `/campaigns/{id}:resume` | POST | `campaign-manager` | paused → sending |
| `/campaigns/{id}:cancel` | POST | `campaign-manager` | terminal |
| `/campaigns/{id}/report` | GET | `campaign-manager` | counts |
| `/dead-letter` | GET | `notifications-operator` | |
| `/dead-letter/{notification_id}:retry` | POST | `notifications-operator` | re-enqueue |
| `/dead-letter/{notification_id}:discard` | POST | `notifications-operator` | mark resolved=discard |
| `/provider-routing` | GET, PUT | `notifications-operator` | |
| `/provider-routing/{market}/{channel}:failover` | POST | `notifications-operator` | manual primary↔backup swap |
| `/deliveries` | GET | `auditor`, `notifications-operator` | filterable by market + channel + event_kind + date range |

All admin endpoints emit audit events at every state-changing call (per BR-12).

## §3 — Provider webhook endpoints

| Endpoint | Method | Auth | Notes |
|---|---|---|---|
| `/notifications/webhooks/ses` | POST | SNS topic signature | SES bounce/complaint/delivery |
| `/notifications/webhooks/sendgrid` | POST | event-webhook signature | SendGrid bounce/delivery |
| `/notifications/webhooks/unifonic` | POST | HMAC-SHA256 (vault-key) | Unifonic delivery report |
| `/notifications/webhooks/vodafone-egypt` | POST | HMAC-SHA256 (vault-key) | Vodafone Egypt DLR |
| `/notifications/webhooks/infobip` | POST | HMAC-SHA256 (vault-key) | Infobip DLR |
| `/notifications/webhooks/fcm` | POST | OIDC token (FCM) | FCM unregistered/delivery |

Common contract:
- All endpoints accept signed bodies; signature validated fail-closed (401 on mismatch).
- Idempotent via `(provider_id, provider_message_id, event_kind)` PK in `webhooks_received`.
- 200 OK on success, including idempotent re-delivery (do NOT 409 on duplicate; provider would retry on 4xx/5xx).

## §4 — Internal event subscribers (MediatR)

Subscribed events (consumed via `INotificationHandler<T>`):

| Source spec | Event | Subscriber |
|---|---|---|
| 004 | `auth.otp_requested` | `OtpRequestedSubscriber` |
| 011 | `order.placed` | `OrderEventSubscriber` |
| 011 | `order.confirmed` | `OrderEventSubscriber` |
| 011 | `order.shipped` | `OrderEventSubscriber` |
| 011 | `order.delivered` | `OrderEventSubscriber` |
| 011 | `order.cancelled` | `OrderEventSubscriber` |
| 011 | `order.refund_initiated` | `RefundEventSubscriber` |
| 011 | `order.refund_completed` | `RefundEventSubscriber` |
| 020 | `verification.approved` | `VerificationResultSubscriber` |
| 020 | `verification.rejected` | `VerificationResultSubscriber` |
| 7-a/b | `pricing.price_dropped` | `PriceDropSubscriber` |
| inv | `inventory.restocked` | `RestockSubscriber` |
| cart | `cart.abandoned_24h` | `AbandonedCartSubscriber` |
| 026 | `shipping.status_changed` | `ShippingStatusSubscriber` (subscribed pre-026; tolerates absence) |

Each subscriber:
- Validates the event against an `event_timestamp ≥ subscriber_registered_at` rule (replay-safe).
- Resolves the recipient → contact methods + preferences.
- Resolves the (channel, market) → primary provider via `provider_routing`.
- Resolves the template version (current `published`).
- Renders → `payload_redacted_jsonb` (PII-stripped).
- Enqueues a `Notification` row in `pending` state with the derived idempotency key.

## §5 — `INotificationProvider` interface

```csharp
public interface INotificationProvider
{
    string ProviderId { get; }            // e.g., "ses", "unifonic"
    Channel Channel { get; }              // SMS | Email | Push
    bool SupportsMarket(string marketCode);
    Task<SendResult> SendAsync(NotificationDispatch dispatch, CancellationToken ct);
    bool ValidateWebhookSignature(HttpRequest req, IReadOnlyDictionary<string,string> vaultSecrets);
    WebhookEvent ParseWebhookEvent(HttpRequest req);
}

public record NotificationDispatch(
    Guid NotificationId,
    Channel Channel,
    string Recipient,                     // email | phone_e164 | fcm_token
    string Subject,                       // empty for SMS
    string Body,
    string Locale,                        // ar | en
    string MarketCode,
    IReadOnlyDictionary<string,string> Headers); // for tracing/idempotency

public record SendResult(bool Accepted, string? ProviderMessageId, string? ErrorCode, string? ErrorMessageRedacted);

public record WebhookEvent(string ProviderMessageId, string EventKind, DateTimeOffset OccurredAt, string? ErrorCode);
```

Conformance: every provider impl MUST be unit-tested for signature-validate-fail-closed and at least one happy-path send.

## §6 — Audit-event contract

(Full schema in data-model.md.)

Every audit event emitted by 025 MUST contain:
- `event_type` ∈ the enum from data-model.md.
- `actor_kind` ∈ `system | customer | admin` plus `actor_id`.
- `correlation_id` linking related events (e.g., `notification.created` → `notification.delivered` share a correlation_id).

## §7 — Versioning policy

- Adding a new channel (e.g., WhatsApp) is **breaking** for the closed-set channel enum; requires spec amendment + ADR-009 update.
- Adding a new provider for an existing channel is **non-breaking** — extend `provider_routing` enum + provider impl.
- Adding a new event_kind is **non-breaking** — append to the templates and subscribers.
- Removing a mandatory payload key on an audit event is **breaking**.
