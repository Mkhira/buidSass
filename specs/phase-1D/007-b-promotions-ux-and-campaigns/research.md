# Research: Promotions UX & Campaigns (Spec 007-b · Phase 1D)

**Date**: 2026-04-28
**Inputs**: spec.md (this directory), plan.md (this directory), specs/phase-1B/007-a-pricing-and-tax-engine/{spec.md,data-model.md,contracts/}, specs/phase-1D/{020-verification, 021-quotes-and-b2b}/research.md, constitution v1.0.0, ADR-001/003/004/010/022/023, project-memory rules.

This document resolves the design unknowns surfaced during plan authoring. Every NEEDS-CLARIFICATION marker that would otherwise have surfaced has been addressed inline below. Each section follows the spec-kit format: **Decision · Rationale · Alternatives considered · Verification hook**.

---

## R1. Lifecycle scheduler — `LifecycleTimerWorker` interval, advisory-lock pattern, drift budget

**Decision**: `LifecycleTimerWorker` ticks every **60 seconds**, gated by Postgres advisory lock `pg_try_advisory_lock(hashtext('pricing.lifecycle_timer'))` to coordinate horizontally across replicas. Each tick runs two idempotent SQL updates inside a single transaction: (1) `UPDATE pricing.coupons SET state='active' WHERE state='scheduled' AND valid_from <= now() AND deleted_at IS NULL`; (2) `UPDATE pricing.coupons SET state='expired' WHERE state IN ('scheduled','active') AND valid_to <= now()`. The same two-statement pattern repeats for `pricing.promotions` and `pricing.campaigns`. Each `UPDATE` writes the audit row through the `SaveChangesInterceptor` (Migration B trigger). Tests use `FakeTimeProvider` and assert drift ≤ 60 s.

**Rationale**: 60 s is the **minimum interval** that satisfies SC-005's "≤ 60 s drift" without paying for a tighter loop. A bulk-update approach (rather than per-row queries) keeps a tick under p95 ≤ 50 ms even with 5 000 in-flight scheduled rows per market. Advisory lock is the established spec 020 / 021 pattern — adding an ops dependency (e.g., Redis distributed lock) for a single 60 s job is unjustified.

**Alternatives considered**:
- **`pg_cron`**: not available on Azure Postgres Flexible Server's default tier (per ADR-010 deployment baseline). Requires a tier upgrade unrelated to this spec.
- **External cron** (Azure Container Apps job): adds a deployment surface for a 60 s heartbeat — over-engineered.
- **Per-row `EXPIRE_AT` timer** (e.g., Quartz.NET): introduces a Quartz dependency for an in-process job; not justified.
- **1-second tick**: 60× the Postgres pings for no SC benefit.
- **5-minute tick**: blows SC-005.

**Verification hook**: integration test `LifecycleTimerWorker_Drift_Within_60s` schedules 100 coupons with `valid_from = now + 30s`, advances `FakeTimeProvider` to `now + 90s`, asserts every row is `active` and audit rows exist.

---

## R2. Preview tool — reusing 007-a's `IPriceCalculator.Calculate(ctx)` Preview mode vs. building a separate preview engine

**Decision**: Reuse the existing 007-a `IPriceCalculator.Calculate(ctx)` in `Preview` mode (007-a FR-012) as the **only** pricing-resolution path for the preview tool. The 007-b handler `PreviewPriceExplanation` builds a `PricingContext` from the operator-provided sample profile, **layers the in-flight (unsaved) rule into the context's read-side as if it had been persisted**, calls the engine, and renders the resulting `PriceResult.explanation[]` plus a delta ribbon (per-line difference vs. a second call without the in-flight rule). No alternative engine is implemented. No `pricing.price_explanations` row is written (Preview mode side-effect-free per 007-a FR-012).

**Rationale**: 007-a guarantees determinism (007-a SC-002: same context → same explanation hash). Re-implementing layer resolution in 007-b would create two pricing realities and inevitably drift. The Preview-mode contract was specifically designed for this consumer.

