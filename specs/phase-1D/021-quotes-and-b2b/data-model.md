# Phase 1 — Data Model: Quotes and B2B (Spec 021)

**Date**: 2026-04-28
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)

---

## 1. ERD

```text
                                  ┌────────────────────────────────────────────────┐
                                  │  quote_market_schemas                          │
                                  │  ──────────────────────                        │
                                  │  market_code PK + version PK                   │
                                  │  effective_from / effective_to                 │
                                  │  validity_days int                             │
                                  │  rate_limit_per_customer_per_hour int          │
                                  │  rate_limit_per_company_per_hour int           │
                                  │  company_verification_required bool            │
                                  │  tax_preview_drift_threshold_pct numeric(5,2)  │
                                  │  sla_decision_business_days int                │
                                  │  sla_warning_business_days int                 │
                                  │  invitation_ttl_days int                       │
                                  │  holidays_list jsonb                           │
                                  └────────────────────────────────────────────────┘

┌─────────────────────────┐  *   ┌──────────────────────────┐  *   ┌─────────────────────────┐
│  companies              │ ◄────│  company_memberships     │─────►│  identity.customers     │
│  ─────────────          │      │  ───────────────────     │      │  (logical FK; spec 004) │
│  id PK                  │      │  id PK                   │      └─────────────────────────┘
│  name jsonb {en,ar}     │      │  company_id FK           │
│  tax_id text            │      │  user_id FK (logical)    │      ┌─────────────────────────┐
│  market_code            │      │  role enum               │      │  company_branches       │
│  primary_address        │      │  joined_at               │      │  ───────────────        │
│  billing_address        │      │  UNIQUE(company_id,      │      │  id PK                  │
│  approver_required bool │      │         user_id, role)   │      │  company_id FK          │
│  po_required bool       │      └──────────────────────────┘      │  name jsonb {en,ar}     │
│  unique_po_required bool│                                         │  address                │
│  invoice_billing_       │      ┌──────────────────────────┐      │  contact_phone          │
│      eligible bool      │      │  company_invitations     │      └─────────────────────────┘
│  state enum             │      │  ────────────────        │
│  created_at / updated_at│ 1   *│  id PK                   │
│  xmin                   │ ◄────│  company_id FK           │
└─────────────────────────┘      │  invited_by user_id      │
              │                  │  invited_email           │
              │                  │  target_role             │
              │                  │  token (opaque)          │
              │                  │  state enum              │
              │ 1                │  sent_at / expires_at    │
              ▼                  └──────────────────────────┘
┌──────────────────────────────────┐
│  quotes                          │       ┌──────────────────────────┐
│  ──────                          │       │  quote_versions          │
│  id PK                           │ 1   * │  ────────────            │
│  customer_id FK (logical)        │ ─────►│  id PK                   │
│  company_id FK nullable          │       │  quote_id FK             │
│  branch_id FK nullable           │       │  version_number int      │
│  market_code                     │       │  authored_by (admin id)  │
│  state enum                      │       │  published_at            │
│  requested_at                    │       │  line_items jsonb        │
│  current_version_id FK nullable  │       │  terms_text jsonb        │
│  expires_at nullable             │       │  terms_days int          │
│  decided_at / decided_by nullable│       │  validity_extends bool   │
│  terminal_at / terminal_reason   │       │  totals_summary jsonb    │
│  po_number text nullable         │       │  customer_revision_      │
│  invoice_billing bool            │       │      comment jsonb       │
│  customer_supplied_message jsonb │       │  approver_rejection_     │
│  internal_note text              │       │      note nullable text  │
│  approver_rejection_note nullable│       │  UNIQUE(quote_id,        │
│  originating_cart_snapshot jsonb │       │         version_number)  │
│  originating_product_id nullable │       └──────────────────────────┘
│  restriction_policy_snapshot     │                  │
│      jsonb                       │                  │ 1
│  schema_version int              │                  ▼
│  xmin                            │       ┌──────────────────────────┐
└──────────────────────────────────┘       │  quote_version_documents │
              │                            │  ────────────────────────│
              │                            │  id PK                   │
              │ 1                          │  quote_version_id FK     │
              ▼                            │  locale enum {en, ar}    │
┌──────────────────────────────────┐       │  storage_key text        │
│  quote_state_transitions         │       │  content_type text       │
│  ───────────────────────────     │       │  generated_at            │
│  id PK                           │       │  UNIQUE(version_id, loc) │
│  quote_id FK                     │       └──────────────────────────┘
│  prior_state / new_state         │
│  actor_kind enum                 │       ┌──────────────────────────┐
│  actor_id nullable (uuid)        │       │  repeat_order_templates  │
│  reason jsonb {en?, ar?}         │       │  ────────────────────    │
│  metadata jsonb                  │       │  id PK                   │
│  occurred_at                     │       │  source_quote_id FK      │
│  (append-only Postgres trigger)  │       │  company_id nullable FK  │
└──────────────────────────────────┘       │  user_id FK (logical)    │
                                           │  name jsonb {en?, ar?}   │
                                           │  created_at / created_by │
                                           │  UNIQUE partial          │
                                           │    (company_id, name)    │
                                           │    WHERE company_id NN   │
                                           │  UNIQUE partial          │
                                           │    (user_id, name)       │
                                           │    WHERE company_id NULL │
                                           └──────────────────────────┘
```

