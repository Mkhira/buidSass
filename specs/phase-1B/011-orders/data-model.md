# Data Model — Orders v1 (Spec 011)

**Date**: 2026-04-22 · Schema: `orders`.

## Tables (9)

### 1. `orders.orders`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `order_number` | text UNIQUE NOT NULL | `ORD-KSA-202604-000187` |
| `account_id` | uuid FK → identity.accounts | |
| `market_code` | citext NOT NULL | |
| `currency` | citext NOT NULL | |
| `subtotal_minor` / `discount_minor` / `tax_minor` / `shipping_minor` / `grand_total_minor` | bigint | |
| `price_explanation_id` | uuid FK → pricing.price_explanations | immutable reference |
| `coupon_code` | citext NULL | |
| `shipping_address_json` / `billing_address_json` | jsonb NOT NULL | snapshot |
| `b2b_po_number` / `b2b_reference` / `b2b_notes` | text NULL | |
| `b2b_requested_delivery_from` / `..._to` | timestamptz NULL | |
| `order_state` | citext NOT NULL | `placed`\|`cancellation_pending`\|`cancelled` |
| `payment_state` | citext NOT NULL | `authorized`\|`captured`\|`pending_cod`\|`pending_bank_transfer`\|`failed`\|`voided`\|`refunded`\|`partially_refunded` |
| `fulfillment_state` | citext NOT NULL | `not_started`\|`awaiting_stock`\|`picking`\|`packed`\|`handed_to_carrier`\|`delivered`\|`cancelled` |
| `refund_state` | citext NOT NULL | `none`\|`requested`\|`partial`\|`full` |
| `placed_at` | timestamptz NOT NULL | |
| `cancelled_at` / `delivered_at` | timestamptz NULL | |
| `quotation_id` | uuid NULL FK → quotations | if originated from a quote |
| `checkout_session_id` | uuid NULL FK → checkout.sessions | |
| `owner_id` / `vendor_id` | P6 | |
| `row_version` | bytea NOT NULL | |

Indexes: `(account_id, placed_at DESC)`, `(market_code, placed_at)`, `(payment_state)`, `(fulfillment_state)`.

### 2. `orders.order_lines`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `order_id` | uuid FK → orders | |
| `product_id` | uuid FK → catalog.products | |
| `sku` | citext NOT NULL | snapshot |
| `name_ar` / `name_en` | text | snapshot |
| `qty` | int NOT NULL | |
| `unit_price_minor` / `line_discount_minor` / `line_tax_minor` / `line_total_minor` | bigint | |
| `restricted` | bool NOT NULL | snapshot |
| `attributes_json` | jsonb NOT NULL | snapshot |
| `cancelled_qty` | int NOT NULL DEFAULT 0 | item-level cancellations |
| `returned_qty` | int NOT NULL DEFAULT 0 | aggregated from spec 013 |

### 3. `orders.shipments`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `order_id` | uuid FK → orders | |
| `provider_id` | citext NOT NULL | |
| `method_code` | citext NOT NULL | |
| `tracking_number` | text NULL | |
| `carrier_label_url` | text NULL | |
| `eta_from` / `eta_to` | timestamptz NULL | |
| `state` | citext NOT NULL | `created`\|`handed_to_carrier`\|`in_transit`\|`out_for_delivery`\|`delivered`\|`returned`\|`failed` |
| `created_at` / `handed_to_carrier_at` / `delivered_at` | timestamptz NULL | |
| `payload_json` | jsonb | provider raw |

### 4. `orders.shipment_lines`
| column | type | notes |
|---|---|---|
| `shipment_id` | uuid FK → shipments | |
| `order_line_id` | uuid FK → order_lines | |
| `qty` | int NOT NULL | |

PK `(shipment_id, order_line_id)`.

### 5. `orders.order_state_transitions`
Per-state-machine transition audit.
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `order_id` | uuid FK → orders | |
| `machine` | citext NOT NULL | `order`\|`payment`\|`fulfillment`\|`refund` |
| `from_state` / `to_state` | citext | |
| `actor_account_id` | uuid NULL | system transitions NULL |
| `trigger` | citext NOT NULL | |
| `reason` | text NULL | |
| `occurred_at` | timestamptz NOT NULL | |
| `context_json` | jsonb NULL | e.g. webhook payload id |