**Implementation note** (the only non-trivial part): the in-flight rule is fed via a **scoped `IInFlightRuleOverlay`** registered as an `AsyncLocal<>` on the request, consumed by the engine's repository layer. When the overlay is set, the repository returns the in-flight version of the rule for the duration of the call; otherwise the persisted version is returned. This is a 007-a-side change (already specced under 007-a FR-012's `Preview` mode and the `IPriceCalculator` contract); 007-b only consumes it.

**Alternatives considered**:
- **Build a dedicated `PreviewEngine`**: drift risk is unacceptable.
- **Persist the rule, run the engine, then roll back**: defeats audit (a transient row would still appear in the journal); transactional rollback semantics with `IPriceCalculator.Calculate` are not contract-stable.
- **Compute deltas client-side from a serialized rule body**: pushes engine logic into the admin UI, breaking Principle 10.

**Verification hook**: integration test `Preview_Output_Matches_Runtime_For_Same_Profile` saves a coupon, runs the preview against profile P, runs `IPriceCalculator.Calculate` against the same P, asserts `explanation_hash` parity.

---

## R3. Cross-module event ingestion — `catalog.sku.archived` and `b2b.company.suspended`

**Decision**: Declare four contracts in `Modules/Shared/`: `ICatalogSkuArchivedSubscriber` + `ICatalogSkuArchivedPublisher`, `IB2BCompanySuspendedSubscriber` + `IB2BCompanySuspendedPublisher`. Spec 005 (catalog) and spec 021 (b2b) **publish** the events when their respective archive / suspend operations commit. Spec 007-b implements the subscribers in `Modules/Pricing/Subscribers/`. The bus is the same in-process MediatR `INotification` channel used by spec 020's account-lifecycle hooks (research §R13 in spec 020) — no Kafka, no MQ. Idempotency is per-event: each subscriber checks "did we already mark this row broken?" via the `broken_at_utc` column and no-ops if true.

**Rationale**: In-process notifications match the modular-monolith architecture (ADR-023). The Shared interface lives away from the publisher's module so the publisher does not take a Pricing dependency (project-memory rule: cross-module hooks via `Modules/Shared/` to avoid module dependency cycles).

**Alternatives considered**:
- **Outbox + DB-poll**: adds operational surface for an in-process concern.
- **Direct MediatR notification with the publisher in Pricing**: forces spec 005 and 021 to take a Pricing dependency at compile time — circular.
- **Event-sourced cascade**: massively over-engineered for two event kinds.

**Verification hook**: integration test `CatalogSkuArchived_MarksReferencingRulesBroken` emits a `CatalogSkuArchivedEvent` via the fake publisher; asserts every Coupon / Promotion / BusinessPricingRow with that SKU in `applies_to[]` now has `applies_to_broken=true` + queue indicator surfaced.

---

## R4. In-flight grace contract — event payload vs. lookup, default value, market-tunable bounds

**Decision**: In-flight grace lives in **two places**:
1. **At rest**: `pricing.commercial_thresholds` table carries `coupon_in_flight_grace_seconds` and `promotion_in_flight_grace_seconds` per market (default 1800; bound 300–7200 by check constraint).
2. **At event time**: every `Coupon.Deactivated` and `Promotion.Deactivated` domain event carries `in_flight_grace_seconds` as a payload field, populated from the threshold row at deactivation time.

Spec 010 (checkout) consumes the event payload only — it does NOT look up the threshold row at gate-evaluation time. This decouples 010 from 007-b's table on the hot path.

A separate read-only API `ICheckoutGraceWindowProvider.GetGraceSeconds(ruleId)` exists in `Modules/Shared/` for any consumer that needs to query (e.g., a future support tool inspecting a stuck cart); 010's main path does not call it.

**Rationale**: Spec 010's gate code is correct even when 007-b is temporarily unavailable (e.g., during a migration). The threshold update path emits a `Commercial.ThresholdChanged` event so any cached value at the consumer side can be invalidated.

**Alternatives considered**:
- **Lookup at every checkout step**: hot-path RPC for a known-at-deactivation-time value.
- **Hardcoded grace**: violates Principle 5 (market configuration).
- **Per-rule override**: more complexity than the SC-quotient justifies; the per-market default is sufficient.

**Verification hook**: integration test `Coupon_Deactivation_Event_Carries_Grace_Seconds_From_Market_Threshold` deactivates a coupon under each market, asserts the event payload's `in_flight_grace_seconds` matches the threshold row.