Reuses `audit_log_entries` (spec 003) — every state transition + every below-baseline override + every membership change writes via `IAuditEventPublisher`.

---

## 2. Tables

### 2.1 `companies`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | UUIDv7. |
| `name` | `jsonb` | NOT NULL | `{ "en": "...", "ar": "..." }` — both locales required. |
| `tax_id` | `text` | NOT NULL | Per-market format validated at creation. PII; never logged. |
| `market_code` | `text` | NOT NULL, CHECK in (`eg`, `ksa`) | |
| `primary_address` | `jsonb` | NOT NULL | Structured address. |
| `billing_address` | `jsonb` | nullable | Defaults to primary_address if NULL. |
| `approver_required` | `bool` | NOT NULL DEFAULT true | |
| `po_required` | `bool` | NOT NULL DEFAULT false | |
| `unique_po_required` | `bool` | NOT NULL DEFAULT false | |
| `invoice_billing_eligible` | `bool` | NOT NULL DEFAULT true | Toggle off if a company shouldn't receive Net-X terms (admin action). |
| `state` | `text` | NOT NULL, CHECK in (`active`, `pending-verification`, `suspended`, `closed`) | Default `active` per Clarifications Q2. |
| `created_at` / `updated_at` | `timestamptz` | NOT NULL | |
| `xmin` | system | mapped via `IsRowVersion()` | Optimistic concurrency. |

**Indexes**:
- UNIQUE `(market_code, tax_id)` — one company per (market, tax_id).
- `IX_companies_state` on `(state)` partial WHERE `state != 'closed'`.

### 2.2 `company_memberships`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `company_id` | `uuid` | NOT NULL, FK | |
| `user_id` | `uuid` | NOT NULL, FK (logical to spec 004) | |
| `role` | `text` | NOT NULL, CHECK in (`companies.admin`, `buyer`, `approver`) | |
| `joined_at` | `timestamptz` | NOT NULL | |

**Indexes**:
- UNIQUE `(company_id, user_id, role)`.
- `IX_company_memberships_user` on `(user_id)` for "list my companies".
- `IX_company_memberships_company_role` on `(company_id, role)` for "list approvers of this company".

**Invariants** (enforced at handler level + integration tests):
- A company MUST have at least one `companies.admin` at all times (FR-024).
- When `companies.approver_required=true`, a company MUST have ≥ 1 `approver` (FR-025).

### 2.3 `company_branches`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `company_id` | `uuid` | NOT NULL, FK |
| `name` | `jsonb` | NOT NULL `{ en, ar }` |
| `address` | `jsonb` | NOT NULL |
| `contact_phone` | `text` | nullable |

### 2.4 `company_invitations`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `company_id` | `uuid` | NOT NULL, FK |
| `invited_by` | `uuid` | NOT NULL, FK to identity (logical) |
| `invited_email` | `text` | NOT NULL — CITEXT-style normalized lower-case |
| `target_role` | `text` | NOT NULL, CHECK in (`companies.admin`, `buyer`, `approver`) |
| `token` | `text` | NOT NULL UNIQUE — opaque, 32-byte URL-safe random |
| `state` | `text` | NOT NULL, CHECK in (`pending`, `accepted`, `declined`, `expired`) DEFAULT `pending` |
| `sent_at` | `timestamptz` | NOT NULL |
| `expires_at` | `timestamptz` | NOT NULL — `sent_at + 14 days` from market schema |

