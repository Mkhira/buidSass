# Phase 1 — Data Model: Professional Verification (Spec 020)

**Date**: 2026-04-28
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)
**Research**: [research.md](./research.md)

---

## 1. ERD

```text
                                  ┌─────────────────────────────────────────────┐
                                  │  verification_market_schemas                │
                                  │  ─────────────────────────                  │
              ┌──snapshots────────│  market_code  PK + version PK               │
              │                   │  effective_from / effective_to              │
              │                   │  required_fields (jsonb)                    │
              │                   │  allowed_document_types (jsonb)             │
              │                   │  retention_months / cooldown_days /         │
              │                   │  expiry_days / reminder_windows_days /      │
              │                   │  sla_decision_business_days /               │
              │                   │  sla_warning_business_days /                │
              │                   │  holidays_list (jsonb)                      │
              │                   └─────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────┐  1   ┌────────────────────────────────────┐
│  verifications                  │ ◄────│  verification_documents             │
│  ────────────────               │      │  ──────────────────────             │
│  id PK                          │      │  id PK                              │
│  customer_id FK → identity      │      │  verification_id FK                 │
│  market_code                    │      │  storage_key (nullable after purge) │
│  schema_version (FK to mkt sch) │      │  content_type                       │
│  profession                     │      │  size_bytes                         │
│  regulator_identifier           │      │  scan_status                        │
│  state (enum, RowVersion xmin)  │      │  uploaded_at                        │
│  submitted_at                   │      │  purge_after (nullable)             │
│  decided_at (nullable)          │      │  purged_at (nullable)               │
│  decided_by (nullable)          │      └────────────────────────────────────┘
│  expires_at (nullable)          │
│  supersedes_id (FK self, null)  │      ┌────────────────────────────────────┐
│  superseded_by_id (FK self,null)│ 1    │  verification_state_transitions    │
│  void_reason (nullable text)    │ ◄────│  ──────────────────────────────    │
│  restriction_policy_snapshot    │      │  id PK                             │
│      (jsonb)                    │      │  verification_id FK                │
│  created_at / updated_at        │      │  prior_state / new_state           │
│  xmin (system column,           │      │  actor_kind (enum)                 │
│       mapped IsRowVersion)      │      │  actor_id (nullable)               │
└─────────────────────────────────┘      │  reason (text, NOT NULL)           │
              │                          │  metadata (jsonb)                  │
              │ 1                        │  occurred_at                       │
              │                          └────────────────────────────────────┘
              ▼
┌─────────────────────────────────┐
│  verification_reminders         │
│  ────────────────────────       │
│  id PK                          │      ┌────────────────────────────────────┐
│  verification_id FK             │      │  verification_eligibility_cache    │
│  window_days int                │      │  ──────────────────────────        │
│  emitted_at                     │      │  customer_id PK                    │
│  skipped bool                   │      │  market_code                       │
│  skip_reason (nullable)         │      │  eligibility_class (enum)          │
│  UNIQUE(verification_id,        │      │  expires_at (nullable)             │
│         window_days)            │      │  computed_at                       │
└─────────────────────────────────┘      └────────────────────────────────────┘
```

Reuses `audit_log_entries` (spec 003) — every state transition + every PII read writes to it via `IAuditEventPublisher`. Not a new table here.

---

## 2. Tables

