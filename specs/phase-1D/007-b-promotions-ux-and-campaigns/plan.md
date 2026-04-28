# Implementation Plan: Promotions UX & Campaigns

**Branch**: `phase_1D_creating_specs` (working) · target merge: `007-b-promotions-ux-and-campaigns` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1D/007-b-promotions-ux-and-campaigns/spec.md`

## Summary

Deliver the Phase-1D commercial-authoring layer that sits **on top of the existing 007-a `Pricing` module** and turns its raw engine tables (`pricing.coupons`, `pricing.promotions`, `pricing.product_tier_prices`, `pricing.b2b_tiers`) into a usable, auditable, bilingual admin surface. The deliverable is **strictly authoring + lifecycle + preview + audit + linkage + seeders + cross-module referential integrity** — *no new pricing math, no engine changes*. All resolution still flows through `IPriceCalculator.Calculate(ctx)`.

Concretely:

1. **Lifecycle layer** (Principle 24): a four-state machine `draft → (scheduled | active) → (deactivated ↔ active | expired)` for Coupons, Promotions, and Campaigns; a two-state `active ↔ deactivated` for BusinessPricingRow + B2BTier rows. Encoded in `LifecycleStateMachine.cs`. Schedule activation / expiry driven by a `LifecycleTimerWorker` running per minute (≤ 60 s tolerance per FR-002).
2. **Authoring slices** under `Modules/Pricing/Admin/`: full CRUD + state-transition handlers for Coupons, Promotions, ProductTierPrices (tier rows + company-override rows), Campaigns, PreviewProfiles, and CommercialApprovals. Each slice writes a structured audit row via the existing `IAuditEventPublisher` (Principle 25).
3. **Preview tool** (cross-cutting): a single `PreviewPriceExplanation` handler that calls `IPriceCalculator.Calculate(ctx)` in `Preview` mode (existing 007-a FR-012) with the in-flight (unsaved) rule overlaid on the engine's read-side, then renders the layer-by-layer explanation with a delta ribbon vs. the unmodified engine output. Must hit p95 ≤ 200 ms for a 20-line cart (SC-002).
4. **Approval gate** (FR-025–FR-027): per-market `commercial_thresholds` rows (seeded ON at launch with conservative values: 30 % off / 14 days / SAR 50 000 / EGP 250 000); high-impact drafts route to a `CommercialApproval` queue requiring a separate `commercial.approver` co-signature with a ≥ 10-character note. Self-approval is hard-blocked.
5. **Cross-module referential integrity** (FR-034a–FR-034c): subscribes to `catalog.sku.archived` (spec 005) and `b2b.company.suspended` (spec 021) events, raises `applies_to_broken=true` / `company_link_broken=true` indicators, and runs a `BrokenReferenceAutoDeactivationWorker` that auto-deactivates `active` rules whose only references are broken after a 7-day grace.
6. **Lifecycle event emission**: 007-b publishes 10 domain events on the in-process bus consumed by spec 025 (FR-032 / FR-033): `CouponActivated/Expired/Deactivated/Reactivated`, `PromotionActivated/Expired/Deactivated/Reactivated`, `CampaignLinkBroken`, `CommercialThresholdChanged`. Critically, deactivation events carry `in_flight_grace_seconds` (default 1800; 5–120-min market-tunable per FR-003a) so spec 010 checkout can implement the in-flight grace contract.
7. **Multi-vendor readiness** (Principle 6): every new and existing in-scope row carries a `vendor_id` column (nullable in V1, indexed). The admin UI hides vendor scoping; a future Phase 2 spec layers vendor-scoped authoring with no schema migration.
8. **`promotions-v1` seeder**: idempotent, runs in Dev / Staging via the spec 003 seed framework; populates ≥ 1 row in each of `draft / scheduled / active / deactivated / expired` for Coupons + Promotions, plus 3 tier rows + 2 company overrides + 3 campaigns; bilingual editorial-grade AR/EN labels (Principle 4).

No customer-facing UI ships in this spec. The admin web UI is owned by Phase 1C spec 015 + downstream operational tooling; spec 015's contract merge is the gate before Lane B begins.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16 (per spec 004 + ADR-022).

**Primary Dependencies**:
- `MediatR` v12.x + `FluentValidation` v11.x — vertical-slice handlers (ADR-003).
- `Microsoft.EntityFrameworkCore` v9.x — code-first migrations on the existing `Pricing` schema (ADR-004).
- `Microsoft.AspNetCore.Authorization` (built-in) — `[RequirePermission("commercial.*")]` attributes from spec 004's RBAC.
- `Modules/Pricing/IPriceCalculator` (existing, owned by 007-a) — the **only** pricing-resolution entry point; consumed in `Preview` mode by the preview tool. **Not modified by this spec.**
- `Modules/AuditLog/IAuditEventPublisher` (existing) — every create / update / lifecycle transition / deactivation / reactivation / approval event.
- `Modules/Identity` consumables — RBAC primitives + new permissions `commercial.operator`, `commercial.b2b_authoring`, `commercial.approver`, `commercial.threshold_admin` (last is `super_admin`-only).
- `Modules/Shared/IAuditEventPublisher`, `Modules/Shared/AppDbContext` — existing; reused.
- New shared interfaces declared under `Modules/Shared/` (see Project Structure):
  - `ICatalogSkuArchivedSubscriber` — event sink for `catalog.sku.archived`; spec 005 publishes.
  - `IB2BCompanySuspendedSubscriber` — event sink for `b2b.company.suspended`; spec 021 publishes.
  - `ICheckoutGraceWindowProvider` — read-only API exposing `(coupon_id|promotion_id) → in_flight_grace_seconds`; spec 010 consumes for the in-flight grace gate.
  - `ICommercialDomainEvents` — publish surface for the 8 lifecycle events.
- `MessageFormat.NET` (already vendored by spec 003) — ICU AR/EN keys for every operator-visible reason code.

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). Schema work splits into:

**Migration A — additive columns on existing 007-a tables** (`pricing.coupons`, `pricing.promotions`):
- `state` (enum: `draft | scheduled | active | deactivated | expired`, default `draft`).
- `state_changed_at_utc`, `state_changed_by_actor_id`, `state_changed_reason_note`.
- `applies_to_broken bool default false`, `applies_to_broken_at_utc?`.
- `vendor_id uuid?` indexed.
- `display_in_banners bool default false` (Coupon only).
- `banner_eligible bool default false` (Promotion only).
- `priority int default 100` (Promotion only — already present in 007-a per FR-008; verified during Phase B).

**Migration B — additive columns on `pricing.product_tier_prices`** to support company overrides:
- `company_id uuid?` (mutually exclusive with `tier_id` non-null but EITHER is required by check constraint).
- `copied_from_tier_id uuid?` (audit-only pointer when a company override was seeded from a tier row).
- `company_link_broken bool default false`, `company_link_broken_at_utc?`.
- `vendor_id uuid?` indexed.
- Unique partial indexes: `(tier_id, sku, market_code) WHERE company_id IS NULL`; `(company_id, sku, market_code) WHERE company_id IS NOT NULL`.

**Migration C — new tables in the `pricing` schema** (6 net-new):
- `commercial_thresholds` — per-market threshold settings + `gate_enabled` flag.
- `campaigns` + `campaign_links` — banner-link entities.
- `preview_profiles` — admin-only sample customer + cart contexts (with `visibility` + `created_by`).
- `commercial_approvals` — co-signature records on high-impact activations.
- `commercial_audit_events` — append-only diff payload referenced by the shared audit log (denormalized for fast operator-side review).

State writes use EF Core optimistic concurrency via Postgres `xmin` mapped as `IsRowVersion()` (the same pattern adopted in spec 020 / 021) for the concurrent-edit case (Edge Case: two operators editing the same rule).

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers Postgres (per spec 003 contract — no SQLite shortcut). Contract tests assert HTTP shape parity between every `spec.md` Acceptance Scenario and the live handler. Property tests for the lifecycle state-machine invariants (no terminal→non-terminal, no double-decision, idempotent transitions, schedule timer ≤ 60 s tolerance). Concurrency tests for the optimistic-concurrency edit guard. Cross-module subscriber tests use a fake `ICatalogSkuArchivedPublisher` + `IB2BCompanySuspendedPublisher` shipped from `Modules/Shared/Testing/`. Time-driven worker tests use `FakeTimeProvider`.

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — Phase 1C spec 015 delivers the admin UI against the contracts merged here.

**Project Type**: .NET vertical-slice extension to the existing `Pricing` modular-monolith module (ADR-023). No new top-level module.

**Performance Goals**:
- **Coupon-uniqueness lookup** (real-time check on form blur): p95 ≤ 200 ms (FR-007).
- **Preview tool** (call `IPriceCalculator.Calculate(ctx)` in Preview mode + render delta ribbon): p95 ≤ 200 ms for a 20-line sample cart (SC-002).
- **SKU picker query** against a 50 000-SKU catalog: p95 ≤ 300 ms (SC-006).
- **Coupon / Promotion / Campaign list** (default 50/page with filters): p95 ≤ 600 ms with 10 000 rows.
- **Lifecycle state-transition write path** (single rule activate / deactivate / approve): p95 ≤ 800 ms inclusive of audit row.
- **Admin write throughput**: rate-limit hard-cap 30 writes/min/actor + 600 writes/hour/actor (FR-035) — enforced via the existing spec 003 rate-limit middleware.
- **`LifecycleTimerWorker` tolerance**: ≤ 60 s drift between `valid_from` / `valid_to` and the corresponding state transition (FR-002 / SC-005).
- **Bulk CSV import preview**: p95 ≤ 5 000 ms for a 1 000-row file.

**Constraints**:
- **Idempotency**: every state-transitioning POST endpoint requires `Idempotency-Key` (per spec 003 platform middleware); duplicates within 24 h return the original 200 response.
- **Concurrency guard**: every state-transitioning command uses an EF Core `RowVersion` (xmin) optimistic-concurrency check; the loser sees `commercial.row.version_conflict` (Edge Case + FR-035).
- **Hard-delete prohibition** (FR-005a): the API layer MUST return `405 commercial.row.delete_forbidden` for any `DELETE /v1/admin/commercial/{kind}/{id}` route on Coupons / Promotions / Campaigns; the admin UI MUST NOT render a Delete affordance. Exception: PreviewProfile rows MAY be hard-deleted by their author (or `super_admin`); BusinessPricingRow rows in `draft` (never saved) MAY be discarded by their author.
- **Engine immutability** (Out of Scope): this spec MUST NOT modify any code under `Modules/Pricing/Internal/Calculate/` or any layer-resolution logic. All such changes require a 007-a amendment.
- **Time source**: every state transition + every rate-limit window reads `TimeProvider.System.GetUtcNow()`; tests inject `FakeTimeProvider`.
- **PII at rest**: campaign reason notes + audit diff payloads stored as plain TEXT (TDE covers at-rest); customer-supplied data is not handled in this surface.
- **PII in logs**: `ILogger` destructuring filters block any `internal_notes` field on Campaigns.
- **Worker idempotency**: `LifecycleTimerWorker` and `BrokenReferenceAutoDeactivationWorker` are safe to re-run within a window; transitions are no-ops when already in the target state. Workers use the existing Postgres advisory-lock pattern from spec 020 to coordinate horizontally (`pg_try_advisory_lock(hashtext('pricing.lifecycle_timer'))`).
- **In-flight grace contract**: deactivation events MUST carry `in_flight_grace_seconds` so spec 010 checkout can honor a `PriceExplanation` issued before the deactivation timestamp until either `payment_authorized` or grace-elapsed (FR-003a).
- **AR editorial**: every customer-visible label / description on Coupons / Promotions / Campaigns MUST have both `ar` and `en` populated (validator-enforced at write time per FR-030); admin-only fields MAY be single-locale.

**Scale/Scope**: ~28 HTTP endpoints (coupon: 7, promotion: 7, business-pricing: 6, campaign: 5, preview-profile: 3, commercial-approval queue: 3, commercial-thresholds: 2). 41 functional requirements (FR-001…FR-037 plus FR-003a, FR-005a, FR-034a, FR-034b, FR-034c). 10 SCs. 9 key entities (6 net-new + 3 augmented). 1 four-state lifecycle + 1 two-state lifecycle. 6 net-new tables + 2 augmented existing tables. 2 hosted workers. 10 domain events. Target capacity at V1 launch: 200 active coupons + 100 active promotions per market, 5 000 BusinessPricingRows per market, 20 active campaigns per market; peaks of 25 concurrent commercial operators authoring simultaneously.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | This spec adds no customer-facing surface. Browse / cart / checkout flows from 005 / 009 / 010 are unchanged. | PASS |
| P4 Arabic / RTL editorial | Every customer-visible label + description on Coupons / Promotions / Campaigns is required-bilingual at write time (FR-030); validator enforces. AR labels seeded by `promotions-v1` flagged in `AR_EDITORIAL_REVIEW.md`. | PASS |
| P5 Market Configuration | `commercial_thresholds` rows are per-market; coupon `markets[]`, promotion `markets[]`, campaign `markets[]` per-market; in-flight grace is per-market; `valid_from / valid_to` interpreted with per-market timezone for display, UTC for resolution (Edge Case). No hardcoded EG/KSA branches. | PASS |
| P6 Multi-vendor-ready | `vendor_id` slot reserved on Coupons, Promotions, ProductTierPrices, Campaigns, PreviewProfiles. V1 always null and indexed. | PASS |
| P9 B2B is V1 | Business-pricing authoring (tier rows + company overrides) ships in V1 (FR-013–FR-016). Distinct `commercial.b2b_authoring` permission. | PASS |
| P10 Pricing centralized | **Critical re-check.** No new pricing primitive is introduced. The Preview tool calls `IPriceCalculator.Calculate(ctx)` in Preview mode (007-a FR-012). All resolution still flows through 007-a's engine. Out of Scope explicitly bars engine changes. | PASS |
| P19 Notifications | Domain events listed in `data-model.md §6`; spec 025 subscribes; lifecycle writes never block on notification success. The grace-window contract `in_flight_grace_seconds` flows in the deactivation events. | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9, MediatR — no deviation. | PASS |
| P23 Architecture | Vertical slice extension to the existing `Modules/Pricing/`; reuses existing seams (`IPriceCalculator`, `IAuditEventPublisher`, RBAC). No premature service extraction; no new top-level module. | PASS |
| P24 State Machines | Two explicit state machines (`LifecycleState` for Coupon/Promotion/Campaign, `BusinessPricingState` for tier+company-override rows); each documented in `data-model.md §3` with allowed states, transitions, triggers, actors, failure handling. | PASS |
| P25 Audit | Every create + update + lifecycle transition + deactivation + reactivation + approval event emits an audit row with actor, timestamp, prior state, new state, structured field-level diff, and reason note for deactivation. SC-003 verifies. | PASS |
| P27 UX Quality | No UI here, but error payloads carry stable reason codes (`coupon.code.duplicate`, `promotion.locked.active_pricing_field`, `commercial.row.version_conflict`, `commercial.self_approval.forbidden`, `pricing.target.archived`, etc.) for spec 015 / 019 to render. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request / response / errors / reason codes. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1D Milestone 7. Bulk-coupon-code-generation, A/B testing, and customer coupon-wallet are deliberately deferred to Phase 1.5 / Phase 2 (Out of Scope). | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/Pricing/Admin/` + new sub-folders + Persistence migrations. | PASS |
| ADR-003 Vertical slice | One folder per slice under `Pricing/Admin/Coupons/`, `.../Promotions/`, `.../BusinessPricing/`, `.../Campaigns/`, `.../PreviewProfiles/`, `.../CommercialApprovals/`. | PASS |
| ADR-004 EF Core 9 | Code-first migrations under `Modules/Pricing/Persistence/Migrations/`. `SaveChangesInterceptor` audit hook from spec 003 reused. `ManyServiceProvidersCreatedWarning` already suppressed in existing `PricingModule.cs` (project-memory rule). | PASS |
| ADR-010 KSA residency | All tables in the KSA-region Postgres; no cross-region replication. | PASS |