**Indexes**:
- `IX_company_invitations_token` UNIQUE on `(token)`.
- `IX_company_invitations_state_expires` on `(state, expires_at)` partial WHERE `state='pending'` — for the expiry worker.
- UNIQUE partial `(company_id, invited_email, target_role) WHERE state='pending'` — one open invite per (company, email, role).

### 2.5 `quotes`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `customer_id` | `uuid` | NOT NULL, FK (logical) | |
| `company_id` | `uuid` | nullable, FK | NULL for individual-customer quotes (US2). |
| `branch_id` | `uuid` | nullable, FK | NULL = company HQ default; only meaningful when `company_id` is non-null. |
| `market_code` | `text` | NOT NULL | Captured at request; cross-market not allowed (FR-011). |
| `state` | `text` | NOT NULL, CHECK in 8 enum values | See §3 state machine. |
| `requested_at` | `timestamptz` | NOT NULL | |
| `current_version_id` | `uuid` | nullable, FK to `quote_versions.id` | Set on first publish. |
| `expires_at` | `timestamptz` | nullable | Set on first publish; recomputed when a revision sets `validity_extends=true`. |
| `decided_at` | `timestamptz` | nullable | Set on first state-out-of-`requested`/`drafted`/`revised`/`pending-approver`. |
| `decided_by` | `uuid` | nullable | NULL when system-driven (e.g. `expired`). |
| `terminal_at` | `timestamptz` | nullable | Mirrors `decided_at` for terminal states; helps `RetentionPurgeWorker` if added in future. |
| `terminal_reason` | `text` | nullable | Stable enum (matches a `QuoteReasonCode` value). |
| `po_number` | `text` | nullable | NULL until buyer supplies one (at request or at acceptance). |
| `invoice_billing` | `bool` | NOT NULL DEFAULT false | Set true when company-account + invoice_billing_eligible at request time. |
| `customer_supplied_message` | `jsonb` | nullable | `{ en?, ar? }` — at least one if provided. |
| `internal_note` | `text` | nullable | Operator-only. |
| `approver_rejection_note` | `text` | nullable | Most recent approver rejection reason; cleared on next admin revision publish. |
| `originating_cart_snapshot` | `jsonb` | nullable | Set when request originates from a cart (US1); array of `{sku, quantity, line_note}`. |
| `originating_product_id` | `uuid` | nullable | Set when request originates from a single product (US2). |
| `restriction_policy_snapshot` | `jsonb` | NOT NULL | Snapshot of `IProductRestrictionPolicy` per-line at request time (mirrors spec 020 pattern); enables Phase 2 `vendor_id` reservation. |
| `schema_version` | `int` | NOT NULL | FK to `quote_market_schemas (market_code, version)`. |
| `xmin` | system | `IsRowVersion()` | Multi-approver finalize-race guard. |

**Indexes**:
- `IX_quotes_customer_state` on `(customer_id, state)` — "list my quotes".
- `IX_quotes_company_state_market` on `(company_id, state, market_code)` partial WHERE `company_id IS NOT NULL`.
- `IX_quotes_state_market_requested` on `(state, market_code, requested_at)` partial WHERE `state IN ('requested','drafted','revised','pending-approver')` — admin queue oldest-first.
- `IX_quotes_expires_at` on `(expires_at)` partial WHERE `state IN ('revised','pending-approver')` — expiry worker scan.
- UNIQUE partial `(company_id, po_number) WHERE company_id IS NOT NULL AND po_number IS NOT NULL` — only enforced on companies with `unique_po_required=true` (the validator checks the flag and inserts; the unique partial index is the deterministic fallback that catches races).