### 6. `orders.quotations`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `quote_number` | text UNIQUE NOT NULL | `QUO-{market}-{yyyymm}-{seq6}` |
| `account_id` | uuid NOT NULL | |
| `market_code` | citext NOT NULL | |
| `status` | citext NOT NULL | `draft`\|`active`\|`accepted`\|`rejected`\|`expired`\|`converted` |
| `price_explanation_id` | uuid FK → pricing.price_explanations | |
| `valid_until` | timestamptz NOT NULL | |
| `created_by_account_id` | uuid | admin or self-service |
| `converted_order_id` | uuid NULL FK → orders | |
| `created_at` / `updated_at` | timestamptz | |

### 7. `orders.quotation_lines`
Snapshot of requested lines (same columns as `order_lines` minus shipping/restock).

### 8. `orders.orders_outbox`
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `event_type` | citext NOT NULL | `order.placed`, `payment.captured`, `fulfillment.shipped`, `fulfillment.delivered`, `order.cancelled`, etc. |
| `aggregate_id` | uuid NOT NULL | order id |
| `payload_json` | jsonb NOT NULL | |
| `committed_at` | timestamptz NOT NULL | |
| `dispatched_at` | timestamptz NULL | |

Partial index `WHERE dispatched_at IS NULL`.

### 9. `orders.cancellation_policies`
| column | type | notes |
|---|---|---|
| `market_code` | citext PK | |
| `authorized_cancel_allowed` | bool NOT NULL DEFAULT true | |
| `captured_cancel_hours` | int NOT NULL DEFAULT 24 | refund path enabled within |
| `updated_by_account_id` | uuid | audit |
| `updated_at` | timestamptz | |

Return window policy is owned by spec 013 `returns.return_policies.return_window_days`; spec 011's `GET /v1/customer/orders/{id}/return-eligibility` handler reads from spec 013 via direct DB join (same PostgreSQL instance, cross-schema read permitted inside the monolith). Do NOT duplicate the column here.

---

## State machines

### SM-1 · Order
States: `placed`, `cancellation_pending`, `cancelled`.
| from | to | trigger |
|---|---|---|
| placed | cancellation_pending | customer cancel (captured payment) |
| placed | cancelled | customer cancel (authorized payment) OR admin cancel |
| cancellation_pending | cancelled | refund completed |

### SM-2 · Payment
States: `authorized`, `captured`, `pending_cod`, `pending_bank_transfer`, `failed`, `voided`, `refunded`, `partially_refunded`.
| from | to | trigger |
|---|---|---|
| authorized | captured | webhook / manual capture |
| authorized | voided | cancellation |
| captured | refunded | full refund |
| captured | partially_refunded | partial refund |
| pending_cod | captured | delivery confirmation |
| pending_cod | failed | delivery failure |
| pending_bank_transfer | captured | admin confirm |
| pending_bank_transfer | failed | timeout / admin reject |

### SM-3 · Fulfillment
States: `not_started`, `awaiting_stock`, `picking`, `packed`, `handed_to_carrier`, `delivered`, `cancelled`.
| from | to | trigger |
|---|---|---|
| not_started | picking | admin |
| not_started | awaiting_stock | order created with insufficient stock (rare) |
| awaiting_stock | picking | stock arrives |
| picking | packed | admin |
| packed | handed_to_carrier | admin + shipment created |
| handed_to_carrier | delivered | carrier webhook / admin |
| any | cancelled | order cancelled |

### SM-4 · Refund
States: `none`, `requested`, `partial`, `full`.
| from | to | trigger |
|---|---|---|
| none | requested | spec 013 return request |
| requested | partial | partial refund issued |
| requested | full | full refund issued |
| partial | full | subsequent refund covers remainder |

## Audit
Every transition → `order_state_transitions` row; admin mutations additionally write to spec 003 `audit_log_entries`.
