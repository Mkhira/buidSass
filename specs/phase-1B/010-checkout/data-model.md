# Data Model — Checkout v1 (Spec 010)

**Date**: 2026-04-22 · Schema: `checkout`.

## Tables (5)

### 1. `checkout.sessions`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `cart_id` | uuid FK → cart.carts | |
| `account_id` | uuid NULL | populated on auth |
| `cart_token_hash` | bytea NULL | guest surface |
| `market_code` | citext NOT NULL | |
| `state` | citext NOT NULL | enumerated below |
| `shipping_address_json` | jsonb NULL | snapshot |
| `billing_address_json` | jsonb NULL | may equal shipping |
| `shipping_provider_id` | citext NULL | |
| `shipping_method_code` | citext NULL | |
| `shipping_fee_minor` | bigint NULL | cached |
| `payment_method` | citext NULL | `card`\|`mada`\|`apple_pay`\|`stc_pay`\|`bank_transfer`\|`cod`\|`bnpl` |
| `coupon_code` | citext NULL | passthrough |
| `issued_explanation_id` | uuid NULL FK → pricing.price_explanations | populated at submit |
| `last_preview_hash` | bytea NULL | for drift detection |
| `accepted_drift_at` | timestamptz NULL | |
| `last_touched_at` | timestamptz NOT NULL | for expiry |
| `expires_at` | timestamptz NOT NULL | derived |
| `submitted_at` / `confirmed_at` / `failed_at` / `expired_at` | timestamptz NULL | |
| `order_id` | uuid NULL FK → orders.orders | set on confirm |
| `failure_reason_code` | citext NULL | |
| `row_version` | bytea NOT NULL | |

Index `(account_id, state, last_touched_at)` for admin list. Index `(state, expires_at)` for expiry worker.

### 2. `checkout.payment_attempts`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `session_id` | uuid FK → sessions | |
| `provider_id` | citext NOT NULL | |
| `method` | citext NOT NULL | |
| `amount_minor` | bigint NOT NULL | |
| `currency` | citext NOT NULL | |
| `state` | citext NOT NULL | `initiated`\|`authorized`\|`captured`\|`declined`\|`voided`\|`failed`\|`pending_webhook` |
| `provider_txn_id` | text NULL | |
| `error_code` | text NULL | |
| `error_message` | text NULL | |
| `created_at` / `updated_at` | timestamptz | |

Index `(session_id, created_at)`. Index `(provider_id, provider_txn_id)`.

### 3. `checkout.shipping_quotes`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `session_id` | uuid FK → sessions | |
| `provider_id` | citext NOT NULL | |
| `method_code` | citext NOT NULL | |
| `eta_min_days` / `eta_max_days` | int | |
| `fee_minor` | bigint NOT NULL | |
| `currency` | citext NOT NULL | |
| `expires_at` | timestamptz NOT NULL | 10 min from fetch |
| `payload_json` | jsonb | provider raw |

### 4. `checkout.payment_webhook_events`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `provider_id` | citext NOT NULL | |
| `provider_event_id` | text NOT NULL | |
| `event_type` | citext NOT NULL | |
| `signature_verified` | bool NOT NULL | |
| `received_at` | timestamptz NOT NULL | |
| `handled_at` | timestamptz NULL | |
| `raw_payload` | jsonb NOT NULL | |

Unique `(provider_id, provider_event_id)`.

### 5. `checkout.idempotency_results`
| column | type | notes |
|---|---|---|
| `idempotency_key` | text PK | |
| `account_id` | uuid NULL | may be guest token hash |
| `request_fingerprint` | bytea NOT NULL | sha-256 of normalized body |
| `response_status` | int NOT NULL | |
| `response_json` | jsonb NOT NULL | |
| `created_at` | timestamptz NOT NULL | |
| `expires_at` | timestamptz NOT NULL | 5 min |

Index `(expires_at)` for cleanup.

---

## State machine — `CheckoutSession`
States: `init`, `addressed`, `shipping_selected`, `payment_selected`, `submitted`, `confirmed`, `failed`, `expired`.

| from | to | trigger | actor |
|---|---|---|---|
| init | addressed | set shipping address | customer |
| addressed | shipping_selected | select shipping method | customer |
| shipping_selected | payment_selected | select payment method | customer |
| payment_selected | submitted | submit (idempotent) | customer |
| submitted | confirmed | payment captured + order created | system |
| submitted | failed | payment declined / order-create error | system |
| failed | payment_selected | customer retries with new method | customer |
| any pre-submit | expired | TTL elapsed | worker |
| expired | — | terminal | |
| confirmed | — | terminal | |

## State machine — `PaymentAttempt`
`initiated → authorized → captured` on success; `initiated → declined` or `initiated → failed` on error; `authorized → voided` on compensation; `initiated → pending_webhook` while awaiting asynchronous providers.

## Audit + events
Every state transition on session + attempt writes an audit row (Principle 25). Events:
- `checkout.session.created|addressed|shipping_selected|payment_selected|submitted|confirmed|failed|expired|admin_expired`
- `checkout.payment.authorized|captured|declined|voided|refunded|pending_webhook`
- `checkout.webhook.received|deduped`