### 2.6 `quote_versions`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `quote_id` | `uuid` | NOT NULL, FK |
| `version_number` | `int` | NOT NULL — monotonic per quote |
| `authored_by` | `uuid` | NOT NULL, FK (admin user-id, logical) |
| `published_at` | `timestamptz` | NOT NULL |
| `line_items` | `jsonb` | NOT NULL | Array of `{ sku, qty, baseline_unit_price, override_unit_price, override_reason: { en?, ar? }?, line_discount_amount, line_tax_preview, currency }`. |
| `terms_text` | `jsonb` | NOT NULL | `{ en, ar }` — both required. |
| `terms_days` | `int` | NOT NULL DEFAULT 0 | Net-X term in days; 0 = "due on receipt". |
| `validity_extends` | `bool` | NOT NULL DEFAULT false | True if this revision extended validity per Clarifications Q5. |
| `totals_summary` | `jsonb` | NOT NULL | `{ subtotal, total_discount, total_tax_preview, grand_total, currency }`. |
| `customer_revision_comment` | `jsonb` | nullable | `{ en?, ar? }` — set when this version was authored in response to a customer revision request. |

**Constraint**: UNIQUE `(quote_id, version_number)`.

**Append-only at the row level**: an `IEntityTypeConfiguration` rule disallows EF Core UPDATEs on this table; tests verify.

### 2.7 `quote_version_documents`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `quote_version_id` | `uuid` | NOT NULL, FK |
| `locale` | `text` | NOT NULL, CHECK in (`en`, `ar`) |
| `storage_key` | `text` | NOT NULL — opaque to `IStorageService` |
| `content_type` | `text` | NOT NULL DEFAULT `application/pdf` |
| `generated_at` | `timestamptz` | NOT NULL |

**Constraint**: UNIQUE `(quote_version_id, locale)` — one EN + one AR per version.

### 2.8 `quote_state_transitions`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `quote_id` | `uuid` | NOT NULL, FK |
| `prior_state` | `text` | NOT NULL — uses literal `__none__` for the initial insert |
| `new_state` | `text` | NOT NULL |
| `actor_kind` | `text` | NOT NULL, CHECK in (`customer`, `buyer`, `approver`, `admin_operator`, `system`) |
| `actor_id` | `uuid` | nullable |
| `reason` | `jsonb` | nullable | `{ en?, ar? }` — applies for actions that carry a reason (revisions, approver rejection); NULL for system transitions. |
| `metadata` | `jsonb` | NOT NULL DEFAULT `'{}'` | Holds e.g. `{ idempotency_key, version_number, drift_pct, po_warning_acknowledged }`. |
| `occurred_at` | `timestamptz` | NOT NULL |

**Indexes**: `IX_quote_state_transitions_quote_occurred` on `(quote_id, occurred_at)`.

**Append-only**: Postgres `BEFORE UPDATE OR DELETE ... RAISE EXCEPTION` trigger.

### 2.9 `quote_market_schemas`

| Column | Type | Constraints |
|---|---|---|
| `market_code` | `text` | PK part 1, CHECK in (`eg`, `ksa`) |
| `version` | `int` | PK part 2 |
| `effective_from` | `timestamptz` | NOT NULL |
| `effective_to` | `timestamptz` | nullable — NULL = currently active |
| `validity_days` | `int` | NOT NULL DEFAULT 14, CHECK > 0 |
| `rate_limit_per_customer_per_hour` | `int` | NOT NULL DEFAULT 10 |
| `rate_limit_per_company_per_hour` | `int` | NOT NULL DEFAULT 50 |
| `company_verification_required` | `bool` | NOT NULL DEFAULT false (Clarifications Q2) |
| `tax_preview_drift_threshold_pct` | `numeric(5,2)` | NOT NULL DEFAULT 5.00 |
| `sla_decision_business_days` | `int` | NOT NULL DEFAULT 2 |
| `sla_warning_business_days` | `int` | NOT NULL DEFAULT 1 |
| `invitation_ttl_days` | `int` | NOT NULL DEFAULT 14 |
| `holidays_list` | `jsonb` | NOT NULL DEFAULT `[]` |

**Constraint**: at most one row per `market_code` may have `effective_to IS NULL` — partial unique index.

### 2.10 `repeat_order_templates`

| Column | Type | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `source_quote_id` | `uuid` | NOT NULL, FK |
| `company_id` | `uuid` | nullable, FK |
| `user_id` | `uuid` | NOT NULL, FK (logical) |
| `name` | `jsonb` | NOT NULL — `{ en?, ar? }`, at least one |
| `created_at` | `timestamptz` | NOT NULL |
| `created_by` | `uuid` | NOT NULL, FK (logical) |