### 2.1 `verifications`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | New v7 UUID per row. |
| `customer_id` | `uuid` | NOT NULL, FK → `identity.customer_accounts.id` (logical; cross-module via Shared) | One customer can have multiple verifications across time. |
| `market_code` | `text` | NOT NULL, CHECK (`market_code IN ('eg','ksa')`) | Market-of-record at submission. |
| `schema_version` | `int` | NOT NULL, FK → `verification_market_schemas (market_code, version)` | Snapshots which schema applied (FR-026). |
| `profession` | `text` | NOT NULL | e.g. `dentist`, `dental_lab_tech`, `dental_student`, `clinic_buyer`. |
| `regulator_identifier` | `text` | NOT NULL | e.g. SCFHS license number, EG syndicate registration. PII; never logged. |
| `state` | `text` | NOT NULL, CHECK (state IN enum below) | See §3 state machine. |
| `submitted_at` | `timestamptz` | NOT NULL | Immutable. |
| `decided_at` | `timestamptz` | nullable | Set on first transition out of `submitted`/`in-review`/`info-requested`. |
| `decided_by` | `uuid` | nullable, FK → `identity.admin_accounts.id` (logical) | NULL when system-driven (`expired`, `superseded`, `void`). |
| `expires_at` | `timestamptz` | nullable | Set on `approved`. Recomputed on renewal `approved`. |
| `supersedes_id` | `uuid` | nullable, FK → self | Renewal back-pointer (FR-020). |
| `superseded_by_id` | `uuid` | nullable, FK → self | Forward-pointer once a renewal is approved. |
| `void_reason` | `text` | nullable | Free text for `void` transitions (e.g., `account_inactive`, `account_deleted`, `customer_market_changed`). |
| `restriction_policy_snapshot` | `jsonb` | NOT NULL | The `IProductRestrictionPolicy` view captured at submission for replay (FR-026). |
| `created_at` | `timestamptz` | NOT NULL | DEFAULT `now()`. |
| `updated_at` | `timestamptz` | NOT NULL | Updated on every state change. |
| `xmin` | system | mapped via `IsRowVersion()` | Optimistic concurrency token (R4). |

**Indexes**:
- `IX_verifications_customer_state_market` on `(customer_id, state, market_code)` — eligibility cache rebuild + customer "list my verifications".
- `IX_verifications_state_market_submitted` on `(state, market_code, submitted_at)` partial WHERE `state IN ('submitted','in-review','info-requested')` — admin queue oldest-first.
- `IX_verifications_expires_at` on `(expires_at)` partial WHERE `state='approved'` — expiry worker scan.
- `IX_verifications_supersedes` on `(supersedes_id)` — renewal lookups.

### 2.2 `verification_documents`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `verification_id` | `uuid` | NOT NULL, FK → `verifications.id` ON DELETE RESTRICT | Documents survive even if parent transitions; audit linkage. |
| `storage_key` | `text` | nullable | NULL once purged; row remains. |
| `content_type` | `text` | NOT NULL, CHECK in allowlist (`application/pdf`, `image/jpeg`, `image/png`, `image/heic`) | |
| `size_bytes` | `bigint` | NOT NULL, CHECK ≤ 10485760 (10 MB) | |
| `scan_status` | `text` | NOT NULL, CHECK in (`pending`, `clean`, `infected`, `error`) | Submission can only be created when all docs `clean`. |
| `uploaded_at` | `timestamptz` | NOT NULL | |
| `purge_after` | `timestamptz` | nullable | Set when parent enters terminal state: `terminal_at + market.retention_months`. |
| `purged_at` | `timestamptz` | nullable | Set by `VerificationDocumentPurgeWorker`. |

**Indexes**:
- `IX_verification_documents_verification` on `(verification_id)`.
- `IX_verification_documents_purge_after` on `(purge_after)` partial WHERE `purged_at IS NULL AND purge_after IS NOT NULL`.

**Validation**: aggregate per-submission size ≤ 25 MB and per-submission count ≤ 5 enforced in handler before INSERT (FR-006).

### 2.3 `verification_state_transitions`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `verification_id` | `uuid` | NOT NULL, FK | |
| `prior_state` | `text` | NOT NULL | Use literal `__none__` for the initial submission insert. |
| `new_state` | `text` | NOT NULL | |
| `actor_kind` | `text` | NOT NULL, CHECK in (`customer`, `reviewer`, `system`) | |
| `actor_id` | `uuid` | nullable | NULL for `system`. |
| `reason` | `text` | NOT NULL | Reviewer-entered reason (FR-014) or system reason code. |
| `metadata` | `jsonb` | NOT NULL DEFAULT `'{}'` | Holds e.g. `{ supersedes_id: ..., document_ids: [...], reminder_window_days: 7, idempotency_key: ... }`. |
| `occurred_at` | `timestamptz` | NOT NULL | |