---

## R5. Audit-diff payload — denormalized cache vs. on-demand reconstruction

**Decision**: Each lifecycle / authoring write emits **two** audit artifacts:
1. **Canonical audit row** via `IAuditEventPublisher` into the shared `audit_log_entries` table (spec 003) — actor, timestamp, kind, entity ref, structured metadata.
2. **Denormalized diff row** in a 007-b-owned `pricing.commercial_audit_events` table — full `before` / `after` JSON snapshot of the entity, plus the field-level diff list. Append-only (Postgres `BEFORE UPDATE OR DELETE` trigger blocking modifications).

The admin UI reads from `commercial_audit_events` for fast field-by-field "who changed what" rendering; the global audit log reads from `audit_log_entries`. Both are written in the same transaction as the entity write.

**Rationale**: SC-003 requires 100 % audit coverage with field-level diffs. Reconstructing diffs on-demand from the global audit log would force an O(n) scan of all writes for a given entity — slow at scale. The denormalized cache is small (one row per write, no order growth) and pays its keep on every operator review.

**Alternatives considered**:
- **Use the global audit log only**: slow read on the admin "history" tab; would need a secondary index just for this consumer.
- **Skip the denormalized table; compute diffs client-side**: shifts logic into the UI; breaks the contract guarantee that the diff is byte-stable across renderers.

**Verification hook**: contract test `AuditCoverage_Script_OK` runs the spec-015 audit-coverage script over a 100-action operator session and asserts 100 % of actions have a matching `commercial_audit_events` row + matching `audit_log_entries` row.

---

## R6. Coupon-uniqueness check — case-insensitive index strategy, p95 budget

**Decision**: Enforce coupon code uniqueness via a Postgres unique index on `UPPER(code)` (a functional index). The form-blur uniqueness check uses `SELECT 1 FROM pricing.coupons WHERE UPPER(code) = UPPER($1) LIMIT 1`. The query is rate-limited to 60 / min / actor to prevent code-enumeration. p95 ≤ 200 ms (FR-007) is met because the functional index is in-memory after the first call.

**Rationale**: Functional indexes on Postgres are first-class citizens; they avoid storing a duplicate `code_canonical` column and keep INSERT cost low. Case-insensitive match without normalization at write time would require a `LOWER()` scan — index-only with the functional index pattern.

**Alternatives considered**:
- **Store `code_canonical` column** (denormalized uppercase) + plain unique index: adds a write-time concern + a constraint that the two columns stay in sync.
- **Use Postgres `citext` extension**: introduces an extension dependency; minimal benefit over functional index.

**Verification hook**: unit test `Coupon_Code_Uniqueness_Is_Case_Insensitive` inserts `WELCOME10`; attempts to insert `welcome10`; asserts `unique_violation` and reason code `coupon.code.duplicate`.

---

## R7. Bulk CSV import — preview-then-commit, idempotency, column casing

**Decision**: Two-step API:
1. `POST /v1/admin/commercial/business-pricing/bulk-import/preview` — accepts the CSV body, returns a parsed-effect report with `would_insert[]`, `would_update[]`, `would_skip[]`, `rejected[]` (with per-row error reason). No write. Returns a `preview_token` valid for 15 minutes.
2. `POST /v1/admin/commercial/business-pricing/bulk-import/commit` — accepts `{preview_token, idempotency_key}`. If the rows have not changed since the preview snapshot, commits in one transaction; otherwise returns `409 commercial.bulk_import.snapshot_changed` with a fresh preview embedded.

**Column casing** (the open item from clarification): **strict snake_case headers** (`tier_code`, `sku`, `net_minor`, `markets`). No alias support, no case-insensitive header parse — operators paste from a documented template. Invalid headers fail-fast with `commercial.bulk_import.invalid_header`.

**Rationale**: Two-step is documented in the plan's Complexity Tracking. Strict snake_case avoids an entire class of "did the operator mean `Net_Minor` or `net_minor`?" support tickets. The 15-minute preview token window prevents stale-preview commits.

**Alternatives considered**:
- **Single-call `?dry_run=true` flag**: documented as rejected in the plan's Complexity Tracking.
- **Case-insensitive header parse**: invites typos with confusing error messages.
- **CamelCase / kebab-case**: unconventional in the existing seed framework.