**Indexes**: see [research.md §R12](./research.md) for the two unique partial indexes.

---

## 3. State machines

### 3.1 `Quote.state`

#### States

| State | Terminal? | Notes |
|---|---|---|
| `requested` | No | Customer-submitted; awaiting admin pickup. |
| `drafted` | No | Admin operator authoring a version that has NOT been published yet. Customer-invisible. |
| `revised` | No | At least one version published; customer-visible. Default state between revisions. |
| `pending-approver` | No | Buyer has submitted acceptance; approvers can finalize or reject. |
| `accepted` | Yes | Order created. |
| `rejected` | Yes | Customer-rejected. No cool-down (R10). |
| `expired` | Yes | Past `expires_at`. |
| `withdrawn` | Yes | Customer withdrew or system voided (account-lifecycle). |

#### Transitions

| From | To | Trigger | Actor | Guard |
|---|---|---|---|---|
| `__none__` | `requested` | Customer submits (cart or product) | customer | Rate limit ≤ caps; non-empty cart or known product; no other non-terminal quote for this customer (or company-bounded — revisit if needed) |
| `requested` | `drafted` | Admin opens to author | admin_operator | Has `quotes.author` + market scope |
| `drafted` | `revised` | Admin publishes | admin_operator | Reason / terms / lines validated; PDF generation succeeds |
| `revised` | `drafted` | Customer requests revision | customer / buyer | Localized comment provided |
| `revised` | `drafted` | Admin opens to revise (proactive) | admin_operator | — |
| `revised` | `pending-approver` | Buyer submits acceptance (`approver_required=true` AND ≥ 1 approver) | buyer | Quote not expired; eligibility check passes per FR-036; PO uniqueness check (FR-019); idempotency-key |
| `revised` | `accepted` | Buyer submits acceptance (`approver_required=false` OR no approver) | buyer / customer | Same guards as above + conversion succeeds |
| `pending-approver` | `accepted` | Approver finalizes | approver | xmin guard (first-action-wins); conversion succeeds |
| `pending-approver` | `revised` | Approver rejects with comment | approver | Reason `{ en?, ar? }` provided |
| `pending-approver` | `revised` | Only-approver leaves the company while quote pending | system | Triggered by AccountLifecycleHandler |
| any non-terminal | `rejected` | Customer rejects published quote | customer / buyer | — |
| any non-terminal | `withdrawn` | Customer withdraws | customer / buyer | — |
| any non-terminal | `withdrawn` | System voids on account-locked / deleted / market-changed | system | — |
| any non-terminal | `expired` | Worker | system | `expires_at <= now` |

**Forbidden** (rejected at the state-machine guard):
- Any terminal → non-terminal.
- `requested` → `accepted` directly (must pass through publication).
- `drafted` → `pending-approver` directly (drafted is operator-invisible; customers cannot interact with it).

### 3.2 `CompanyInvitation.state`

| State | Terminal? | Notes |
|---|---|---|
| `pending` | No | Awaiting invitee response. |
| `accepted` | Yes | Invitee created the membership. |
| `declined` | Yes | Invitee explicitly declined. |
| `expired` | Yes | Past `expires_at`. |

Transitions:
- `__none__ → pending`: company admin invites.
- `pending → accepted`: invitee accepts.
- `pending → declined`: invitee declines.
- `pending → expired`: worker, `expires_at <= now`.
- `pending → expired`: company admin revokes (treated as expired, with audit metadata `revoked_by`).

---

## 4. Reason codes

`QuoteReasonCode` enum (full set, V1) — see [research.md §R8](./research.md) for the complete inventory and ICU-key wiring expectations.

---

## 5. Audit-event schema (writes to `audit_log_entries` via `IAuditEventPublisher`)

