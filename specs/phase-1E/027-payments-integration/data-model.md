# Data Model: 027 — Payments Integration

**Phase**: 1
**Date**: 2026-05-10

12 tables under the `payments` schema. All inherit the four mandatory columns from spec 003. **Zero cardholder-data columns** — verified by V-1 schema scan.

## Tables

### `payments.payments`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| order_id | uuid FK → orders.orders | |
| market_code | text NOT NULL | `sa`, `eg` |
| method | text NOT NULL | `card`, `apple_pay`, `mada`, `stc_pay`, `meeza`, `bnpl_tabby`, `bnpl_tamara`, `bnpl_valu`, `cod`, `bank_transfer` |
| provider_id | text | resolved at attempt creation; nullable for COD/bank_transfer (native, no provider) |
| provider_message_id | text | populated by provider response; nullable on pending |
| state | text NOT NULL | payment state machine value |
| amount | numeric(12,2) NOT NULL | |
| currency | text NOT NULL | `SAR`, `EGP` |
| idempotency_key | text NOT NULL | unique per (order_id, method, attempt_id) |
| attempt_id | uuid NOT NULL | client-generated to support BR-3 idempotency |
| failed_reason | text | nullable; populated on state=failed |
| expired_reason | text | nullable; on state=expired |
| customer_id | uuid FK → auth.users | |
| recipient_payload_redacted_jsonb | jsonb | masked fields per BR-14 |
| authorized_at, captured_at, failed_at, expired_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

Indexes: `(order_id, created_at DESC)` (latest-attempt resolution), `(state)` partial WHERE state IN active states, `(provider_id, provider_message_id)`, `(idempotency_key) WHERE deleted_at IS NULL` UNIQUE.

**Cardholder columns**: NONE. PAN, CVV, expiry, track-data are NOT stored. Only the provider's token-equivalent is referenced via `provider_message_id`.

### `payments.refunds`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| payment_id | uuid FK → payments | |
| amount | numeric(12,2) NOT NULL | partial-refund support; sum across refunds ≤ payment.amount |
| currency | text NOT NULL | matches payment.currency |
| reason | text | operator-entered |
| state | text NOT NULL | `pending`, `completed`, `failed` |
| provider_refund_id | text | |
| initiated_by | uuid FK → auth.users | |
| failed_reason | text | nullable |
| completed_at, failed_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

Constraint (V-5): trigger validates `SUM(refunds.amount WHERE state IN ('pending','completed')) <= payments.amount`.

### `payments.webhooks_received`
| Column | Type | Notes |
|---|---|---|
| provider_id | text | composite PK |
| provider_message_id | text | composite PK |
| event_kind | text | composite PK |
| received_at | timestamptz NOT NULL | |
| signature_validated | boolean NOT NULL | |
| body_hash | text | sha256 of raw body; for manual debug only |

PK enforces idempotency (BR-4).

### `payments.provider_routing`
| Column | Type | Notes |
|---|---|---|
| market_code | text | composite PK |
| method | text | composite PK |
| primary_provider_id | text NOT NULL | |
| backup_provider_id | text | nullable |
| auto_failover_enabled | boolean NOT NULL DEFAULT false | clarify-locked default |
| failover_threshold_pct | int NOT NULL DEFAULT 50 | |
| failover_window_minutes | int NOT NULL DEFAULT 5 | |
| updated_at | timestamptz | |

Constraint: `primary_provider_id <> backup_provider_id`.

### `payments.payment_methods_market_config`
| Column | Type | Notes |
|---|---|---|
| market_code | text | composite PK |
| method | text | composite PK |
| enabled | boolean NOT NULL DEFAULT true | |
| min_cart_total | numeric(12,2) | nullable |
| max_cart_total | numeric(12,2) | nullable |
| eligibility_jsonb | jsonb | extra rules (e.g., COD postal-code allowlist) |
| updated_at | timestamptz | |

V-7 enforced at app layer.

### `payments.reconciliation_runs`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| started_at, completed_at | timestamptz | |
| date_range_start, date_range_end | date | |
| providers_processed | jsonb | list of provider ids |
| internal_payments_count | int | |
| provider_ledger_rows_count | int | |
| matched_count | int | |
| exceptions_count | int | |
| status | text | `running`, `completed`, `failed` |
| created_at | timestamptz | |

### `payments.reconciliation_exceptions`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| run_id | uuid FK → reconciliation_runs | |
| reason | text NOT NULL | `orphan_provider_row`, `missing_on_provider`, `amount_mismatch`, `currency_mismatch` |
| provider_id | text | |
| provider_ledger_row_jsonb | jsonb | |
| internal_payment_id | uuid FK → payments | nullable for orphan-provider-row |
| internal_amount, provider_amount | numeric(12,2) | for amount_mismatch |
| state | text | `open`, `resolved` |
| resolution | text | `refund_issued`, `internal_correction`, `provider_correction_requested`, `accepted_loss` |
| resolved_by | uuid FK | nullable |
| resolution_notes | text | |
| resolved_at | timestamptz | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

