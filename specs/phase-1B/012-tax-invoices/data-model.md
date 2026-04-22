# Data Model — Tax Invoices v1 (Spec 012)

**Date**: 2026-04-22 · Schema: `invoices`.

## Tables (7)

### 1. `invoices.invoices`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `invoice_number` | text UNIQUE NOT NULL | `INV-KSA-202604-000187` |
| `order_id` | uuid FK → orders.orders | |
| `account_id` | uuid FK → identity.accounts | |
| `market_code` | citext NOT NULL | |
| `currency` | citext NOT NULL | |
| `issued_at` | timestamptz NOT NULL | |
| `price_explanation_id` | uuid FK → pricing.price_explanations | tax source of truth |
| `subtotal_minor` / `discount_minor` / `tax_minor` / `shipping_minor` / `grand_total_minor` | bigint | |
| `bill_to_json` | jsonb NOT NULL | snapshot of billing party (B2C or B2B) |
| `seller_json` | jsonb NOT NULL | snapshot of seller legal entity + VAT number |
| `b2b_po_number` | text NULL | |
| `pdf_blob_key` | text NULL | set after render |
| `pdf_sha256` | text NULL | byte-identity verification |
| `xml_blob_key` | text NULL | reserved for Phase 2 ZATCA clearance |
| `zatca_qr_b64` | text NULL | KSA only |
| `state` | citext NOT NULL | `pending`\|`rendered`\|`delivered`\|`failed` |
| `render_attempts` | int NOT NULL DEFAULT 0 | |
| `last_error` | text NULL | |
| `row_version` | bytea NOT NULL | |

Indexes: `(order_id)`, `(market_code, issued_at)`, `(account_id, issued_at DESC)`.

### 2. `invoices.invoice_lines`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `invoice_id` | uuid FK → invoices | |
| `order_line_id` | uuid FK → orders.order_lines | |
| `sku` | citext NOT NULL | |
| `name_ar` / `name_en` | text | snapshot |
| `qty` | int NOT NULL | |
| `unit_price_minor` / `line_discount_minor` / `line_tax_minor` / `line_total_minor` | bigint | |
| `tax_rate_bp` | int NOT NULL | basis points; e.g. 1500 = 15 % |

### 3. `invoices.credit_notes`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `credit_note_number` | text UNIQUE NOT NULL | `CN-KSA-202604-000023` |
| `invoice_id` | uuid FK → invoices | |
| `refund_id` | uuid NULL | spec 013 reference |
| `issued_at` | timestamptz NOT NULL | |
| `subtotal_minor` / `discount_minor` / `tax_minor` / `shipping_minor` / `grand_total_minor` | bigint | negative totals (or positive w/ `kind=refund`) |
| `reason_code` | citext NOT NULL | |
| `pdf_blob_key` / `pdf_sha256` | | |
| `zatca_qr_b64` | text NULL | KSA only |
| `state` | citext NOT NULL | same as invoices |

### 4. `invoices.credit_note_lines`
Same columns as `invoice_lines`, plus `invoice_line_id` FK → invoice_lines (the original line it refunds).

### 5. `invoices.invoice_render_jobs`
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `invoice_id` | uuid NULL | |
| `credit_note_id` | uuid NULL | |
| `state` | citext NOT NULL | `queued`\|`rendering`\|`done`\|`failed` |
| `attempts` | int NOT NULL DEFAULT 0 | |
| `next_attempt_at` | timestamptz NOT NULL | |
| `last_error` | text NULL | |
| `created_at` | timestamptz NOT NULL | |

Partial index `WHERE state IN ('queued','failed')`.

### 6. `invoices.invoice_templates`
| column | type | notes |
|---|---|---|
| `market_code` | citext PK | |
| `seller_legal_name_ar` / `_en` | text NOT NULL | |
| `seller_vat_number` | text NOT NULL | |
| `seller_address_ar` / `_en` | text NOT NULL | |
| `bank_details_json` | jsonb NOT NULL | IBAN / bank name / account holder |
| `footer_html_ar` / `_en` | text | |
| `updated_by_account_id` | uuid | audit |
| `updated_at` | timestamptz | |

### 7. `invoices.invoices_outbox`
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `event_type` | citext NOT NULL | `invoice.issued`, `invoice.regenerated`, `credit_note.issued` |
| `aggregate_id` | uuid NOT NULL | |
| `payload_json` | jsonb NOT NULL | |
| `committed_at` | timestamptz NOT NULL | |
| `dispatched_at` | timestamptz NULL | |

---

## State machine — Invoice
States: `pending` → `rendered` → `delivered`; or → `failed`.
| from | to | trigger |
|---|---|---|
| pending | rendered | render worker success |
| rendered | delivered | notification dispatched (spec 019) |
| pending | failed | max attempts reached |
| failed | rendered | admin retry |

## Audit
Every admin action (resend, force-regenerate) writes to spec 003 `audit_log_entries`.
