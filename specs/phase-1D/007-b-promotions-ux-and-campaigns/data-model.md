# Data Model: Promotions UX & Campaigns (Spec 007-b · Phase 1D)

**Date**: 2026-04-28
**Inputs**: spec.md, plan.md, research.md (this directory); 007-a `data-model.md` (the engine's existing tables).
**Scope**: 6 new tables in the `pricing` schema (`commercial_thresholds`, `campaigns`, `campaign_links`, `preview_profiles`, `commercial_approvals`, `commercial_audit_events`) + additive migrations on 2 existing 007-a tables (`coupons`, `promotions`) + 1 augmented existing table (`product_tier_prices`). 2 explicit state machines. 18 audit-event-kind enum values (deduped to 14 base kinds; sub-aliases noted in §5). 10 domain events. 5 cross-module shared interfaces.

---

## 1. ERD

```text
                                    ┌─────────────────────────────┐
                                    │ pricing.commercial_thresholds│  one row per market
                                    │ pk: market_code             │
                                    │ - gate_enabled              │
                                    │ - threshold_percent_off     │
                                    │ - threshold_amount_off_minor│
                                    │ - threshold_duration_days   │
                                    │ - coupon_grace_seconds      │
                                    │ - promotion_grace_seconds   │
                                    └─────────────────────────────┘
                                                  ▲ read-only
                                                  │
              ┌───────────────────┐    ┌──────────┴──────────┐    ┌──────────────────────┐
              │ pricing.coupons    │    │ pricing.promotions   │    │ pricing.campaigns     │
              │ (existing 007-a    │    │ (existing 007-a       │    │ (NEW)                 │
              │  + lifecycle cols) │    │  + lifecycle cols)    │    │ pk: id (uuid)         │
              │ pk: id (uuid)      │    │ pk: id (uuid)         │    │ - markets[]           │
              │ - state            │    │ - state               │    │ - state               │
              │ - markets[]        │    │ - markets[]           │    │ - landing_query?      │
              │ - vendor_id?       │    │ - vendor_id?          │    │ - vendor_id?          │
              │ - applies_to_broken│    │ - applies_to_broken   │    │ - link_broken         │
              │ - display_in_      │    │ - banner_eligible     │    │ - row_version (xmin)  │
              │   banners          │    │ - row_version (xmin)  │    │ - name_ar / name_en   │
              │ - row_version      │    │ - priority            │    └──────────────────────┘
              └────────┬──────────┘    └─────────┬────────────┘                ▲
                       │                          │                              │ 1
                       │                          │                              │ ▼ many
                       │                          │                ┌─────────────────────────────┐
                       │                          │                │ pricing.campaign_links       │
                       │                          │                │ pk: id (uuid)                │
                       │                          │                │ fk: campaign_id              │
                       │                          │                │ - kind: promotion|coupon|    │
                       │                          │                │   landing_only               │
                       │                          │                │ - target_id?                 │
                       │                          │                │ - link_broken                │
                       │                          │                └─────────────────────────────┘
                       │                          │                              ▲
                       │                          │                              │
                       │                          │                  refs (target_id)
                       │                          │                              │
                       └──────────────────────────┴──────────────────────────────┘
                                                  │
                                                  ▼ audited via append-only
                                ┌─────────────────────────────────────────┐
                                │ pricing.commercial_audit_events          │
                                │ pk: id (uuid, append-only via trigger)   │
                                │ - target_entity_kind / target_entity_id  │
                                │ - actor_id / actor_role                  │
                                │ - kind (16 enum values §5)               │
                                │ - before_jsonb / after_jsonb / diff_jsonb│
                                │ - reason_note?                           │
                                └─────────────────────────────────────────┘
                                                  ▲
                                                  │ also written to (canonical)
                                ┌─────────────────────────────────────────┐
                                │ shared.audit_log_entries (spec 003)      │
                                └─────────────────────────────────────────┘

              ┌──────────────────────────────────┐    ┌───────────────────────────┐
              │ pricing.product_tier_prices       │    │ pricing.b2b_tiers          │
              │ (existing 007-a + company_id col) │ ─▶ │ (existing 007-a, untouched)│
              │ pk: id (uuid)                     │ fk │                            │
              │ - tier_id?                        │    └───────────────────────────┘
              │ - company_id?  ── XOR check ──    │
              │ - copied_from_tier_id?            │
              │ - sku, market_code, net_minor     │
              │ - state (active|deactivated)      │
              │ - vendor_id?                      │
              │ - company_link_broken             │
              │ - row_version (xmin)              │
              └──────────────────────────────────┘

              ┌─────────────────────────────────┐    ┌─────────────────────────────────┐
              │ pricing.preview_profiles         │    │ pricing.commercial_approvals     │
              │ pk: id (uuid)                    │    │ pk: id (uuid)                    │
              │ - market_code, locale            │    │ - target_entity_kind / id        │
              │ - account_kind, tier_id?         │    │ - author_actor_id                │
              │ - verification_state             │    │ - approver_actor_id              │
              │ - cart_lines (jsonb)             │    │ - cosign_note (≥10 chars)        │
              │ - visibility (personal|shared)   │    │ - approved_at_utc                │
              │ - created_by                     │    │ - row_version (xmin)             │
              │ - vendor_id?                     │    │ unique (target_kind, target_id)  │
              │ - row_version (xmin)             │    └─────────────────────────────────┘
              └─────────────────────────────────┘
```

---

## 2. Tables

### 2.1 `pricing.coupons` — augmented (existing 007-a table)

**New columns added by Migration A** (existing columns unchanged):

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `state` | `pricing.lifecycle_state` enum | NOT NULL, default `'draft'` | Values: `draft`, `scheduled`, `active`, `deactivated`, `expired` |
| `state_changed_at_utc` | `timestamptz` | NOT NULL, default `now()` | Updated on every transition |
| `state_changed_by_actor_id` | `uuid` | NOT NULL, default `'00000000-0000-0000-0000-000000000000'` | System actor for the seeded default; real actors thereafter |
| `state_changed_reason_note` | `text` | NULL | Required (≥ 10 chars) only on deactivate; check constraint enforces |
| `applies_to_broken` | `bool` | NOT NULL, default `false` | Set by `CatalogSkuArchivedHandler` |
| `applies_to_broken_at_utc` | `timestamptz` | NULL | Set when `applies_to_broken` flips to true |
| `vendor_id` | `uuid` | NULL, INDEXED | Always null in V1 |
| `display_in_banners` | `bool` | NOT NULL, default `false` | Coupon-only |
| `row_version` | `xid` (system column `xmin` mapped) | system | EF Core `IsRowVersion()` |

Indexes (new): `idx_coupons_state` (`state` WHERE `state` IN (`'scheduled'`, `'active'`)), `idx_coupons_state_changed_at_utc`, `idx_coupons_vendor_id`, `idx_coupons_applies_to_broken` (partial WHERE true).

Existing unique index on `UPPER(code)` is verified intact (007-a R6).

### 2.2 `pricing.promotions` — augmented (existing 007-a table)

**New columns added by Migration A** (mirror of 2.1, plus):

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `banner_eligible` | `bool` | NOT NULL, default `false` | Promotion-only |

All other lifecycle columns identical to coupons (§2.1).

Indexes mirror coupons; plus existing `priority` index from 007-a (verified intact).

### 2.3 `pricing.product_tier_prices` — augmented (existing 007-a table)

**New columns added by Migration B**:

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `company_id` | `uuid` | NULL | FK to `b2b.companies(id)` (spec 021); enforced at app layer (no DB FK to avoid cross-module schema coupling) |
| `copied_from_tier_id` | `uuid` | NULL | Audit-only pointer |
| `state` | `pricing.business_pricing_state` enum | NOT NULL, default `'active'` | Values: `active`, `deactivated` |
| `state_changed_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |
| `state_changed_by_actor_id` | `uuid` | NOT NULL |  |
| `state_changed_reason_note` | `text` | NULL | Required on deactivate |
| `company_link_broken` | `bool` | NOT NULL, default `false` |  |
| `company_link_broken_at_utc` | `timestamptz` | NULL |  |
| `vendor_id` | `uuid` | NULL, INDEXED |  |
| `row_version` | `xid` | system | xmin |

**Check constraint**: `chk_tier_xor_company` — `(tier_id IS NOT NULL AND company_id IS NULL) OR (tier_id IS NULL AND company_id IS NOT NULL) OR (tier_id IS NOT NULL AND company_id IS NOT NULL AND copied_from_tier_id IS NOT NULL)`. This allows the three valid row kinds: tier-only, company-only, or company-override-copied-from-tier (with the audit pointer set).

**Indexes** (new):
- `ux_tier_pricing_tier_sku_market` UNIQUE PARTIAL on `(tier_id, sku, market_code)` WHERE `company_id IS NULL` AND `state = 'active'`.
- `ux_tier_pricing_company_sku_market` UNIQUE PARTIAL on `(company_id, sku, market_code)` WHERE `company_id IS NOT NULL` AND `state = 'active'`.
- `idx_tier_pricing_company_link_broken` partial WHERE `true`.
- `idx_tier_pricing_vendor_id`.

### 2.4 `pricing.campaigns` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default `gen_random_uuid()` |  |
| `name_ar` | `text` | NOT NULL, length 1-300 |  |
| `name_en` | `text` | NOT NULL, length 1-300 |  |
| `valid_from` | `timestamptz` | NOT NULL |  |
| `valid_to` | `timestamptz` | NOT NULL, > `valid_from` (check constraint) |  |
| `markets` | `text[]` | NOT NULL, NON-EMPTY (check), each `IN ('SA','EG')` |  |
| `landing_query` | `text` | NULL, max 1024 chars | URL-style facet query |
| `notes_internal` | `text` | NULL, max 4000 chars | Operator-only; admin single-locale OK per FR-030 |
| `state` | `pricing.lifecycle_state` enum | NOT NULL, default `'draft'` |  |
| `state_changed_at_utc` | `timestamptz` | NOT NULL |  |
| `state_changed_by_actor_id` | `uuid` | NOT NULL |  |
| `state_changed_reason_note` | `text` | NULL |  |
| `link_broken` | `bool` | NOT NULL, default `false` |  |
| `vendor_id` | `uuid` | NULL, INDEXED |  |
| `row_version` | `xid` | system | xmin |

Indexes: `idx_campaigns_state_active_or_scheduled` partial; `idx_campaigns_valid_from`; `idx_campaigns_markets` GIN; `idx_campaigns_vendor_id`.

### 2.5 `pricing.campaign_links` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `campaign_id` | `uuid` | NOT NULL, FK `pricing.campaigns(id)` ON DELETE CASCADE |  |
| `kind` | `text` | NOT NULL, CHECK `IN ('promotion','coupon','landing_only')` |  |
| `target_id` | `uuid` | NULL — required when kind ≠ `'landing_only'` (check) | App-layer FK to `coupons.id` or `promotions.id` |
| `link_broken_at_utc` | `timestamptz` | NULL | Set by `CampaignLinkBrokenWatcher` |

A campaign has at most one active link at a time (unique partial index on `campaign_id` WHERE `link_broken_at_utc IS NULL`).

### 2.6 `pricing.preview_profiles` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `name` | `text` | NOT NULL, length 1-200 | operator-facing label, single locale OK |
| `market_code` | `text` | NOT NULL, CHECK `IN ('SA','EG')` |  |
| `locale` | `text` | NOT NULL, CHECK `IN ('ar','en')` |  |
| `account_kind` | `text` | NOT NULL, CHECK `IN ('consumer','b2b')` |  |
| `tier_id` | `uuid` | NULL |  |
| `verification_state` | `text` | NOT NULL, CHECK `IN ('none','submitted','approved','rejected','expired')` |  |
| `cart_lines` | `jsonb` | NOT NULL, schema-validated `[{sku, qty, restricted}]`, max 50 entries |  |
| `visibility` | `text` | NOT NULL, default `'personal'`, CHECK `IN ('personal','shared')` |  |
| `created_by` | `uuid` | NOT NULL |  |
| `vendor_id` | `uuid` | NULL, INDEXED |  |
| `row_version` | `xid` | system |  |

Indexes: `idx_preview_profiles_visibility_personal_by_creator` partial WHERE `visibility = 'personal'` on `(created_by)`; `idx_preview_profiles_visibility_shared` partial WHERE `visibility = 'shared'`.

### 2.7 `pricing.commercial_approvals` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `target_entity_kind` | `text` | NOT NULL, CHECK `IN ('coupon','promotion')` | gate doesn't apply to campaigns or BPRs in V1 |
| `target_entity_id` | `uuid` | NOT NULL |  |
| `author_actor_id` | `uuid` | NOT NULL | the operator who drafted the rule |
| `approver_actor_id` | `uuid` | NOT NULL, CHECK `approver_actor_id <> author_actor_id` | separation of duties, layer 2 of R12 |
| `cosign_note` | `text` | NOT NULL, length ≥ 10 |  |
| `approved_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |
| `row_version` | `xid` | system |  |

**Unique constraint**: `(target_entity_kind, target_entity_id)` — enforces "one approval per draft" at the DB layer (R12 layer 2).

### 2.8 `pricing.commercial_thresholds` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `market_code` | `text` | PK, CHECK `IN ('SA','EG')` |  |
| `gate_enabled` | `bool` | NOT NULL, default `true` |  |
| `threshold_percent_off` | `numeric(5,2)` | NULL, range 0-100 | NULL disables this single criterion |
| `threshold_amount_off_minor` | `bigint` | NULL, ≥ 0 |  |
| `threshold_duration_days` | `int` | NULL, ≥ 0 |  |
| `coupon_in_flight_grace_seconds` | `int` | NOT NULL, range 300-7200, default 1800 |  |
| `promotion_in_flight_grace_seconds` | `int` | NOT NULL, range 300-7200, default 1800 |  |
| `updated_at_utc` | `timestamptz` | NOT NULL |  |
| `updated_by_actor_id` | `uuid` | NOT NULL |  |
| `row_version` | `xid` | system |  |

Seeded by `PricingThresholdsSeeder` per R8.

### 2.9 `pricing.commercial_audit_events` — NEW (append-only via trigger)

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `target_entity_kind` | `text` | NOT NULL, CHECK `IN ('coupon','promotion','campaign','business_pricing','preview_profile','commercial_threshold','commercial_approval')` |  |
| `target_entity_id` | `uuid` | NOT NULL |  |
| `kind` | `text` | NOT NULL — one of 16 §5 enum values |  |
| `actor_id` | `uuid` | NOT NULL |  |
| `actor_role` | `text` | NOT NULL |  |
| `before_jsonb` | `jsonb` | NULL — present on update / lifecycle events |  |
| `after_jsonb` | `jsonb` | NULL — present on create / update / lifecycle events |  |
| `diff_jsonb` | `jsonb` | NULL — present on update events |  |
| `reason_note` | `text` | NULL |  |
| `recorded_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |
| `correlation_id` | `uuid` | NULL — propagated request-scoped |  |

**Append-only trigger**: `BEFORE UPDATE OR DELETE ON pricing.commercial_audit_events FOR EACH ROW EXECUTE FUNCTION raise_immutable_audit_violation()`. Same pattern as spec 020 / 021's transition tables.

Indexes: `idx_cae_target` on `(target_entity_kind, target_entity_id, recorded_at_utc DESC)`; `idx_cae_actor` on `(actor_id, recorded_at_utc DESC)`; `idx_cae_kind_recent` partial WHERE `recorded_at_utc > now() - interval '90 days'`.

---

## 3. State machines

### 3.1 `LifecycleState` (Coupon, Promotion, Campaign)

#### States

| State | Meaning | Engine treatment |
|---|---|---|
| `draft` | Unpublished; operator authoring | Engine never evaluates |
| `scheduled` | Published with `valid_from > now`; awaiting timer-flip | Engine evaluates `now BETWEEN valid_from AND valid_to` so effectively inactive until timer flips |
| `active` | Live | Engine resolves normally |
| `deactivated` | Operator-paused or system-auto-paused; reversible until `valid_to` | Engine returns `pricing.{kind}.deactivated` on next call (subject to FR-003a in-flight grace from event payload) |
| `expired` | `valid_to` reached; terminal | Read-only; engine returns `pricing.{kind}.expired` |

#### Transitions

| From | To | Trigger | Actor | Required | Forbidden when |
|---|---|---|---|---|---|
| `draft` | `scheduled` | `Schedule` action with `valid_from > now` | `commercial.operator` | bilingual labels valid; high-impact gate satisfied (or no approval needed) |  |
| `draft` | `active` | `Schedule` action with `valid_from <= now` | `commercial.operator` | same as above |  |
| `scheduled` | `active` | `LifecycleTimerWorker` tick | `system` | `valid_from <= now`, state still `scheduled` |  |
| `scheduled` | `deactivated` | `Deactivate` action | `commercial.operator` | reason note ≥ 10 chars |  |
| `scheduled` | `expired` | `LifecycleTimerWorker` tick | `system` | `valid_to <= now` (rare — user scheduled a past window) |  |
| `active` | `deactivated` | `Deactivate` action | `commercial.operator` | reason note ≥ 10 chars |  |
| `active` | `expired` | `LifecycleTimerWorker` tick | `system` | `valid_to <= now`, state still `active` |  |
| `active` | `deactivated` | `BrokenReferenceAutoDeactivationWorker` | `system` | every reference broken AND broken ≥ 7 days |  |
| `deactivated` | `active` | `Reactivate` action | `commercial.operator` | `valid_to > now`; if high-impact gate enabled, fresh `CommercialApproval` required |  |
| `deactivated` | `expired` | `LifecycleTimerWorker` tick | `system` | `valid_to <= now` (terminal lock-in) |  |
| `expired` | (no exit) | — | — | terminal | always |

**Idempotency**: a transition into the current state is a no-op (no audit row, no side effect).

**Concurrency**: every transition asserts `xmin` row_version match; loser receives `409 commercial.row.version_conflict`.

**Hard-delete**: forbidden for any state per FR-005a; `DELETE` API returns `405 commercial.row.delete_forbidden`.

### 3.2 `BusinessPricingState` (ProductTierPrice rows — both tier and company-override kinds)

#### States

| State | Meaning |
|---|---|
| `active` | Engine resolves normally (007-a layer 2) |
| `deactivated` | Engine skips this row; falls back to the next-higher-priority row (company override → tier → list) |

#### Transitions

| From | To | Trigger | Actor |
|---|---|---|---|
| `active` | `deactivated` | `Deactivate` action | `commercial.b2b_authoring` |
| `active` | `deactivated` | `B2BCompanySuspendedHandler` for company-override rows when company is suspended | `system` (after the 7-day grace window per R13) |
| `deactivated` | `active` | `Reactivate` action | `commercial.b2b_authoring` |

No `draft`, no `scheduled`, no `expired` — business pricing takes effect immediately on save and remains until removed (no schedule).

**Hard-delete**: forbidden when referenced by any historical `PriceExplanation` (per FR-005a); allowed for never-saved drafts only.

---

## 4. Reason codes

The complete reason-code surface (49 owned codes + 6 engine-emitted cross-reference codes) is enumerated in `contracts/promotions-ux-and-campaigns-contract.md §11`. ICU keys live in `services/backend_api/Modules/Pricing/Messages/pricing.commercial.{en,ar}.icu`.

---

## 5. Audit-event schema (writes to `audit_log_entries` via `IAuditEventPublisher` + `commercial_audit_events`)

**18 audit-event-kind enum values** (14 base kinds + 4 analytic sub-aliases for deactivated/reactivated):

| Kind | When |
|---|---|
| `coupon.created` | After `INSERT pricing.coupons` |
| `coupon.updated` | After non-state-transition update; field-level diff captured |
| `coupon.lifecycle_transitioned` | After any state transition (audit subkind in `diff_jsonb.state_change`) |
| `coupon.deactivated` | Sub-event of `lifecycle_transitioned` for analytics |
| `coupon.reactivated` | Sub-event of `lifecycle_transitioned` for analytics |
| `promotion.created` | mirror of coupon.created |
| `promotion.updated` |  |
| `promotion.lifecycle_transitioned` |  |
| `promotion.deactivated` |  |
| `promotion.reactivated` |  |
| `business_pricing.row_changed` | Insert / update of a tier row or company override |
| `business_pricing.bulk_imported` | Successful CSV commit |
| `campaign.created` |  |
| `campaign.updated` |  |
| `campaign.lifecycle_transitioned` |  |
| `commercial.threshold_changed` | Update on `pricing.commercial_thresholds` |
| `commercial.approval_recorded` | Insert into `pricing.commercial_approvals` |
| `preview_profile.visibility_changed` | Promotion `personal → shared` or demotion |

(The literal enum-value count is **18**: 5 coupon + 5 promotion + 3 campaign + 2 business_pricing + 2 commercial + 1 preview_profile. The deduped count, treating `*.deactivated` and `*.reactivated` as sub-aliases of `lifecycle_transitioned`, is **14**: 3 coupon + 3 promotion + 3 campaign + 2 business_pricing + 2 commercial + 1 preview_profile. plan.md post-design re-check cites both numbers explicitly.)

Every audit row carries: `actor_id`, `actor_role`, `target_entity_kind`, `target_entity_id`, `before_jsonb?`, `after_jsonb?`, `diff_jsonb?`, `reason_note?`, `recorded_at_utc`, `correlation_id?`.

---

## 6. Domain events (in-process bus → consumed by spec 025)

Declared in `Modules/Shared/CommercialDomainEvents.cs`. All implement `MediatR.INotification`.

| Event | Payload | Subscribers |
|---|---|---|
| `CouponActivated` | `{ coupon_id, market_codes[], code, valid_from, valid_to, activated_at_utc, actor_id }` | spec 025 (admin digest) |
| `CouponExpired` | `{ coupon_id, expired_at_utc }` | spec 025 |
| `CouponDeactivated` | `{ coupon_id, deactivated_at_utc, deactivated_by_actor_id, reason_note, in_flight_grace_seconds }` | spec 025, spec 010 (in-flight gate per R4) |
| `CouponReactivated` | `{ coupon_id, reactivated_at_utc, actor_id }` | spec 025 |
| `PromotionActivated` | mirror | spec 025 |
| `PromotionExpired` | mirror | spec 025 |
| `PromotionDeactivated` | `{ promotion_id, deactivated_at_utc, actor_id, reason_note, in_flight_grace_seconds }` | spec 025, spec 010 |
| `PromotionReactivated` | mirror | spec 025 |
| `CampaignLinkBroken` | `{ campaign_id, broken_link_target_id, broken_link_kind }` | spec 025 |
| `CommercialThresholdChanged` | `{ market_code, before_jsonb, after_jsonb, actor_id }` | spec 025 (audit-digest) |

**Subscription contract**: spec 025 subscribes via the same in-process bus pattern used by spec 020 / 021. Lifecycle writes do NOT block on notification success (FR-033). Failure to deliver a notification does NOT roll back the lifecycle transition.

---

## 7. Cross-module contracts (live in `Modules/Shared/`)

```csharp
// Modules/Shared/ICatalogSkuArchivedSubscriber.cs
public interface ICatalogSkuArchivedSubscriber
{
    Task HandleAsync(CatalogSkuArchivedEvent e, CancellationToken ct);
}

public sealed record CatalogSkuArchivedEvent(
    Guid SkuId,
    string Sku,
    DateTimeOffset ArchivedAtUtc,
    Guid ActorId);

// Modules/Shared/ICatalogSkuArchivedPublisher.cs
public interface ICatalogSkuArchivedPublisher
{
    Task PublishAsync(CatalogSkuArchivedEvent e, CancellationToken ct);
}

// Modules/Shared/IB2BCompanySuspendedSubscriber.cs
public interface IB2BCompanySuspendedSubscriber
{
    Task HandleAsync(B2BCompanySuspendedEvent e, CancellationToken ct);
}

public sealed record B2BCompanySuspendedEvent(
    Guid CompanyId,
    DateTimeOffset SuspendedAtUtc,
    Guid ActorId,
    string ReasonNote);

// Modules/Shared/ICheckoutGraceWindowProvider.cs
public interface ICheckoutGraceWindowProvider
{
    /// <summary>
    /// Returns the configured in-flight grace window for a deactivated rule.
    /// Spec 010 reads grace from the deactivation event payload on the hot path;
    /// this provider is for ad-hoc inspection only.
    /// </summary>
    Task<int?> GetGraceSecondsAsync(string ruleKind, Guid ruleId, CancellationToken ct);
}

// Modules/Shared/CommercialDomainEvents.cs (extracted highlights)
public sealed record CouponDeactivated(
    Guid CouponId,
    DateTimeOffset DeactivatedAtUtc,
    Guid DeactivatedByActorId,
    string ReasonNote,
    int InFlightGraceSeconds) : INotification;
// ...etc for the other 9 events
```

**Ownership**: spec 005 implements `ICatalogSkuArchivedPublisher`; spec 021 implements `IB2BCompanySuspendedPublisher`; spec 007-b implements both subscribers (under `Modules/Pricing/Subscribers/`) and `ICheckoutGraceWindowProvider` (in `PricingModule.cs`); spec 025 implements all 10 domain-event handlers.

---

## 8. Migration ordering

Migrations are applied in the following order during `dotnet ef database update` (Phase B):

1. **`AddLifecycleColumnsToCouponsAndPromotions`** — adds `state` enum + 7 columns + indexes to `pricing.coupons` and `pricing.promotions`. Default seeded `state='draft'` for any pre-existing 007-a row (there should be none in production at this point; in Dev, the row count is small and `'draft'` is correct).
2. **`ExtendProductTierPricesForCompanyOverrides`** — adds `company_id`, `copied_from_tier_id`, `state`, lifecycle columns, broken-flag, `vendor_id`, partial unique indexes, XOR check constraint to `pricing.product_tier_prices`.
3. **`AddCommercialAuthoringTables`** — creates `pricing.commercial_thresholds`, `pricing.campaigns`, `pricing.campaign_links`, `pricing.preview_profiles`, `pricing.commercial_approvals`, `pricing.commercial_audit_events` + the append-only trigger function.

Each migration is reversible (`Down` method present) for Dev rollbacks; production rollbacks are documented in the runbook (R14 of plan).

---

## 9. Volume estimates (V1)

| Table | Initial rows | Growth/month |
|---|---|---|
| `pricing.coupons` | ~50 (seed + early authored) | ~50/month |
| `pricing.promotions` | ~30 | ~20/month |
| `pricing.campaigns` | ~10 | ~10/month |
| `pricing.campaign_links` | ~10 | ~15/month |
| `pricing.product_tier_prices` (tier rows) | ~3 000 (seeded by 007-a) | ~200/month edits |
| `pricing.product_tier_prices` (company overrides) | ~50 | ~30/month |
| `pricing.preview_profiles` | ~5 (seed) | ~10/month |
| `pricing.commercial_approvals` | 0 | ~10/month (gated drafts) |
| `pricing.commercial_thresholds` | 2 (seeded) | rare changes |
| `pricing.commercial_audit_events` | 0 | ~5 000/month at steady state |

Total storage at end of year 1 across all 007-b additive tables: < 500 MB (audit events dominate; well within Azure Postgres Flexible Server's default tier).

---

## 10. Read-side hot paths and cache strategy

| Read path | Frequency | Caching |
|---|---|---|
| `IPriceCalculator.Calculate(ctx)` reading coupons / promotions / tier prices | Hundreds / minute (cart pricing) | Owned by 007-a; this spec does NOT alter it. The new lifecycle columns are read in the engine's existing query (additive WHERE clause `state IN ('active','scheduled')`); no new round-trip. |
| Operator UI listing coupons (filtered by state) | Tens / minute | EF Core query with `idx_coupons_state` index; no custom cache. |
| Coupon-uniqueness check on form blur | Spiky during authoring | Functional unique index on `UPPER(code)`; rate-limited to 60 / min / actor (R6). |
| Threshold lookup at activation | Tens / hour | In-process 30-second cache keyed by `market_code`; invalidated on `CommercialThresholdChanged` event. |
| Preview tool engine call | Spiky during authoring | No additional cache; relies on engine's internal caches. |

---

## 11. Out-of-scope schema concerns

- **Per-rule grace-window override**: not introduced; market-level threshold is sufficient (research §R4).
- **Coupon-bulk-code-generation table**: deferred per spec.md Out of Scope.
- **Customer-facing wallet table**: deferred per spec.md Out of Scope.
- **A/B-test variant table**: deferred per spec.md Out of Scope.
- **Per-warehouse pricing**: deferred per spec.md Out of Scope.
- **Per-vendor authoring scope**: V1 single-vendor; `vendor_id` reserved.