**Verification hook**: integration test `BulkImport_PreviewToken_Expires_After_15min`; integration test `BulkImport_Strict_SnakeCase_Headers_Required`.

---

## R8. Threshold seeding — gate ON at launch, conservative defaults per market

**Decision**: `PricingThresholdsSeeder` runs in **all environments** (Dev / Staging / Production) and is idempotent. Seeded values per market:

| Market | `gate_enabled` | `threshold_percent_off` | `threshold_amount_off_minor` | `threshold_duration_days` | `coupon_in_flight_grace_seconds` | `promotion_in_flight_grace_seconds` |
|---|---|---|---|---|---|---|
| KSA (`SA`) | `true` | `30` | `5_000_000` (= SAR 50 000 = 5 000 000 fils) | `14` | `1800` | `1800` |
| EG (`EG`) | `true` | `30` | `25_000_000` (= EGP 250 000 = 25 000 000 piasters) | `14` | `1800` | `1800` |

Seeder upserts on `(market_code)` primary key; never overwrites existing rows in Production (gated by spec 003's `SeedGuard`-equivalent `--mode=apply --idempotent` semantics — the existing seed framework's contract).

**Rationale**: Clarification §Q1 locks the values. Per-market separation is required by Principle 5. Idempotency lets the seeder run on every deploy without risk.

**Alternatives considered**:
- **Hardcode the values in code, no DB row**: violates Principle 5 (no market-aware tunability without a code deploy).
- **Different defaults per environment** (e.g., gate OFF in Dev): operators training in Dev would never encounter the gate, then be surprised in Staging.

**Verification hook**: integration test `Thresholds_Seeded_For_Both_Markets_GateOn` after seeder run; integration test `Thresholds_Seeder_Is_Idempotent` runs the seeder twice and asserts no duplicate rows / no audit-row noise.

---

## R9. Optimistic-concurrency for concurrent operator edits

**Decision**: Every entity in scope (`Coupon`, `Promotion`, `Campaign`, `BusinessPricingRow`, `PreviewProfile`, `CommercialApproval`) carries a `xmin`-mapped `IsRowVersion()` column (named `row_version` in EF, materialized as Postgres `xmin` system column via `[Timestamp]` mapping). Every mutation handler accepts the client's `If-Match: <row_version>` header (or `version` field in the request body); mismatch → `409 commercial.row.version_conflict` with the current row body embedded for client-side merge.

**Rationale**: Pattern is established in specs 008 / 020 / 021. EF Core's `IsRowVersion()` natively maps `xmin`; no extra column write cost.

**Alternatives considered**:
- **Last-write-wins**: silent data loss; unacceptable for audited rules.
- **Pessimistic lock via `SELECT ... FOR UPDATE`**: holds locks across the operator's typing session; unacceptable.

**Verification hook**: integration test `TwoOperators_EditingSameRule_SecondSaveReceives_409`.

---

## R10. Reason-code surface — namespace strategy, count, ICU keys

**Decision**: All operator-visible reason codes live in the `commercial.*` namespace (e.g., `commercial.row.version_conflict`, `commercial.deactivation.reason_required`) plus the `coupon.*`, `promotion.*`, `campaign.*`, `business_pricing.*`, `pricing.target.*` sub-namespaces for entity-specific codes. Each code has an ICU key in both `pricing.commercial.en.icu` and `pricing.commercial.ar.icu`. Total count after the spec lock: **32 codes** enumerated in the contract (§9).

**Rationale**: Namespace consistency follows specs 020 / 021. ICU keys allow MessageFormat.NET interpolation per locale.

**Alternatives considered**: a single flat namespace — rejected for grep-ability.

**Verification hook**: contract test `EveryReasonCode_HasIcuKey_InBothLocales`.

---

## R11. SKU / company / segment picker performance budgets

**Decision**: All four lookup endpoints (`SearchSkus`, `SearchCompanies`, `SearchSegments`, `SearchCampaignsForBanners`) page-cap at 200 results, support `q` substring + `cursor` pagination, and consume the upstream module's existing search index (Meilisearch for SKUs per ADR-005; Postgres trigram for companies + segments). p95 ≤ 300 ms for the SKU picker against a 50 000-SKU catalog (SC-006); p95 ≤ 200 ms for company + segment pickers.