**Indexes**: `IX_verification_state_transitions_verification_occurred` on `(verification_id, occurred_at)`.

**Append-only**: no UPDATE/DELETE allowed; enforced via Postgres trigger (`BEFORE UPDATE OR DELETE ... RAISE EXCEPTION`).

### 2.4 `verification_market_schemas`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `market_code` | `text` | PK part 1, CHECK in (`eg`, `ksa`) | |
| `version` | `int` | PK part 2 | Monotonic per market. |
| `effective_from` | `timestamptz` | NOT NULL | |
| `effective_to` | `timestamptz` | nullable | NULL = currently active. |
| `required_fields` | `jsonb` | NOT NULL | Schema definition (e.g. `[{"name":"profession","type":"enum","values":[...]}, {"name":"regulator_identifier","type":"text","pattern":"^[0-9]{7,12}$"}]`). |
| `allowed_document_types` | `jsonb` | NOT NULL DEFAULT `["application/pdf","image/jpeg","image/png","image/heic"]` | |
| `retention_months` | `int` | NOT NULL, CHECK ≥ 0 | KSA seed = 24, EG seed = 36. |
| `cooldown_days` | `int` | NOT NULL, CHECK ≥ 0 | Default 7. |
| `expiry_days` | `int` | NOT NULL, CHECK > 0 | Default 365. |
| `reminder_windows_days` | `jsonb` | NOT NULL DEFAULT `[30,14,7,1]` | Descending. |
| `sla_decision_business_days` | `int` | NOT NULL DEFAULT 2 | |
| `sla_warning_business_days` | `int` | NOT NULL DEFAULT 1 | |
| `holidays_list` | `jsonb` | NOT NULL DEFAULT `[]` | List of ISO dates (YYYY-MM-DD). |

**Constraint**: at most one row per `market_code` may have `effective_to IS NULL` — enforced via unique partial index `(market_code) WHERE effective_to IS NULL`.

**Update model**: schemas are versioned, never mutated. A change is `INSERT new version + UPDATE old version SET effective_to = now()` in one Tx.

### 2.5 `verification_reminders`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `verification_id` | `uuid` | NOT NULL, FK | |
| `window_days` | `int` | NOT NULL, CHECK > 0 | Must match an entry in the verification's snapshotted `reminder_windows_days`. |
| `emitted_at` | `timestamptz` | NOT NULL | |
| `skipped` | `bool` | NOT NULL DEFAULT false | True for back-window skip-with-audit-note (R5). |
| `skip_reason` | `text` | nullable | Required when `skipped = true`. |

**Constraint**: `UNIQUE (verification_id, window_days)` — the dedup invariant (FR-019, R5).

### 2.6 `verification_eligibility_cache`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `customer_id` | `uuid` | PK | One row per customer. |
| `market_code` | `text` | NOT NULL | Customer's current market-of-record at last refresh. |
| `eligibility_class` | `text` | NOT NULL, CHECK in (`eligible`, `ineligible`, `unrestricted_only`) | |
| `expires_at` | `timestamptz` | nullable | Mirrors the active approved verification's `expires_at` if `eligibility_class='eligible'`. |
| `reason_code` | `text` | nullable | One of `EligibilityReasonCode` enum values; null when `eligibility_class='eligible'`. |
| `professions` | `jsonb` | NOT NULL DEFAULT `[]` | The set of approved professions for this customer (`["dentist"]` etc.). Empty when ineligible. |
| `computed_at` | `timestamptz` | NOT NULL | |

**Indexes**: `IX_verification_eligibility_cache_market_class` on `(market_code, eligibility_class)`.

**Write path**: every state transition's command invokes `EligibilityCacheInvalidator.RebuildAsync(customerId)` inside the Tx; read-side joins this single row with `IProductRestrictionPolicy.GetForSku(sku)`.

---

## 3. State machine: `Verification.state`

### 3.1 States