**No violations**. Complexity Tracking below documents intentional non-obvious design choices.

### Post-design re-check (after Phase 1 artifacts)

Re-evaluated after `data-model.md`, `contracts/promotions-ux-and-campaigns-contract.md`, `quickstart.md`, and `research.md` were authored. **No new violations introduced.** Specific re-checks:

- **P10 (re-emphasized)**: every code path under `Modules/Pricing/Internal/Calculate/` is left untouched; the Preview tool is a thin caller of `IPriceCalculator.Calculate` and renders the result. Verified via the post-design contract review: 0 endpoints write to `pricing.price_explanations` or recompute totals. ✅
- **P5**: every market-tunable knob (`gate_enabled`, `threshold_percent_off`, `threshold_amount_off_minor`, `threshold_duration_days`, `coupon_in_flight_grace_seconds`, `promotion_in_flight_grace_seconds`) is sourced from `pricing.commercial_thresholds` rows. ✅
- **P6**: every new table carries `vendor_id` with the same nullable+indexed pattern. ✅
- **P19**: 8 domain events declared in `Modules/Shared/CommercialDomainEvents.cs`; subscribed by spec 025; no in-line notification calls inside lifecycle writes. ✅
- **P24**: `LifecycleState` and `BusinessPricingState` machines encoded as explicit transition guards in `LifecycleStateMachine.cs` / `BusinessPricingStateMachine.cs`; transitions visible at compile time. ✅
- **P25**: `audit_event_kinds` documented in `data-model.md §5`. The denormalized list spans Coupon ×3 base kinds (`created`, `updated`, `lifecycle_transitioned`) plus 2 sub-aliases (`deactivated`, `reactivated`); same shape for Promotion (3 + 2); plus Campaign ×3 (`created`, `updated`, `lifecycle_transitioned`); BusinessPricing ×2 (`row_changed`, `bulk_imported`); Commercial ×2 (`threshold_changed`, `approval_recorded`); PreviewProfile ×1 (`visibility_changed`). The deduped kind count (treating `deactivated`/`reactivated` as analytic sub-aliases of `lifecycle_transitioned`) is **14**; the literal enum count including sub-aliases is **18**. data-model §5 declares 18 enum values. ✅
- **P28**: contracts file enumerates 28 endpoints + the 5 cross-module interfaces with full reason-code inventory (~50 owned codes; see contracts §11 for the canonical list). ✅