**Rationale**: Reuses existing search infrastructure; no new index introduced.

**Alternatives considered**: build a 007-b-owned secondary index — unjustified.

**Verification hook**: load test `SkuPicker_p95_300ms_at_50k_skus`.

---

## R12. Approval-gate concurrency — preventing race-around the self-approval guard

**Decision**: The self-approval guard is enforced at **two layers**:
1. **Authorization filter**: the `RecordApproval` handler rejects with `403 commercial.self_approval.forbidden` if `current_actor_id == draft.created_by`.
2. **Domain check inside the transaction**: the `CommercialApproval` row carries a unique constraint `(target_entity_id, target_entity_kind)` so the second concurrent approval call against the same draft fails with `commercial.approval.already_recorded` rather than producing two approval rows.

**Rationale**: Layer 1 catches the common case; layer 2 catches the rare race (two approvers click Approve simultaneously) and ensures the audit trail is unambiguous about which approval activated the rule.

**Alternatives considered**: a Redis-backed mutex per draft — over-engineered.

**Verification hook**: concurrency test `TwoApprovers_ClickingApproveSimultaneously_OneRowOneActivation`.

---

## R13. `BrokenReferenceAutoDeactivationWorker` — eligibility heuristic, edge cases

**Decision**: Daily worker at 02:00 UTC (configurable). Eligibility: a row is auto-deactivated if **(a)** state is `active`, AND **(b)** the broken-reference indicator was set ≥ 7 days ago, AND **(c)** every reference in `applies_to[]` (or the `company_id` for BusinessPricingRow) is currently broken (i.e., the row has no live targets). Worker writes the same `Deactivate` audit row that an operator would, with `actor_id='system'` and `reason_note="auto_deactivated:broken_references"`. The deactivation event is published with the configured `in_flight_grace_seconds`.

**Rationale**: 7-day grace per FR-034c. The "every reference broken" condition prevents auto-deactivating a 100-SKU promotion when one SKU was archived.

**Alternatives considered**:
- **Auto-deactivate on first broken reference**: punishes legitimate SKU re-orgs.
- **Never auto-deactivate**: leaves zombie rules forever.

**Verification hook**: integration test `BrokenReferenceAutoDeactivation_Deactivates_AfterAllRefsBrokenFor7Days`; negative test `BrokenReferenceAutoDeactivation_Skips_When_AnyRefStillLive`.

---

## R14. EF Core warning suppression and DI scope (project-memory rule)