| State | Terminal? | Notes |
|---|---|---|
| `submitted` | No | Created by customer; awaiting reviewer pickup. |
| `in-review` | No | A reviewer opened it (transitions on reviewer action only — opening the detail view does **not** flip state; `in-review` is reached when the reviewer clicks "begin review" or implicitly on first decision attempt). |
| `info-requested` | No | Reviewer needs more info from customer. SLA timer paused. |
| `approved` | No (active state, not terminal) | Carries `expires_at`. Eligible for restricted purchases per profession + market. |
| `rejected` | Yes | Customer cool-down before next submission allowed. |
| `expired` | Yes | Set by `VerificationExpiryWorker` once `expires_at <= now`. |
| `revoked` | Yes | Set by reviewer with `verification.revoke` permission. No cool-down. |
| `superseded` | Yes (derived) | Set when a renewal is approved; back-pointer in `superseded_by_id`. |
| `void` | Yes | Set by system on customer-account lifecycle changes (locked / deleted / market-change). |

### 3.2 Transitions

| From | To | Trigger | Actor | Guard |
|---|---|---|---|---|
| `__none__` | `submitted` | Customer submits | customer | Required-fields satisfied; documents all `scan_status='clean'`; no other non-terminal verification exists for the customer (or this is a renewal — see below). |
| `submitted` | `in-review` | Reviewer begins review | reviewer | Has `verification.review` + market scope. |
| `in-review` | `approved` | Reviewer approves | reviewer | Reason non-empty; xmin guard. |
| `in-review` | `rejected` | Reviewer rejects | reviewer | Reason non-empty; xmin guard. |
| `in-review` | `info-requested` | Reviewer requests info | reviewer | Reason non-empty; xmin guard. |
| `submitted` | `approved` / `rejected` / `info-requested` | Reviewer skips explicit "begin review" | reviewer | Same as in-review transitions (combined). |
| `info-requested` | `in-review` | Customer resubmits | customer | At least one new document or modified field; original `submitted_at` preserved. |
| `approved` | `expired` | Worker | system | `expires_at <= now`. |
| `approved` | `revoked` | Reviewer revokes | reviewer (revoke perm) | Reason non-empty. |
| `approved` | `superseded` | Renewal approved | system | `superseded_by_id` set in same Tx. |
| any non-terminal | `void` | Account-lifecycle event | system | One of `account_inactive`, `account_deleted`, `customer_market_changed`. |
| approved | `void` | Account-lifecycle event | system | Same as above. |

**Forbidden** (rejected at the state-machine guard):
- Any terminal → non-terminal.
- Any state → `submitted` (a new row is required for re-submission after rejection).
- `info-requested` → `approved` / `rejected` / `revoked` directly (must pass through `in-review`; matches reviewer UX of re-opening the case).

**Renewal exception**: when the customer has an active `approved` verification, they MAY create a second (renewal) verification with `supersedes_id` pointing at the prior. The renewal goes through the normal flow. While the renewal is non-terminal, the prior approval remains `approved` (FR-010). On renewal `approved`, the prior is transitioned to `superseded` in the same Tx (FR-020).

### 3.3 Transition idempotency

Every state-transitioning command requires an `Idempotency-Key` request header. The platform middleware (per spec 003) returns the original 200 response for replays within 24 h. The xmin row-version check additionally guards against two concurrent commands with different idempotency keys (R4).

---

## 4. Eligibility reason codes (Enum surface)

| Code | When | Customer-visible? |
|---|---|---|
| `Eligible` | Customer + market + profession satisfy the SKU's restriction policy and approval is non-expired. | yes |
| `Unrestricted` | Product is not restricted in the customer's market. | no (silent path) |
| `VerificationRequired` | No verification of any kind exists. | yes — "Verify your professional credentials to purchase this product." |
| `VerificationPending` | Verification is in `submitted` or `in-review`. | yes — "Your verification is under review." |
| `VerificationInfoRequested` | Verification is in `info-requested`. | yes — "We need more information to complete your verification." |
| `VerificationRejected` | Verification is `rejected`; cool-down clock visible. | yes — "Your verification was not approved. You may resubmit on {date}." |
| `VerificationExpired` | Verification is `expired`. | yes — "Your verification expired on {date}. Renew to continue." |
| `VerificationRevoked` | Verification is `revoked`. | yes — "Your verification has been revoked. {reason}" |
| `ProfessionMismatch` | Approved verification's profession does not match the SKU's required profession. | yes — "This product is restricted to {required_profession}." |
| `MarketMismatch` | Customer's current market differs from any approved verification's market. | yes — "Your verification was issued for {old_market}. Please verify for {new_market}." |
| `AccountInactive` | Customer account locked / deleted. | no (account flow handles it) |

