# Data Model: 026 — Shipping

**Phase**: 1
**Date**: 2026-05-10

11 tables under `shipping` schema. All carry the four mandatory columns from spec 003 (`created_at`, `updated_at`, `deleted_at`, `market_code` where applicable).

## Tables

### `shipping.shipping_methods`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| market_code | text NOT NULL | `sa`, `eg` |
| name_ar | text NOT NULL | |
| name_en | text NOT NULL | |
| description_ar | text | |
| description_en | text | |
| current_version_id | uuid FK → method_versions(id) | nullable until first publish |
| state | text | derived from current version |
| created_at, updated_at, deleted_at | timestamptz | |

### `shipping.shipping_method_versions`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| method_id | uuid FK | |
| version_no | int | unique within method |
| state | text | `draft`, `in_review`, `published`, `archived` |
| eligibility_jsonb | jsonb | min_cart_total, eligible_customer_tiers, time-window, etc. |
| eta_min_hours | int | |
| eta_max_hours | int | |
| author_id | uuid FK → auth.users | |
| reviewer_id | uuid FK → auth.users | nullable |
| effective_at | timestamptz | nullable; if set, fee tables tied to this version do not apply before this time |
| published_at, archived_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

Constraint: `published_at IS NOT NULL` ⇒ `reviewer_id <> author_id`.

### `shipping.shipping_zones`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| market_code | text NOT NULL | |
| name_ar | text NOT NULL | |
| name_en | text NOT NULL | |
| postal_code_prefixes | jsonb | array of strings |
| city_list | jsonb | array of city slugs |
| created_at, updated_at, deleted_at | timestamptz | |

### `shipping.fee_tables`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| method_version_id | uuid FK | |
| zone_id | uuid FK | |
| weight_min_kg | numeric(6,2) NOT NULL | |
| weight_max_kg | numeric(6,2) NOT NULL | |
| fee_amount | numeric(10,2) NOT NULL | |
| currency | text NOT NULL | `SAR`, `EGP` |
| effective_at | timestamptz NOT NULL | |
| created_at, updated_at, deleted_at | timestamptz | |

Exclusion constraint: `(method_version_id, zone_id, weight_min_kg, weight_max_kg)` non-overlapping (research §4).

### `shipping.shipments`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| order_id | uuid FK → orders.orders | UNIQUE (BR-4 single-shipment-per-order) |
| market_code | text NOT NULL | |
| method_version_id | uuid FK | snapshot |
| provider_id | text NOT NULL | resolved at creation |
| provider_tracking_id | text | populated on label-purchase |
| label_pdf_blob_url | text | SAS-signed URL surface; raw blob path stored separately |
| state | text NOT NULL | shipment state machine |
| ship_to_address_redacted_jsonb | jsonb NOT NULL | masked phone (last-4) + recipient name + address |
| parent_shipment_id | uuid FK → shipments(id) | for re-delivery linkage |
| attempts | int NOT NULL DEFAULT 0 | |
| eta_min, eta_max | timestamptz | |
| label_purchased_at, handed_at, in_transit_at, delivered_at | timestamptz | nullable |
| failed_reason | text | |
| created_at, updated_at, deleted_at | timestamptz | |

Indexes: `(state, market_code)` partial WHERE state IN active states, `(provider_id, provider_tracking_id)`, `(order_id)`, `(parent_shipment_id)`.

### `shipping.shipment_events`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| shipment_id | uuid FK | |
| provider_event_kind | text | raw provider's status |
| internal_state_at_event | text | resolved internal state |
| occurred_at | timestamptz | provider's event time |
| received_at | timestamptz | our reception time |
| raw_payload_redacted | jsonb | provider payload stripped of PII |
| created_at | timestamptz | |

### `shipping.webhooks_received`
| Column | Type | Notes |
|---|---|---|
| provider_id | text | composite PK |
| provider_tracking_id | text | composite PK |
| event_kind | text | composite PK |
| occurred_at | timestamptz | composite PK |
| signature_validated | boolean NOT NULL | always true on accepted rows |

PK enforces idempotency (BR-6).

### `shipping.provider_routing`
| Column | Type | Notes |
|---|---|---|
| market_code | text | composite PK |
| method_id | uuid FK | composite PK |
| primary_provider_id | text NOT NULL | |
| backup_provider_id | text | nullable |
| auto_failover_enabled | boolean NOT NULL DEFAULT false | clarify-locked default |
| failover_threshold_pct | int NOT NULL DEFAULT 50 | range [10,90] |
| failover_window_minutes | int NOT NULL DEFAULT 5 | |
| updated_at | timestamptz | |

Constraint: `primary_provider_id <> backup_provider_id`.

### `shipping.dead_letter_labels`
| Column | Type | Notes |
|---|---|---|
| shipment_id | uuid PK FK | |
| last_error_message_redacted | text | |
| last_error_code | text | |
| entered_at | timestamptz | |
| resolved_at | timestamptz | nullable |
| resolution | text | `retry`, `discard`, `manual_label` |
| resolved_by | uuid FK → auth.users | nullable |

### `shipping.market_schemas`
| Column | Type | Notes |
|---|---|---|
| market_code | text PK | |
| postal_code_regex | text | for address-format validation |
| default_currency | text NOT NULL | `SAR` for sa, `EGP` for eg |
| default_eta_days_min, _max | int | |
| sla_breach_threshold_hours | int NOT NULL | per shipment age |
| updated_at | timestamptz | |

### `shipping.shipment_disputes`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| shipment_id | uuid FK | |
| reported_at | timestamptz NOT NULL | |
| reported_by | text | `customer`, `support_agent` |
| status | text | `open`, `re_delivered`, `closed_with_refund`, `closed_no_action` |
| resolution_notes | text | |
| resolved_at | timestamptz | |
| resolved_by | uuid FK | |

## State Machine — Shipment

```
pending
  └─→ label_purchased ──→ handed_to_carrier ──→ in_transit ──→ out_for_delivery ──→ delivered
                                                                       │
                                                                       └─→ delivery_attempted (loop ≤ 3) ──→ return_to_sender_initiated ──→ returned_to_sender
                                                       
delivered ──→ delivery_disputed ──→ re_delivered_pending ─→ (new shipment) ∪ closed_with_refund

pending ──→ failed_to_create_label / pending_label_provider_failure ─→ dead_letter_label

(any active) ──→ label_voided
```

Precedence (BR-12, descending): `delivered > returned_to_sender > delivery_attempted > out_for_delivery > in_transit > handed_to_carrier > label_purchased > pending`.

## Audit-event additions

`shipping.method_published`, `shipping.method_archived`, `shipping.fee_table_updated`, `shipping.label_purchased`, `shipping.handed_to_carrier`, `shipping.status_changed`, `shipping.delivery_disputed`, `shipping.re_delivery_created`, `shipping.label_voided`, `shipping.label_creation_failed`, `shipping.dead_letter`, `shipping.sla_breach`, `provider.degraded`, `provider.failover`, `secret.placeholder_replaced`.

Retention ≥ 365 days for audit; shipment hot-data ≥ 90 days.

## Cross-references

- `shipping.shipments.order_id` → `orders.orders.id` (spec 011).
- E1 KV slots populated by 026: `shipping/sa/smsa/api-key`, `shipping/sa/aramex/api-key`, `shipping/eg/bosta/api-key`, `shipping/eg/aramex/api-key`. Each emits `secret.placeholder_replaced`.
- 025 subscribes to `shipping.status_changed`.