**Decision**: The existing `PricingModule.cs` already calls `AddDbContext<PricingDbContext>(o => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)))` per the project-memory rule. This spec MUST verify the suppression remains in place after Phase B migration work (it's easy to lose during a refactor). The suppression is required because Identity tests boot multiple service providers via `WebApplicationFactory<>`.

**Rationale**: Project-memory rule.

**Alternatives considered**: none — the rule is binding.

**Verification hook**: build-time grep in CI checking the suppression line is present in `PricingModule.cs`.

---

## R15. `applies_to_broken` vs. queue indicator vs. auto-deactivation — three-tier signaling

**Decision**: When a SKU is archived, the row's lifecycle does **not** change. Instead:
1. **Day 0**: `applies_to_broken=true` is set; the row appears in the operator queue with a yellow "needs review" badge. The engine continues to honor the rule for any unbroken target SKUs.
2. **Day 7**: if no operator action, `BrokenReferenceAutoDeactivationWorker` flips state to `deactivated` (only if every reference is broken, per R13).
3. **Engine output**: throughout the 7-day window, the rule's resolved `PriceExplanation` rows include a `pricing.target.archived` advisory row per affected line, so support / finance can trace.

**Rationale**: Three-tier signaling balances operator autonomy (day 0 alert) with safety (day 7 auto-deactivate) without breaking pricing on day 1.

**Alternatives considered**: documented in plan's Complexity Tracking.

**Verification hook**: integration test `Archived_SKU_Surfaces_Advisory_In_PriceExplanation_During_Grace`.

---

## R16. Module placement — extend `Modules/Pricing/` vs. new module

**Decision**: Extend `Modules/Pricing/`. See plan §Project Structure and Complexity Tracking row 1 for full rationale.

**Verification hook**: directory tree review during PR.

---

## R17. AR editorial review surface

**Decision**: Every customer-visible string (coupon labels, promotion labels, campaign names, descriptions seeded by `PromotionsV1DevSeeder`) MUST appear in `services/backend_api/Modules/Pricing/Messages/AR_EDITORIAL_REVIEW.md` flagged for review. The PR template adds a checkbox "AR strings reviewed by editorial" — same workflow as specs 020 / 021.

**Rationale**: Principle 4 — editorial-grade Arabic, not machine-translated. SC-007 verifies.

**Alternatives considered**: skipping editorial review — non-compliant.

**Verification hook**: PR-time human review; no automated test.

---

## R18. OpenAPI artifact convention

**Decision**: One artifact per module sub-domain: `services/backend_api/openapi.pricing.commercial.json` (regenerated on every PR via the existing `dotnet swagger tofile` task; checked in for review diffs). Same convention as `openapi.b2b.json` (spec 021).

**Verification hook**: PR diff against the regenerated file.

---

## R19. `PreviewProfile` storage of cart lines — dehydrated SKU references vs. embedded snapshots

**Decision**: Store `cart_lines[]` as a JSONB column with `[{sku, qty, restricted}]` shape. SKU is a foreign-key reference (NOT enforced at DB level — JSONB doesn't support FK; enforced at write time by the handler). On preview run, the SKU is resolved through the Catalog read repository; if the SKU is archived, the preview surfaces a `preview.profile.sku_archived` advisory and the operator may regenerate the profile.

**Rationale**: SKUs in profiles are a long-tail ergonomics concern; no need for FK enforcement. JSONB keeps the schema simple.

**Alternatives considered**: a `preview_profile_lines` join table — over-engineered for ≤ 50 lines per profile.

**Verification hook**: integration test `Preview_With_Archived_SKU_In_Profile_Surfaces_Advisory`.

---

## Open items deferred (with justification)

- **CSV column header internationalization**: the bulk-import template ships in English-only column headers (`tier_code`, `sku`, `net_minor`, `markets`). Operators in EG / KSA author the CSV against the English template. Justification: column headers are operator-tooling, not customer-facing; localizing them would force an Arabic-RTL CSV-parser path that no commercial operator has requested. Revisit in Phase 1.5 if operators ask.
- **Per-rule grace-window override** (i.e., specifying a custom in-flight-grace on a single coupon): deferred. Per-market default is sufficient at V1; per-rule override adds a column + a UI control for an unproven need.
- **Audit log retention beyond `commercial_audit_events`**: spec 015 admin-foundation owns retention; this spec only writes. If retention shrinks, the diff cache shrinks; the global audit log retains the full history.
- **Soft-delete on PreviewProfile** (rather than hard-delete): not adopted. PreviewProfiles are operator-personal scratchpads; their loss has no audit consequence.
- **Coupon-bulk-code-generation**: explicit Out of Scope per spec.md.
- **Customer coupon-wallet UI**: explicit Out of Scope per spec.md.

---

## Cross-spec consistency checks

- **Spec 020 alignment**: lifecycle `valid_to` semantic (immediate, no grace) matches spec 020's `verification.expired` semantic. Account-lifecycle hook used by spec 021 is NOT consumed here — 007-b has no per-customer state.
- **Spec 021 alignment**: `b2b.company.suspended` event name and payload shape match spec 021's research §R13.
- **Spec 010 alignment**: deactivation event payload includes `in_flight_grace_seconds` for spec 010 to consume; spec 010's checkout re-validation gate is the consumer of record (research §R4 above).
- **Spec 005 alignment**: `catalog.sku.archived` event name matches spec 005's catalog-archival contract (under FR-034b above, spec 005 also commits to refusing hard-delete of a referenced SKU).
- **Spec 007-a alignment**: no engine code is modified. Preview mode (007-a FR-012) is the only engine consumer here. The four engine tables get additive columns only.
- **ADR-022** (PostgreSQL 16): functional index on `UPPER(code)` is a PG ≥ 12 feature — fine.
