# Data Model — Returns & Refunds v1 (Spec 013)

**Date**: 2026-04-22 · Schema: `returns`.

## Tables (9)

### 1. `returns.return_requests`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `return_number` | text UNIQUE NOT NULL | `RET-KSA-202604-000021` |
| `order_id` | uuid FK → orders.orders | |
| `account_id` | uuid FK → identity.accounts | |
| `market_code` | citext NOT NULL | |
| `state` | citext NOT NULL | see SM-1 |
| `submitted_at` | timestamptz NOT NULL | |
| `reason_code` | citext NOT NULL | overall reason |
| `customer_notes` | text NULL | |
| `admin_notes` | text NULL | |
| `decided_at` | timestamptz NULL | approve/reject time |
| `decided_by_account_id` | uuid NULL | |
| `force_refund` | bool NOT NULL DEFAULT false | skip-physical flag |
| `row_version` | bytea NOT NULL | |

Indexes: `(order_id)`, `(account_id, submitted_at DESC)`, `(market_code, state, submitted_at)`.

### 2. `returns.return_lines`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `return_request_id` | uuid FK → return_requests | |
| `order_line_id` | uuid FK → orders.order_lines | |
| `requested_qty` | int NOT NULL | |
| `approved_qty` | int NULL | after admin decision |
| `received_qty` | int NULL | |
| `sellable_qty` | int NULL | |
| `defective_qty` | int NULL | |
| `line_reason_code` | citext NULL | |

Constraint: `approved_qty <= requested_qty`; `received_qty <= approved_qty`; `sellable_qty + defective_qty == received_qty` when inspection complete.

### 3. `returns.inspections`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `return_request_id` | uuid FK | |
| `inspector_account_id` | uuid | |
| `state` | citext NOT NULL | `pending`\|`in_progress`\|`complete` |
| `started_at` / `completed_at` | timestamptz | |

### 4. `returns.inspection_lines`
| column | type | notes |
|---|---|---|
| `inspection_id` | uuid FK | |
| `return_line_id` | uuid FK | |
| `sellable_qty` | int NOT NULL | |
| `defective_qty` | int NOT NULL | |
| `photos_json` | jsonb NULL | internal inspection photos |

PK `(inspection_id, return_line_id)`.

### 5. `returns.refunds`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `return_request_id` | uuid FK | |
| `provider_id` | citext NULL | null for manual bank transfer |
| `captured_transaction_id` | text NULL | original capture reference |
| `amount_minor` | bigint NOT NULL | positive |
| `currency` | citext NOT NULL | |
| `state` | citext NOT NULL | see SM-2 |
| `initiated_at` | timestamptz NOT NULL | |
| `completed_at` | timestamptz NULL | |
| `gateway_ref` | text NULL | provider's refund id |
| `failure_reason` | text NULL | |
| `attempts` | int NOT NULL DEFAULT 0 | |
| `manual_iban` | text NULL | COD manual refund |
| `manual_confirmed_by_account_id` | uuid NULL | |
| `row_version` | bytea NOT NULL | |

Unique partial index on `(captured_transaction_id, request_id)` when `state in ('completed','in_progress')` to guard double refunds.

### 6. `returns.refund_lines`
| column | type | notes |
|---|---|---|
| `refund_id` | uuid FK | |
| `return_line_id` | uuid FK | |
| `qty` | int NOT NULL | |
| `unit_price_minor` | bigint NOT NULL | from original order line |
| `tax_rate_bp` | int NOT NULL | original rate (basis points) |
| `line_amount_minor` | bigint NOT NULL | qty × unit × (1 + rate) |

PK `(refund_id, return_line_id)`.

### 7. `returns.return_photos`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `return_request_id` | uuid FK | |
| `blob_key` | text NOT NULL | |
| `mime` | citext NOT NULL | |
| `size_bytes` | int NOT NULL | |
| `sha256` | text NOT NULL | |
| `uploaded_at` | timestamptz NOT NULL | |

### 8. `returns.return_policies`
| column | type | notes |
|---|---|---|
| `market_code` | citext PK | |
| `return_window_days` | int NOT NULL | |
| `auto_approve_under_days` | int NULL | null = never auto-approve |
| `restocking_fee_bp` | int NOT NULL DEFAULT 0 | basis points of subtotal |
| `shipping_refund_on_full_only` | bool NOT NULL DEFAULT true | |
| `updated_by_account_id` | uuid | |
| `updated_at` | timestamptz | |

Per-product zero-window override lives on `catalog.products` as `return_zero_window bool` (set when restricted sealed-pharma flag is on).

### 9. `returns.returns_outbox`
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `event_type` | citext NOT NULL | |
| `aggregate_id` | uuid NOT NULL | |
| `payload_json` | jsonb NOT NULL | |
| `committed_at` | timestamptz NOT NULL | |
| `dispatched_at` | timestamptz NULL | |

---

## State machines

### SM-1 · ReturnRequest
States: `pending_review`, `approved`, `approved_partial`, `rejected`, `received`, `inspected`, `refunded`, `refund_failed`.
| from | to | trigger |
|---|---|---|
| pending_review | approved | admin approve (all lines) |
| pending_review | approved_partial | admin approve (reduced) |
| pending_review | rejected | admin reject |
| approved / approved_partial | received | admin mark-received |
| received | inspected | inspection complete |
| inspected | refunded | refund success |
| inspected | refund_failed | gateway error |
| refund_failed | refunded | retry success |
| pending_review | refunded | admin force-refund (skip-physical) |

### SM-2 · Refund
States: `pending`, `in_progress`, `pending_manual_transfer`, `completed`, `failed`.
| from | to | trigger |
|---|---|---|
| pending | in_progress | gateway call sent |
| in_progress | completed | gateway success |
| in_progress | failed | gateway error |
| pending | pending_manual_transfer | COD or force manual |
| pending_manual_transfer | completed | admin confirm |
| failed | in_progress | retry |

### SM-3 · Inspection
States: `pending`, `in_progress`, `complete`.
| from | to | trigger |
|---|---|---|
| pending | in_progress | admin starts |
| in_progress | complete | all lines scored |

## Audit
All admin actions write to spec 003 `audit_log_entries` with before/after JSON. State transitions recorded in `returns_outbox` as events; a dedicated `return_state_transitions` view may be materialized in Phase 1.5 if needed.