### `payments.chargebacks`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| payment_id | uuid FK | |
| provider_chargeback_id | text | |
| amount | numeric(12,2) NOT NULL | |
| reason_code | text | provider-specific |
| received_at | timestamptz NOT NULL | |
| status | text | `received`, `disputed`, `lost`, `won`, `accepted` |
| resolution_notes | text | |
| resolved_at, resolved_by | timestamptz, uuid | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

### `payments.pci_scope_events`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| event_kind | text | `kv_slot_added`, `kv_slot_removed`, `hosted_fields_domain_changed`, `provider_added`, `provider_removed` |
| changed_by | uuid FK | |
| change_summary | text | |
| created_at | timestamptz | |

Append-only audit-of-record for PCI scope evidence (AC-4 + AC-37).

### `payments.bank_transfer_references`
| Column | Type | Notes |
|---|---|---|
| payment_id | uuid PK FK → payments | |
| reference | text NOT NULL UNIQUE | format `<MARKET>-<UUID4-8>-<HASH4>` |
| matched_bank_statement_entry_jsonb | jsonb | populated on operator match |
| matched_at, matched_by | timestamptz, uuid | nullable |
| created_at, updated_at, deleted_at | timestamptz | |

### `payments.cod_collection_log`
| Column | Type | Notes |
|---|---|---|
| payment_id | uuid PK FK → payments | |
| courier_user_id | uuid | nullable until field-app integration in 1.5 |
| amount_collected | numeric(12,2) | |
| collected_at | timestamptz | |
| operator_confirmed_at | timestamptz | |
| operator_id | uuid FK | |
| outcome | text | `collected`, `refused`, `address_not_found` |
| created_at, updated_at, deleted_at | timestamptz | |

### `payments.idempotency_keys`
| Column | Type | Notes |
|---|---|---|
| key | text PK | |
| payment_id | uuid FK → payments | |
| created_at | timestamptz | |
| expires_at | timestamptz | TTL 24h post-payment-terminal-state |

## State machines

### Payment.state
```
pending_authorization
  ├─→ authorized → capture_failed → captured | expired (auto-void after 24h)
  ├─→ captured (synchronous capture path)
  ├─→ failed (4xx, terminal)
  └─→ expired

pending_external_redirect (BNPL + some Apple Pay)
  ├─→ captured | failed | expired

pending_collection_on_delivery (COD)
  ├─→ captured | failed (cod_collection_failed)

pending_bank_transfer
  ├─→ captured | expired (72h)

captured
  ├─→ refunded (full) | partially_refunded (1+) | chargeback_received
```

### Refund.state
```
pending → completed | failed
```

### ReconciliationException.state
```
open → resolved (one of 4 actions)
```

## Audit-event additions

`payment.created`, `payment.authorized`, `payment.captured`, `payment.failed`, `payment.expired`, `payment.refunded`, `payment.partially_refunded`, `payment.chargeback`, `payment.webhook_replay`, `provider.degraded`, `provider.failover`, `reconciliation.run_started`, `reconciliation.run_completed`, `reconciliation.exception_opened`, `reconciliation.exception_resolved`, `pci_scope.config_changed`, `secret.placeholder_replaced`.

Retention ≥ 365 days.

## Cross-references

- `payments.payments.order_id` → `orders.orders.id` (spec 011 — invoice references payment via spec 012).
- E1 KV slots populated by 027 (each emits `secret.placeholder_replaced`):
  - `payments/sa/hyperpay/{api-key, api-secret, webhook-signing-key}` (3 slots)
  - `payments/sa/tap/{api-key, api-secret, webhook-signing-key}` (3 slots)
  - `payments/sa/tabby/{api-key, api-secret, webhook-signing-key}` (3 slots)
  - `payments/sa/tamara/{api-key, api-secret, webhook-signing-key}` (3 slots)
  - `payments/eg/paymob/{api-key, api-secret, webhook-signing-key}` (3 slots)
  - `payments/eg/kashier/{api-key, api-secret, webhook-signing-key}` (3 slots)
  - `payments/eg/valu/{api-key, api-secret, webhook-signing-key}` (3 slots)

E1's data-model.md §2 reserved 12 placeholder slots; 027 populates 21 (7 providers × 3 keys). The extra slots are extensions of the same `payments/<market>/<provider>/<key>` taxonomy — no taxonomy change required.

## PCI scope verification queries

A nightly compliance query (run by `PciScopeMonitor`):
```sql
SELECT table_name, column_name FROM information_schema.columns
WHERE table_schema = 'payments'
AND column_name ~* '(pan|primary_account_number|card_number|cvv|cvc|track1|track2|magstripe|card_pin|card_expiry)';
```
Expected result: zero rows. Any row triggers a P0 alert.
