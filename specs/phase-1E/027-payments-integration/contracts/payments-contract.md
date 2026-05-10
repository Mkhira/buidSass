# Contract: 027 — Payments Integration

**Phase**: 1
**Contract version**: 1.0.0 (027 ratified, ADR-007 Accepted, PCI scope SAQ-A)

## §1 Customer endpoints

| Endpoint | Method | Auth | Notes |
|---|---|---|---|
| `/payments/methods?market=&cart_total=` | GET | customer JWT | Returns enabled methods after eligibility filter |
| `/payments` | POST | customer JWT | Creates Payment + returns hosted-fields config OR external-redirect URL OR COD/bank-transfer instructions |
| `/payments/{id}` | GET | customer JWT (own only) | Returns Payment status (no `provider_message_id` — privacy) |
| `/payments/{id}/retry` | POST | customer JWT | Creates a NEW Payment for the same order |
| `/payments/me/history` | GET | customer JWT | Last 90 days |

## §2 Admin endpoints (`/admin/payments/...`)

| Endpoint | Method | Permission |
|---|---|---|
| `/provider-routing` | GET, PUT | `payments-operator` |
| `/provider-routing/{market}/{method}:failover` | POST | `payments-operator` |
| `/method-config` | GET, PUT | `payments-operator` |
| `/payments` (filterable) | GET | `payments-operator`, `auditor` |
| `/payments/{id}:refund` | POST `{amount, reason}` | `payments-operator` |
| `/payments/{id}/cod:mark-captured/:mark-failed` | POST | `warehouse-operator` |
| `/payments/{id}/bank-transfer:mark-captured` | POST | `payments-operator` |
| `/reconciliation/runs` | GET | `payments-operator`, `auditor` |
| `/reconciliation/exceptions` | GET (filterable) | `payments-operator`, `auditor` |
| `/reconciliation/exceptions/{id}:resolve` | POST `{action, notes}` | `payments-operator` |
| `/webhook-replay` | POST `{provider, from, to}` | `payments-operator` |
| `/chargebacks` | GET, PATCH | `payments-operator` |

## §3 Webhook endpoints

`/payments/webhooks/{hyperpay|tap|paymob|kashier|tabby|tamara|valu}` — HMAC signature-validated, idempotent on `(provider_id, provider_message_id, event_kind)` PK.

## §4 Internal subscribers (MediatR)

- `OrderConfirmedSubscriber` (defensive Payment-creation if not pre-created at checkout)
- `OrderCancelledSubscriber` (cascades refund if Payment is captured)
- `RefundInitiatedSubscriber` (consumes `order.refund_initiated` from spec 011)

## §5 Emitted events

- `payment.captured` (subscribed by spec 011 order, spec 012 invoice, spec 025 notifications)
- `payment.failed`, `payment.refunded`, `payment.partially_refunded`, `payment.expired`, `payment.chargeback` (subscribed by 025)

## §6 `IPaymentProvider` interface

```csharp
public interface IPaymentProvider
{
    string ProviderId { get; }
    bool SupportsMarket(string marketCode);
    bool SupportsMethod(string method);
    Task<CreatePaymentResult> CreatePaymentAsync(CreatePaymentDispatch d, CancellationToken ct);
    Task<RefundResult> RefundAsync(RefundDispatch d, CancellationToken ct);
    Task<PaymentStatusResult> GetPaymentStatusAsync(string providerMessageId, CancellationToken ct); // poll fallback
    bool ValidateWebhookSignature(HttpRequest req, IReadOnlyDictionary<string,string> vaultSecrets);
    WebhookEvent ParseWebhookEvent(HttpRequest req);
    Task<SettlementLedger> FetchSettlementLedgerAsync(DateOnly date, CancellationToken ct); // capability-flagged
    Task<WebhookReplayResult> ReplayWebhooksAsync(DateRange range, CancellationToken ct); // capability-flagged
    bool SupportsWebhookReplay { get; }
    bool SupportsLedgerApi { get; } // false → CSV/SFTP fallback path
}

public record CreatePaymentDispatch(
    Guid PaymentId,
    Guid OrderId,
    string MarketCode,
    string Method,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string CustomerRef,                  // NEVER customer email or PII
    string RecipientNameRedacted,        // first name + last initial
    string RecipientPhoneMaskedLast4);
```

**Egress field allow-list (BR-14 enforcement)**: `EgressPayloadFilter` rejects any provider call whose payload includes a key not in the allow-list. The list lives in code at `Modules/Payments/PciScope/EgressPayloadFilter.cs` and is CODEOWNERS-protected.

## §7 PCI scope contract

The following are **NEVER stored, logged, or transmitted** by this module:

- Primary Account Number (PAN)
- Card Verification Value (CVV / CVC / CVV2)
- Track 1 / Track 2 / Magstripe data
- Card PIN
- Full expiration date (only the provider-issued token is referenced; the token may itself be a hashed reference, not a card facsimile)

CI guard `scripts/ci/check-pci-scope.sh` greps EF entity definitions, migration files, and payload-builder code paths for cardholder-shaped column names and field assignments. Rejects PRs on any match.

`PciScopeMonitor` runs nightly and emits `pci_scope.config_changed` audit events on any Bicep / KV / hosted-fields-domain change.

## §8 Audit-event contract

Per data-model.md §audit. Each event carries `correlation_id` linking related events (e.g., `payment.created` → `payment.authorized` → `payment.captured` chain shares one correlation_id; refunds derive a child correlation_id linked to the parent payment).

## §9 Versioning

- Adding a new method to the closed set (`card`, `apple_pay`, `mada`, `stc_pay`, `meeza`, `bnpl_tabby`, `bnpl_tamara`, `bnpl_valu`, `cod`, `bank_transfer`) = breaking; spec amendment + ADR-007 update required.
- Adding a new provider for an existing method = non-breaking (config + adapter).
- Adding a new state to Payment state machine = breaking (consumers must handle).
- Removing a mandatory payload key on an audit event = breaking.