## Project Structure

### Documentation (this feature)

```text
specs/phase-1D/007-b-promotions-ux-and-campaigns/
├── plan.md                  # This file
├── research.md              # Phase 0 — lifecycle timer, preview-mode reuse, cross-module event ingestion, in-flight grace contract, audit-diff shape, csv-import idempotency, threshold seeding, optimistic-concurrency edits
├── data-model.md            # Phase 1 — 5 new tables + 2 augmented existing tables, 2 state machines, ERD
├── contracts/
│   └── promotions-ux-and-campaigns-contract.md   # Phase 1 — every coupon + promotion + business-pricing + campaign + preview-profile + approval endpoint, every reason code, every domain event
├── quickstart.md            # Phase 1 — implementer walkthrough, first slice, preview smoke, lifecycle-timer smoke
├── checklists/
│   └── requirements.md      # quality gate (pass)
└── tasks.md                 # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   ├── Shared/                                              # EXTENDED
│   │   ├── ICatalogSkuArchivedSubscriber.cs                 # NEW — event sink for catalog.sku.archived; spec 005 publishes
│   │   ├── ICatalogSkuArchivedPublisher.cs                  # NEW — companion publisher contract; lives in Shared so spec 005 doesn't take a Pricing dependency
│   │   ├── IB2BCompanySuspendedSubscriber.cs                # NEW — event sink for b2b.company.suspended; spec 021 publishes
│   │   ├── IB2BCompanySuspendedPublisher.cs                 # NEW — companion publisher contract
│   │   ├── ICheckoutGraceWindowProvider.cs                  # NEW — read-only API exposing per-rule in_flight_grace_seconds; spec 010 consumes
│   │   ├── CommercialDomainEvents.cs                        # NEW — Coupon{Activated|Expired|Deactivated|Reactivated}, Promotion{Activated|Expired|Deactivated|Reactivated}, Campaign.LinkBroken, Commercial.ThresholdChanged
│   │   └── (existing files unchanged)
│   ├── Pricing/                                             # EXTENDED — no new module
│   │   ├── PricingModule.cs                                 # AMENDED — register new slices, register LifecycleTimerWorker + BrokenReferenceAutoDeactivationWorker, register cross-module subscribers, implement ICheckoutGraceWindowProvider
│   │   ├── Primitives/                                      # NEW or EXTENDED files
│   │   │   ├── LifecycleState.cs                            # NEW — enum: Draft, Scheduled, Active, Deactivated, Expired
│   │   │   ├── LifecycleStateMachine.cs                     # NEW — transition rules + guard predicates; shared by Coupon, Promotion, Campaign
│   │   │   ├── BusinessPricingState.cs                      # NEW — enum: Active, Deactivated
│   │   │   ├── BusinessPricingStateMachine.cs               # NEW
│   │   │   ├── CommercialReasonCode.cs                      # NEW — enum + ICU-key mapper for all 49 owned reason codes (contracts §11)
│   │   │   ├── CommercialActorKind.cs                       # NEW — enum: Operator, B2BAuthor, Approver, SuperAdmin, System
│   │   │   ├── CommercialThresholdPolicy.cs                 # NEW — value-object resolved from commercial_thresholds row
│   │   │   └── HighImpactGate.cs                            # NEW — pure function: (rule, threshold) → bool
│   │   ├── Admin/                                           # EXTENDED
│   │   │   ├── Coupons/                                     # EXISTING folder, EXTENDED
│   │   │   │   ├── CreateCoupon/
│   │   │   │   ├── UpdateCoupon/
│   │   │   │   ├── ScheduleCoupon/
│   │   │   │   ├── DeactivateCoupon/
│   │   │   │   ├── ReactivateCoupon/
│   │   │   │   ├── CloneAsDraft/
│   │   │   │   ├── ListCoupons/
│   │   │   │   └── GetCoupon/
│   │   │   ├── Promotions/                                  # EXISTING folder, EXTENDED — same slice shape as Coupons
│   │   │   ├── BusinessPricing/                             # NEW
│   │   │   │   ├── EditTierRow/
│   │   │   │   ├── EditCompanyOverride/
│   │   │   │   ├── BulkImportTierRows/                      # CSV preview + commit
│   │   │   │   ├── DeactivateBusinessPricingRow/
│   │   │   │   ├── ReactivateBusinessPricingRow/
│   │   │   │   └── ListBusinessPricingRows/
│   │   │   ├── Campaigns/                                   # NEW
│   │   │   │   ├── CreateCampaign/
│   │   │   │   ├── UpdateCampaign/
│   │   │   │   ├── ScheduleCampaign/
│   │   │   │   ├── DeactivateCampaign/
│   │   │   │   └── ListCampaigns/
│   │   │   ├── PreviewProfiles/                             # NEW
│   │   │   │   ├── UpsertPreviewProfile/
│   │   │   │   ├── PromoteToShared/                         # commercial.approver / super_admin only
│   │   │   │   └── ListPreviewProfiles/
│   │   │   ├── Preview/                                     # NEW — the universal preview tool
│   │   │   │   └── PreviewPriceExplanation/
│   │   │   ├── CommercialApprovals/                         # NEW
│   │   │   │   ├── ListPendingApprovals/
│   │   │   │   ├── RecordApproval/                          # creates row + activates the gated rule
│   │   │   │   └── RejectApproval/
│   │   │   ├── CommercialThresholds/                        # NEW
│   │   │   │   ├── GetThresholds/
│   │   │   │   └── UpdateThresholds/                        # super_admin only
│   │   │   └── Lookups/                                     # NEW — for the spec 015 admin pickers
│   │   │       ├── SearchSkus/                              # consumes spec 005 catalog
│   │   │       ├── SearchCompanies/                         # consumes spec 021 b2b
│   │   │       ├── SearchSegments/                          # consumes spec 019 admin-customers
│   │   │       └── SearchCampaignsForBanners/               # consumed by spec 024 cms
│   │   ├── Workers/                                         # NEW (this spec adds 2)
│   │   │   ├── LifecycleTimerWorker.cs                      # 60 s tick; flips Scheduled → Active and Active → Expired; advisory-lock guarded
│   │   │   └── BrokenReferenceAutoDeactivationWorker.cs     # daily; auto-deactivates rules ≥ 7 days broken
│   │   ├── Subscribers/                                     # NEW
│   │   │   ├── CatalogSkuArchivedHandler.cs                 # marks applies_to_broken on referencing rows
│   │   │   ├── B2BCompanySuspendedHandler.cs                # marks company_link_broken on referencing rows
│   │   │   └── CampaignLinkBrokenWatcher.cs                 # subscribes to coupon/promotion lifecycle events; flips link_broken on campaigns
│   │   ├── Authorization/
│   │   │   └── CommercialPermissions.cs                     # NEW — commercial.operator, commercial.b2b_authoring, commercial.approver, commercial.threshold_admin
│   │   ├── Entities/                                        # EXTENDED
│   │   │   ├── Coupon.cs                                    # AMENDED — lifecycle columns, vendor_id, display_in_banners
│   │   │   ├── Promotion.cs                                 # AMENDED — lifecycle columns, vendor_id, banner_eligible, applies_to_broken
│   │   │   ├── ProductTierPrice.cs                          # AMENDED — company_id?, copied_from_tier_id?, vendor_id, company_link_broken
│   │   │   ├── Campaign.cs                                  # NEW
│   │   │   ├── CampaignLink.cs                              # NEW
│   │   │   ├── PreviewProfile.cs                            # NEW (with visibility, created_by)
│   │   │   ├── CommercialThreshold.cs                       # NEW (per market)
│   │   │   ├── CommercialApproval.cs                        # NEW
│   │   │   └── CommercialAuditEvent.cs                      # NEW (denormalized diff cache)
│   │   ├── Persistence/
│   │   │   ├── PricingDbContext.cs                          # AMENDED — add new DbSets
│   │   │   ├── Configurations/                              # IEntityTypeConfiguration<T> per new entity + amended Coupon/Promotion/ProductTierPrice configs
│   │   │   └── Migrations/                                  # 3 net-new migrations: AddLifecycleColumnsToCouponsAndPromotions, ExtendProductTierPricesForCompanyOverrides, AddCommercialAuthoringTables
│   │   ├── Messages/
│   │   │   ├── pricing.commercial.en.icu                    # NEW — operator-visible reason codes EN
│   │   │   ├── pricing.commercial.ar.icu                    # NEW — operator-visible reason codes AR (editorial-grade)
│   │   │   └── AR_EDITORIAL_REVIEW.md                       # NEW — tracked AR keys pending editorial sign-off
│   │   └── Seeding/                                         # EXTENDED
│   │       ├── PricingThresholdsSeeder.cs                   # NEW — seeds commercial_thresholds rows (KSA + EG; idempotent; runs in Dev+Staging+Prod)
│   │       └── PromotionsV1DevSeeder.cs                     # NEW — synthetic coupons/promos/business-pricing/campaigns spanning all states (Dev+Staging only, SeedGuard)
└── tests/
    └── Pricing.Tests/                                       # EXISTING test project, EXTENDED
        ├── Unit/                                            # lifecycle state machine, business-pricing state machine, high-impact gate, threshold-policy resolution, reason-code mapper
        ├── Integration/                                     # WebApplicationFactory + Testcontainers Postgres; every authoring slice; concurrency guard; preview p95; lifecycle timer behavior with FakeTimeProvider; cross-module event subscriber tests; broken-reference auto-deactivation
        └── Contract/                                        # asserts every Acceptance Scenario from spec.md against live handlers
```