Each code maps to one ICU key in `verification.en.icu` / `verification.ar.icu`. Codes are `[OpenApiEnum]`-emitted into `openapi.verification.json` for type-safe consumption by spec 014 (Flutter customer app).

---

## 5. Audit-event schema (writes to `audit_log_entries` via `IAuditEventPublisher`)

| `event_kind` | Source | Payload (`event_data` jsonb) |
|---|---|---|
| `verification.state_changed` | every state transition | `{ verification_id, prior_state, new_state, actor_kind, actor_id, reason, market_code, schema_version, supersedes_id?, expires_at? }` |
| `verification.pii_access` | every PII read | `{ verification_id, document_id?, kind ∈ ['LicenseNumberRead','DocumentBodyRead','DocumentMetadataRead'], surface ∈ ['admin_review','admin_customers','admin_support'] }` |
| `verification.reminder_emitted` | reminder worker | `{ verification_id, window_days, skipped, skip_reason? }` |
| `verification.document_purged` | purge worker | `{ verification_id, document_id, purged_at }` |
| `verification.market_schema_changed` | seeder / admin | `{ market_code, prior_version, new_version, effective_from }` |

All events carry the platform-wide audit envelope (correlation-id, request-id, occurred-at) per spec 003.

---

## 6. Domain events (in-process bus → consumed by spec 025)

`Modules/Shared/VerificationDomainEvents.cs`:

```csharp
public sealed record VerificationApproved(Guid VerificationId, Guid CustomerId, string MarketCode, string LocaleHint);
public sealed record VerificationRejected(Guid VerificationId, Guid CustomerId, string MarketCode, string Reason, string LocaleHint);
public sealed record VerificationInfoRequested(Guid VerificationId, Guid CustomerId, string MarketCode, string Reason, string LocaleHint);
public sealed record VerificationRevoked(Guid VerificationId, Guid CustomerId, string MarketCode, string Reason, string LocaleHint);
public sealed record VerificationExpired(Guid VerificationId, Guid CustomerId, string MarketCode, string LocaleHint);
public sealed record VerificationReminderDue(Guid VerificationId, Guid CustomerId, string MarketCode, int WindowDays, DateTimeOffset ExpiresAt, string LocaleHint);
public sealed record VerificationSuperseded(Guid PriorVerificationId, Guid NewVerificationId, Guid CustomerId, string MarketCode);
public sealed record VerificationVoided(Guid VerificationId, Guid CustomerId, string Reason);
```

Spec 025 subscribes via `IDomainEventHandler<T>`. Verification state writes use the in-process bus and never block on spec 025's delivery success (FR-034).

---

## 7. Cross-module contracts (live in `Modules/Shared/`)

Defined by **this** spec, implemented here, consumed by 005/009/010/019/023:
- `ICustomerVerificationEligibilityQuery` — see [contracts/verification-contract.md](./contracts/verification-contract.md) §Eligibility query.

Defined by **this** spec, implemented by spec 004 (Identity), consumed here:
- `ICustomerAccountLifecycleSubscriber` — receives `CustomerAccountLocked`, `CustomerAccountDeleted`, `CustomerMarketChanged`.

Defined by spec 005 (Catalog), declared in `Modules/Shared/` so 020 can reference without cycle:
- `IProductRestrictionPolicy.GetForSku(sku) → { restricted_in_markets, required_profession, vendor_id? }`. (V1: `vendor_id` always null.)

Defined by **this** spec, **null implementation** in V1, swappable in Phase 1.5+:
- `IRegulatorAssistLookup` — see [research.md §R11](./research.md).
