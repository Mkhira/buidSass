# Contract: 026 — Shipping (operator + customer + integration surface)

**Phase**: 1
**Contract version**: 1.0.0 (026 ratified, ADR-008 Accepted)

## §1 Customer endpoints
| Endpoint | Method | Auth | Notes |
|---|---|---|---|
| `/shipping/quote` | POST | customer JWT or guest cart-token | Returns eligible-method list with exact fees. |
| `/shipping/track/{tracking_number}` | GET | guest with order-id challenge or customer JWT | Returns timeline. |

## §2 Admin endpoints (under `/admin/shipping/...`)
| Endpoint | Method | Permission |
|---|---|---|
| `/methods` | GET, POST | `shipping-method-author` |
| `/methods/{id}:submit/:approve/:reject/:archive` | POST | role-appropriate |
| `/methods/{id}/fee-tables` | GET, POST | `shipping-method-author` |
| `/zones` | GET, POST, PATCH | `shipping-method-author` |
| `/provider-routing` | GET, PUT | `shipping-operator` |
| `/provider-routing/{market}/{method}:failover` | POST | `shipping-operator` |
| `/shipments` | GET (filterable) | `shipping-operator`, `auditor` |
| `/shipments/{id}:mark-handed-over` | POST | `warehouse-staff` |
| `/shipments/{id}:dispute` | POST | `shipping-operator` |
| `/shipments/{id}:create-re-delivery` | POST | `shipping-operator` |
| `/shipments/{id}:void-label` | POST | `shipping-operator` |
| `/dead-letter-labels` | GET | `shipping-operator` |
| `/dead-letter-labels/{id}:retry/:discard` | POST | `shipping-operator` |

## §3 Webhook endpoints
| Endpoint | Method | Auth |
|---|---|---|
| `/shipping/webhooks/smsa` | POST | HMAC-SHA256 (vault key) |
| `/shipping/webhooks/aramex-ksa` | POST | HMAC-SHA256 |
| `/shipping/webhooks/aramex-eg` | POST | HMAC-SHA256 |
| `/shipping/webhooks/bosta` | POST | HMAC-SHA256 |

Common: idempotent on `(provider_id, provider_tracking_id, event_kind, occurred_at)` PK; signature fail-closed (401); 200 OK on idempotent re-delivery.

## §4 Internal subscribers
- `OrderConfirmedSubscriber` ← `order.confirmed` (spec 011)
- `OrderCancelledSubscriber` ← `order.cancelled` (cascade label-void if label was purchased)
- `RefundInitiatedSubscriber` ← `order.refund_initiated` (return-shipment trigger)

## §5 Emitted events (consumed by other modules)
- `shipping.label_purchased` → consumed by 025 (notifications)
- `shipping.status_changed` → consumed by 025
- `shipping.delivery_disputed`, `shipping.re_delivery_created`, `shipping.sla_breach` → consumed by 025 + analytics

## §6 `IShippingProvider` interface
```csharp
public interface IShippingProvider
{
    string ProviderId { get; }
    bool SupportsMarket(string marketCode);
    Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentDispatch d, CancellationToken ct);
    Task<VoidLabelResult> VoidLabelAsync(string trackingId, CancellationToken ct);
    Task<TrackingResult> GetTrackingAsync(string trackingId, CancellationToken ct);  // optional poll
    bool ValidateWebhookSignature(HttpRequest req, IReadOnlyDictionary<string,string> vaultSecrets);
    WebhookEvent ParseWebhookEvent(HttpRequest req);  // returns canonical event
}

public record CreateShipmentDispatch(
    Guid ShipmentId,
    string MarketCode,
    string MethodKey,
    string RecipientNameRedacted,            // first name + last initial
    string RecipientPhoneMaskedLast4,
    AddressMinimized ShipTo,                  // minimized fields per BR-15
    decimal WeightKg,
    string CurrencyCode,
    decimal DeclaredValueAmount);
```

## §7 Audit-event contract
Per data-model.md §audit. Every event carries `correlation_id` linking related rows (e.g., `shipping.label_purchased` → `shipping.status_changed` chain).

## §8 Versioning
- Adding a provider for an existing market+method = non-breaking (config + new impl).
- Adding a new state to the Shipment state machine = breaking (consumers must handle); requires spec amendment.
- Adding a new event_kind = non-breaking.
- Removing mandatory payload key = breaking.