**Structure Decision**: Extend the existing `Modules/Pricing/` module rather than creating a new module. The 007-a engine and the 007-b authoring layer share the exact same domain (the four engine tables), and splitting into `Pricing` + `PricingAuthoring` would force a circular dependency or a duplicate persistence layer. The `Admin/` folder already exists with thin coupon/promotion endpoints from 007-a; this spec replaces those thin endpoints with the full lifecycle slices and adds the four new top-level admin folders (`BusinessPricing`, `Campaigns`, `PreviewProfiles`, `CommercialApprovals`). Cross-module event types (`ICatalogSkuArchivedSubscriber`, `IB2BCompanySuspendedSubscriber`, `ICheckoutGraceWindowProvider`, `CommercialDomainEvents`) live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule). The `Admin/Lookups/` sibling holds the picker endpoints consumed by spec 015 / 024, keeping picker concerns separate from authoring concerns.

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `LifecycleState`, `LifecycleStateMachine`, `BusinessPricingState`, `BusinessPricingStateMachine`, `CommercialReasonCode`, `CommercialThresholdPolicy`, `HighImpactGate` | Foundation for all slices |
| B. Persistence + migrations | 3 migrations (additive on Coupons / Promotions / ProductTierPrices + 5 new tables); EF configurations; append-only check on `commercial_audit_events`; verify `ManyServiceProvidersCreatedWarning` still suppressed | Unblocks all slices and workers |
| C. Reference seeder | `PricingThresholdsSeeder` (KSA + EG `gate_enabled=true`, conservative seeded thresholds; idempotent) | Unblocks integration tests + Staging/Prod boot of the gate |
| D. Cross-module shared declarations | `ICatalogSkuArchivedSubscriber/Publisher`, `IB2BCompanySuspendedSubscriber/Publisher`, `ICheckoutGraceWindowProvider`, `CommercialDomainEvents` | Unblocks spec 005 / 021 / 010 / 025 to author their PRs |
| E. Coupon authoring slices | CreateCoupon → UpdateCoupon → ScheduleCoupon → DeactivateCoupon → ReactivateCoupon → CloneAsDraft → ListCoupons → GetCoupon | FR-006–FR-009, FR-001, FR-003, FR-004, FR-005 |
| F. Promotion authoring slices | Same 8 slices; SKU-overlap warning logic | FR-010–FR-012, FR-001, FR-003, FR-004, FR-005 |
| G. Business-pricing slices | EditTierRow → EditCompanyOverride → BulkImportTierRows (preview+commit) → Deactivate/Reactivate → ListBusinessPricingRows | FR-013–FR-016 |
| H. Campaign slices + banner-link lookup | CreateCampaign → UpdateCampaign → ScheduleCampaign → DeactivateCampaign → ListCampaigns → SearchCampaignsForBanners (lookup for spec 024) | FR-017–FR-020 |
| I. PreviewProfile slices + Preview tool | UpsertPreviewProfile → PromoteToShared (gated on `commercial.approver`) → ListPreviewProfiles → PreviewPriceExplanation (calls `IPriceCalculator.Calculate` in Preview mode + delta ribbon) | FR-021–FR-024 |
| J. Approval gate slices | ListPendingApprovals → RecordApproval (with self-approval guard) → RejectApproval; HighImpactGate wired into all activation handlers | FR-025–FR-027 |
| K. Threshold administration | GetThresholds → UpdateThresholds (super_admin); audit on every change | FR-025, P5 |
| L. Lookup endpoints | SearchSkus (consumes spec 005), SearchCompanies (consumes spec 021), SearchSegments (consumes spec 019) | Pickers for spec 015 admin UI |
| M. Cross-module subscribers | CatalogSkuArchivedHandler, B2BCompanySuspendedHandler, CampaignLinkBrokenWatcher | FR-034a, FR-019 |
| N. Workers | LifecycleTimerWorker (per-minute, advisory-lock-guarded), BrokenReferenceAutoDeactivationWorker (daily) | FR-002, FR-034c, SC-005 |
| O. Authorization wiring | `CommercialPermissions.cs` constants + `[RequirePermission]` attributes; spec 015/019 wire role bindings on their PRs | Permission boundary |
| P. Domain events + 025 contract | Publish 8 lifecycle events on each transition; subscribed by spec 025 (lands on 025's PR, not here); deactivation events carry `in_flight_grace_seconds` per FR-003a | FR-032, FR-033 |
| Q. Contracts + OpenAPI | Regenerate `openapi.pricing.commercial.json`; assert contract test suite green; document every reason code | Guardrail #2 |
| R. AR/EN editorial | All operator-visible strings ICU-keyed; AR strings flagged in `AR_EDITORIAL_REVIEW.md` | P4 |
| S. `promotions-v1` dev seeder | `PromotionsV1DevSeeder` (Dev+Staging only, SeedGuard); spans all 5 states for coupons/promotions, 3 tier rows, 2 company overrides, 3 campaigns; bilingual editorial-grade labels | FR-037, SC-008 |
| T. Integration / DoD | Full Testcontainers run; preview p95 load test (SC-002); coupon-uniqueness p95 load test (FR-007); lifecycle timer drift test (SC-005); cross-module event subscriber tests; integrity-scan worker (SC-004); fingerprint; DoD checklist; audit-coverage script (SC-003) | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Extend existing `Modules/Pricing/` rather than create a new `Modules/PricingAuthoring/` | The authoring surface and the engine share the four core tables (Coupon, Promotion, ProductTierPrice, B2BTier). A separate module would force a circular dependency or a duplicate persistence layer; both worse than co-locating. | A separate module doubles the DI seam, the DbContext, and the migration story without separating any domain boundary that meaningfully exists. |
| Single `LifecycleStateMachine` shared by Coupon, Promotion, and Campaign | The four-state contract (`draft → scheduled/active → deactivated ↔ active \| expired`) is identical across the three; encoding it once + parametrizing the entity type avoids triplicate transition-guard tables. | Three separate state machines triple the test surface for invariants that are *constitutionally* identical (FR-001). |
| Two state machines (Lifecycle + BusinessPricing), not one | BusinessPricingRow lifecycle (`active ↔ deactivated`) is genuinely different — no scheduling, no draft, takes effect immediately. Collapsing the two would force a polymorphic state column with permanently-dead transitions. | A single 5-state enum with "BusinessPricing only sees 2 of them" is documentation rot waiting to happen. |
| Lifecycle columns ADDED to existing `pricing.coupons` / `pricing.promotions` (not stored in a sidecar table) | Lifecycle is a property OF the rule, not a separate concern. A sidecar `coupon_lifecycles` table forces every read of a coupon to JOIN to determine its state, breaking the engine's hot path. | A sidecar table privileges audit purity over read performance; the existing 007-a engine reads coupons hundreds of times per minute. |
| `LifecycleTimerWorker` runs every 60 s with advisory lock + idempotent transitions, instead of using Postgres `pg_cron` or external cron | Self-contained inside the application; no external cron dependency; can be horizontally scaled without coordination cost (advisory lock guards). 60 s tolerance matches FR-002 / SC-005. | `pg_cron` is not available on Azure Postgres Flexible Server's default tier; external cron adds an ops surface for a 60 s job. |
| Synchronous preview through `IPriceCalculator.Calculate(ctx)` in Preview mode (rather than a separate "rules-evaluator" preview engine) | Reuses the engine's exact resolution semantics — preview output is byte-identical to runtime output (SC-002 corollary of 007-a's determinism guarantee). A separate preview engine would drift. | A separate preview engine creates two pricing realities; operators would distrust either. |
| Hard-delete forbidden on Coupons / Promotions / Campaigns at the API layer (`405 commercial.row.delete_forbidden`) | FR-005a; preserves historical `PriceExplanation` resolvability + audit traceability indefinitely. | Allowing `super_admin` purge breaks Principle 25 audit and orphans every past order's price explanation. |
| Approval gate is per-rule + per-market with seeded conservative defaults at launch (gate ON), tunable post-launch by `super_admin` | Clarification §Q1 locked: gate ON at launch is the safety-first product call. Per-market + per-criterion tuning is required so KSA and EG can diverge on launch posture. | Gate-OFF-by-default risks GMV from an unintended runaway coupon on day 1; one global threshold across markets cannot reflect EG vs KSA spend volume. |
| `vendor_id` slot reserved on every new and existing in-scope row, but never populated in V1 | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2. Same pattern as specs 020 / 021. | Omitting forces a migration of every entity table when vendor-scoped authoring lands. |
| `pricing.product_tier_prices` extended with `company_id?` (mutually exclusive with `tier_id` non-null via check constraint), instead of a new `pricing.company_overrides` table | The two row kinds resolve to the same engine layer (007-a layer 2 — B2B tier override). Splitting tables would force the engine to UNION two tables on every cart pricing call. | A separate `company_overrides` table doubles the engine's hot-path read cost for an arbitrary author-time distinction. |
| In-flight grace contract carried in deactivation domain events (`in_flight_grace_seconds`) instead of being looked up by spec 010 at gate-evaluation time | Decouples 010 from 007-b's threshold table; spec 010 only needs the event payload to enforce the gate. The spec 010 gate code is correct even when 007-b is unavailable. | Forcing 010 to call `ICheckoutGraceWindowProvider` synchronously on every checkout step adds a hot-path RPC for a value that's already known at deactivation time. |
| `BrokenReferenceAutoDeactivationWorker` runs daily (not on every event), with a 7-day grace per FR-034c | Operators need time to react to upstream archival; auto-deactivating immediately on `catalog.sku.archived` would punish a legitimate SKU re-org with discount-loss. The 7-day window matches typical operator-rotation cadence. | Immediate auto-deactivation creates noisy false positives; never auto-deactivating leaves zombie active rules forever. |
| Bulk-import CSV path uses preview-then-commit (two HTTP calls), not a single dry-run flag | Operators routinely want to see the parsed effect of a 200-row file before any write; collapsing to a flag risks a typo committing rows the operator didn't read. Two calls is one extra round-trip for a high-stakes operation. | A single `?dry_run=true` flag invites accidental commits when the operator forgets the flag. |
| `LifecycleTimerWorker` interval = 60 s (not 1 s, not 5 min) | 60 s is the minimum interval that keeps SC-005's "≤ 60 s drift" achievable without paying for a tighter loop. Any tighter is wasted Postgres pings; any looser breaks the SC. | A 1-s loop is hot-loop overkill for an admin-tier signal. A 5-min loop blows the SC. |