| `event_kind` | Source | Payload (`event_data` jsonb) |
|---|---|---|
| `quote.state_changed` | every state transition | `{ quote_id, prior_state, new_state, actor_kind, actor_id, reason: {en?,ar?}?, market_code, schema_version, version_number?, idempotency_key? }` |
| `quote.line_override` | admin authoring with below-baseline price | `{ quote_id, version_number, sku, baseline_unit_price, override_unit_price, override_reason: {en?,ar?}, authored_by }` |
| `quote.po_warning_acknowledged` | buyer confirmed reused PO when `unique_po_required=false` | `{ quote_id, po_number, prior_quote_ids[] }` |
| `quote.tax_preview_drift_acknowledged` | buyer/approver confirmed conversion despite drift | `{ quote_id, version_number, preview_pct, authoritative_pct, drift_pct }` |
| `quote.document_generated` | PDF generated on publish | `{ quote_version_id, locale, storage_key, generated_at }` |
| `company.member_changed` | invite accepted / member removed / role changed | `{ company_id, user_id, action, prior_role?, new_role? }` |
| `company.invitation_sent` | invite created | `{ company_id, invitation_id, invited_email, target_role, invited_by }` |
| `company.invitation_terminal` | invitation accept/decline/expire | `{ company_id, invitation_id, terminal_state, terminal_at }` |
| `company.config_changed` | company admin edits config | `{ company_id, changed_fields: [...], actor_user_id }` |
| `company.suspended` | admin (spec 019) suspends company | `{ company_id, suspended_by, reason }` |

All events carry the platform-wide audit envelope (correlation-id, request-id, occurred-at) per spec 003.

---

## 6. Domain events (in-process bus → consumed by spec 025)

`Modules/Shared/QuoteDomainEvents.cs`:

```csharp
public sealed record QuoteRequested(Guid QuoteId, Guid CustomerId, Guid? CompanyId, string MarketCode, string LocaleHint);
public sealed record QuotePublished(Guid QuoteId, Guid QuoteVersionId, int VersionNumber, Guid CustomerId, Guid? CompanyId, string MarketCode, string LocaleHint, IReadOnlyDictionary<string,string> PdfStorageKeysByLocale);
public sealed record QuotePendingApprover(Guid QuoteId, Guid CompanyId, IReadOnlyCollection<Guid> ApproverUserIds, Guid BuyerUserId, string MarketCode);
public sealed record QuoteAccepted(Guid QuoteId, Guid OrderId, Guid CustomerId, Guid? CompanyId, string MarketCode, string LocaleHint);
public sealed record QuoteRejected(Guid QuoteId, Guid CustomerId, Guid? CompanyId, string MarketCode, string LocaleHint);
public sealed record QuoteApproverRejected(Guid QuoteId, Guid BuyerUserId, Guid RejectingApproverUserId, string MarketCode);
public sealed record QuoteExpired(Guid QuoteId, Guid CustomerId, Guid? CompanyId, string MarketCode, string LocaleHint);
public sealed record QuoteWithdrawn(Guid QuoteId, Guid CustomerId, Guid? CompanyId, string Reason, string MarketCode);
```

`Modules/Shared/CompanyInvitationDomainEvents.cs`:

```csharp
public sealed record CompanyInvitationSent(Guid InvitationId, Guid CompanyId, string InvitedEmail, string TargetRole, string LocaleHint);
public sealed record CompanyInvitationAccepted(Guid InvitationId, Guid CompanyId, Guid InviteeUserId, string TargetRole);
public sealed record CompanyInvitationDeclined(Guid InvitationId, Guid CompanyId, string InvitedEmail);
public sealed record CompanyInvitationExpired(Guid InvitationId, Guid CompanyId, string InvitedEmail);
```

Spec 025 subscribes via `IDomainEventHandler<T>`. Quote/Invitation state writes never block on spec 025 delivery (FR-043).

---

## 7. Cross-module contracts (live in `Modules/Shared/`)

Defined by **this** spec, implemented elsewhere:
- `IOrderFromQuoteHandler` — implemented by spec 011 — see [contracts/quotes-and-b2b-contract.md §4](./contracts/quotes-and-b2b-contract.md).
- `IPricingBaselineProvider` — implemented by spec 007-a.
- `ICartSnapshotProvider` — implemented by spec 009.

Defined elsewhere, consumed here:
- `ICustomerVerificationEligibilityQuery` — declared by spec 020; consumed at acceptance (FR-036).
- `ICustomerAccountLifecycleSubscriber` — declared by spec 020; this spec subscribes (R13).
