# Tasks: Pricing & Tax Engine (007-a)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data Model**: [data-model.md](./data-model.md) | **Contracts**: [contracts/pricing.openapi.yaml](./contracts/pricing.openapi.yaml), [contracts/events.md](./contracts/events.md)

**Feature module root**: `services/backend_api/Features/Pricing/`
**Test projects**: `Tests/Pricing.Unit/`, `Pricing.Properties/`, `Pricing.Goldens/`, `Pricing.Integration/`, `Pricing.Contract/`
**Shared contracts**: `packages/shared_contracts/pricing/`

User stories (priority order from `spec.md` §10):
- **US1** Deterministic basket resolution (P1)
- **US2** Promotion stacking + exclusion (P1)
- **US3** Coupon redemption accounting (P1)
- **US4** VAT computation parity (P1)
- **US5** Business + tier pricing for B2B (P1)
- **US6** Price token resolution (P1)
- **US7** Audit on authored changes (P2)
- **US8** Observability + rate safety (P2)

**MVP** = Phases 1 + 2 + 3–8 (US1–US6). US7–US8 ship in the same PR train but can land after MVP.

---

## Phase 1 — Setup

- [ ] T001 Create feature module skeleton at `services/backend_api/Features/Pricing/` with subfolders `Resolve/`, `Pipeline/`, `Promotions/`, `Coupons/`, `BusinessPricing/`, `TierPricing/`, `Tax/`, `Tokens/`, `Money/`, `Snapshots/`, `Observability/`, `Persistence/`, `Shared/`
- [ ] T002 Add test projects `Tests/Pricing.Unit/`, `Pricing.Properties/`, `Pricing.Goldens/`, `Pricing.Integration/`, `Pricing.Contract/` with xUnit + FluentAssertions + FsCheck.xUnit
- [ ] T003 [P] Add NuGet refs: `FsCheck.Xunit`, `NodaTime`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.Extensions.Caching.Memory`, `Testcontainers.PostgreSql`
- [ ] T004 [P] Register module via `AddPricingModule()` extension in `Features/Pricing/PricingModuleExtensions.cs` wired from `Program.cs`
- [ ] T005 [P] Add `Pricing` config section to `appsettings.json` + `.Development.json` (`TokenSigningKey`, `TokenSigningKeyPrevious`, `TokenTtlSeconds=120`, `ResolveBasketMaxLines=200`, `RateLimitPerMinute=600`)

## Phase 2 — Foundational

- [ ] T006 Create `Features/Pricing/Persistence/PricingDbContext.cs` with entity configurations for every table in `data-model.md` §1–§7, separate from catalog/identity contexts
- [ ] T007 Create migration `V007_001__create_pricing_schema.sql` covering all 7 authored tables, redemption, snapshots, `*_history` tables, and triggers for history capture
- [ ] T008 Create migration `V007_002__seed_default_tax_rules.sql` seeding KSA 15%, EG 14%, zero-rated 0% (see data-model §6)
- [ ] T009 [P] Implement `Features/Pricing/Money/Money.cs` value object `(long MinorUnits, CurrencyCode)` per research R1; forbids implicit decimal conversion
- [ ] T010 [P] Implement `Features/Pricing/Money/Rate.cs` basis-points type with `Apply(Money)` returning distributed result per R1/R2
- [ ] T011 [P] Implement `Features/Pricing/Money/BankersRounding.cs` + `DistributedAllocation<Money>` ensuring sum-of-parts equals whole after rounding
- [ ] T012 [P] Implement `Features/Pricing/Shared/PricingErrorCodes.cs` enumerating `pricing.*` codes with HTTP status mapping (per OpenAPI)
- [ ] T013 [P] Implement `Features/Pricing/Shared/` DTOs mirroring the OpenAPI schemas (`ResolveBasketRequest`, `LinePricingBreakdown`, `BasketPricingBreakdown`, `BreakdownTraceEntry`, `CouponValidationError`, `ErrorEnvelope`)
- [ ] T014 [P] Implement `Features/Pricing/Promotions/PredicateEvaluator.cs` (v1 schema per R4) with unit-test harness covering all predicate kinds
- [ ] T015 [P] Implement `Features/Pricing/Tokens/PriceTokenIssuer.cs` + `PriceTokenVerifier.cs` with HMAC-SHA256 and rotating-key support per R8
- [ ] T016 Implement `Features/Pricing/Pipeline/PricingContext.cs` (immutable) + `IPricingStage` interface per R3
- [ ] T017 [P] Wire RBAC policies (`pricing.read`, `pricing.write.promo`, `pricing.write.coupon`, `pricing.write.business`, `pricing.write.tax`, `pricing.audit`) via spec 004 authorization plumbing in `Features/Pricing/Shared/PricingAuthorizationPolicies.cs`
- [ ] T018 [P] Publish OpenAPI to `packages/shared_contracts/pricing/pricing.openapi.yaml` (copy-of-truth) and wire contract-diff CI gate
- [ ] T019 Implement in-memory caches (`PromotionCache`, `TaxRuleCache`) with write-through invalidation per R5/R6

## Phase 3 — US1: Deterministic Basket Resolution (P1)

- [ ] T020 [US1] Implement pipeline stage `Features/Pricing/Pipeline/Stages/BasePriceStage.cs` — loads variant base price + compare-at + tax_class from catalog read model
- [ ] T021 [US1] Implement pipeline stage `NetTotalStage.cs` — computes line nets and basket subtotal (post-discount, pre-tax)
- [ ] T022 [US1] Implement pipeline stage `FinalTotalStage.cs` — basket total = sum(line totals) per R2
- [ ] T023 [US1] Implement `Features/Pricing/Resolve/ResolveBasketHandler.cs` MediatR handler composing all 9 stages in fixed order
- [ ] T024 [US1] Implement endpoint `POST /pricing/resolve-basket` in `Features/Pricing/PricingEndpoints.cs` with FluentValidation (max 200 lines, market required)
- [ ] T025 [P] [US1] Unit tests in `Tests/Pricing.Unit/Pipeline/PipelineDeterminismTests.cs` — identical inputs produce byte-identical breakdowns excluding correlation id
- [ ] T026 [P] [US1] Integration test `Tests/Pricing.Integration/Resolve/BasketResolutionFlowTests.cs` using Testcontainers Postgres seeded with catalog + variants
- [ ] T027 [P] [US1] k6 perf script `scripts/perf/pricing-basket.js` asserting p95 ≤ 250 ms @ 50 lines (SC-002)

## Phase 4 — US2: Promotion Stacking + Exclusion (P1)

- [ ] T028 [US2] Implement `Features/Pricing/Pipeline/Stages/PromotionStage.cs` — loads active promos from cache, evaluates predicates, applies in priority order respecting stackable + exclusion_flag
- [ ] T029 [US2] Implement percentage + fixed primitive evaluators in `Features/Pricing/Promotions/Evaluators/`
- [ ] T030 [US2] Implement BOGO evaluator (`BogoEvaluator.cs`) — buy-N-get-M-same-variant or variant-group with explicit `free_line` discount entries
- [ ] T031 [US2] Implement bundle evaluator (`BundleEvaluator.cs`) — requires all component variants at min qty; emits single bundle discount amortised across component lines
- [ ] T032 [US2] Implement Promotion state machine in `Features/Pricing/Promotions/PromotionStateMachine.cs` enforcing transitions per §6.2
- [ ] T033 [P] [US2] Admin CRUD endpoints `GET|POST|PUT|DELETE /admin/pricing/promotions[/{id}]` in `Features/Pricing/Promotions/PromotionAdminEndpoints.cs` with RBAC + state transitions
- [ ] T034 [P] [US2] Unit tests `Tests/Pricing.Unit/Promotions/StackingTests.cs` — stackable multiply, exclusion_flag halts, priority ordering, paused promo skipped
- [ ] T035 [P] [US2] Golden-file fixtures `Tests/Pricing.Goldens/fixtures/promo-stacking/ksa/*.json` + `/eg/*.json` (12 cases) + driver test `PromoStackingGoldenTests.cs` — byte-for-byte breakdown match

## Phase 5 — US3: Coupon Redemption Accounting (P1)

- [ ] T036 [US3] Implement `Features/Pricing/Pipeline/Stages/CouponStage.cs` — validation only (R7), never writes redemption
- [ ] T037 [US3] Implement coupon validator `Features/Pricing/Coupons/CouponValidator.cs` — runs window, cap, eligibility, min-basket, segment checks; returns structured `CouponValidationError[]`
- [ ] T038 [US3] Implement `Features/Pricing/Resolve/ValidateCouponHandler.cs` for dry-run endpoint `POST /pricing/validate-coupon`
- [ ] T039 [US3] Implement `Features/Pricing/Coupons/CommitRedemptionHandler.cs` for `POST /pricing/coupons/{id}/commit-redemption` — idempotent via unique `(coupon_id, basket_id)` index
- [ ] T040 [US3] Implement Coupon state machine in `CouponStateMachine.cs` including `exhausted` transition when `usage_cap_total` hits
- [ ] T041 [P] [US3] Admin CRUD endpoints for coupons in `Features/Pricing/Coupons/CouponAdminEndpoints.cs`
- [ ] T042 [P] [US3] Unit tests `Tests/Pricing.Unit/Coupons/CouponValidatorTests.cs` covering all 9 validation error codes
- [ ] T043 [P] [US3] Integration test `Tests/Pricing.Integration/Coupons/RedemptionCommitTests.cs` — idempotency on duplicate commit, per-customer cap enforcement

## Phase 6 — US4: VAT Computation Parity (P1)

- [ ] T044 [US4] Implement pipeline stage `Features/Pricing/Pipeline/Stages/TaxStage.cs` — resolves tax rule per line via `TaxRuleCache`, applies `rate_basis_points`, writes per-line VAT entries
- [ ] T045 [US4] Implement `Features/Pricing/Tax/TaxRuleResolver.cs` with fail-fast when no active rule exists (R6)
- [ ] T046 [US4] Admin endpoints `GET|PUT /admin/pricing/tax/{marketCode}` in `Features/Pricing/Tax/TaxAdminEndpoints.cs` with cache invalidation on write
- [ ] T047 [P] [US4] Golden-file fixtures `Tests/Pricing.Goldens/fixtures/vat/ksa/*.json` (25 cases) + `/eg/*.json` (25 cases) + driver test `VatGoldenTests.cs` — exact minor-unit match (SC-005)
- [ ] T048 [P] [US4] Integration test `Tests/Pricing.Integration/Tax/TaxRuleChangeTests.cs` — update rule, verify next resolution uses new rate within 1 s (SC-007)

## Phase 7 — US5: Business + Tier Pricing for B2B (P1)

- [ ] T049 [US5] Implement pipeline stage `Features/Pricing/Pipeline/Stages/BusinessPricingStage.cs` — looks up by `(company_id, variant_id)` then `(company_id, category_id)`; sets skip-flag for tier stage on match
- [ ] T050 [US5] Implement pipeline stage `TierPricingStage.cs` — skipped if business stage matched; otherwise picks highest-threshold tier ≤ qty
- [ ] T051 [US5] Admin endpoints `GET|PUT /admin/pricing/business/{companyId}/{variantId}` and `GET|PUT /admin/pricing/tier/{variantId}` in respective admin endpoints files with exclusion-constraint violations surfaced as `pricing.concurrent_write_conflict`
- [ ] T052 [P] [US5] Unit tests `Tests/Pricing.Unit/Pipeline/B2bPrecedenceTests.cs` — business beats tier, tier applied when business absent, neither applied when below threshold
- [ ] T053 [P] [US5] Integration test `Tests/Pricing.Integration/B2b/CompanyPricingFlowTests.cs` seeding a company + variants

## Phase 8 — US6: Price Token Resolution (P1)

- [ ] T054 [US6] Implement `Features/Pricing/Resolve/ResolveTokenHandler.cs` — verify signature + TTL via `PriceTokenVerifier`, run pipeline for single line, return `LinePricingBreakdown`
- [ ] T055 [US6] Endpoint `POST /pricing/resolve-token` with 410 mapping for expired tokens, 400 for invalid signature
- [ ] T056 [P] [US6] Unit tests `Tests/Pricing.Unit/Tokens/PriceTokenTests.cs` — sign + verify roundtrip, TTL expiry, rotating-key acceptance (current + previous), forged signature rejection
- [ ] T057 [P] [US6] k6 perf script `scripts/perf/pricing-token.js` asserting p95 ≤ 100 ms (SC-003)

## Phase 9 — US7: Audit on Authored Changes (P2)

- [ ] T058 [US7] Implement `Features/Pricing/Observability/AuditEventEmitter.cs` writing to `audit_events` (spec 003) with before/after diffs
- [ ] T059 [US7] Wire emitter into all admin-write handlers (promotions, coupons, business, tier, tax) via MediatR pipeline behavior `AuditingBehavior.cs`
- [ ] T060 [US7] Implement `Features/Pricing/Snapshots/PricingSnapshotBuilder.cs` — pure `BuildBreakdown(context) → BreakdownJson` (spec 011 calls at order placement)
- [ ] T061 [US7] Admin endpoint `POST /admin/pricing/resolve-debug` using trigger-backed `*_history` tables for historical replay (R11); handler in `Features/Pricing/Resolve/ResolveDebugHandler.cs`
- [ ] T062 [P] [US7] Integration test `Tests/Pricing.Integration/Audit/AuditCoverageTests.cs` — every admin write produces an `audit_events` row (SC-006)
- [ ] T063 [P] [US7] Integration test `Tests/Pricing.Integration/Audit/DebugReplayTests.cs` — change a tax rule, replay a past instant, verify old rate returned

## Phase 10 — US8: Observability + Rate Safety (P2)

- [ ] T064 [US8] Implement `Features/Pricing/Observability/ResolutionLogger.cs` emitting R9 field set via Serilog structured + OTel spans
- [ ] T065 [US8] Wire logger into `ResolveBasketHandler` and `ResolveTokenHandler` (MediatR pipeline behavior `LoggingBehavior.cs`)
- [ ] T066 [US8] Implement `customer_hash` and `basket_hash` helpers in `Observability/Hashing.cs` with HMAC/SHA256
- [ ] T067 [US8] Configure rate limiter policy `pricing-resolve` (per-IP + per-customer token bucket) gating `/pricing/resolve-basket` and `/pricing/resolve-token` → 429 with envelope
- [ ] T068 [P] [US8] Unit test `Tests/Pricing.Unit/Observability/LogFieldTests.cs` — asserts no PII, no coupon codes, no SKUs present in emitted log payloads
- [ ] T069 [P] [US8] Integration test `Tests/Pricing.Integration/Observability/RateLimitTests.cs` — over-threshold caller gets 429 with retry-after

## Phase 11 — Property Tests & Goldens

- [ ] T070 [P] FsCheck property tests `Tests/Pricing.Properties/PricingInvariantsTests.cs` — 5 invariants from R15 at 10 000 generated cases per run (SC-004)
- [ ] T071 [P] FsCheck generators `Tests/Pricing.Properties/Generators/BasketGenerators.cs` producing realistic basket+promo+coupon tuples per market
- [ ] T072 [P] Extend golden fixtures with B2B + coupon-blocked-by-exclusion edge cases in `Tests/Pricing.Goldens/fixtures/edge-cases/`

## Phase 12 — Polish & Cross-Cutting

- [ ] T073 [P] Exception handler middleware `Features/Pricing/Shared/PricingExceptionHandler.cs` converting exceptions to `ErrorEnvelope` with correlation id
- [ ] T074 [P] OpenAPI snapshot test `Tests/Pricing.Contract/OpenApiSnapshotTests.cs` ensuring `packages/shared_contracts/pricing/pricing.openapi.yaml` matches canonical
- [ ] T075 [P] DTO snapshot test `Tests/Pricing.Contract/DtoSnapshotTests.cs`
- [ ] T076 [P] Operator runbook `docs/runbooks/pricing.md` — tax rule change, promo pause/expire, coupon-cap inspection, token-key rotation procedure
- [ ] T077 [P] Constitution re-check: re-run `plan.md` gates against implemented code; append PASS/FAIL note
- [ ] T078 Verify Definition of Done checklist in `quickstart.md` §3
- [ ] T079 End-to-end smoke following `quickstart.md` §2 against a fresh environment

---

## Dependency Graph

```
Phase 1 ─▶ Phase 2 ─┬─▶ Phase 3 (US1) ─▶ Phase 4 (US2) ──┐
                    │                                    ├─▶ Phase 11 (Props/Goldens) ─▶ Phase 12 (Polish)
                    ├─▶ Phase 5 (US3) ───────────────────┤
                    ├─▶ Phase 6 (US4) ───────────────────┤
                    ├─▶ Phase 7 (US5) ───────────────────┤
                    ├─▶ Phase 8 (US6) ───────────────────┤
                    ├─▶ Phase 9 (US7) ───────────────────┤
                    └─▶ Phase 10 (US8) ──────────────────┘
```

Phase 3 (US1) sits upstream of 4–10 because all other stages compose into `ResolveBasketHandler`. Phases 4–10 are independent peers once Phase 3 lands.

---

## Parallel Execution Opportunities

- **Phase 1**: T003, T004, T005 in parallel after T001/T002.
- **Phase 2**: T009–T018 in parallel after T006+T007; T019 after T014.
- **Phase 3**: T025, T026, T027 in parallel after T024.
- **Phase 4**: T033, T034, T035 in parallel after T032.
- **Phase 5**: T041, T042, T043 in parallel after T040.
- **Phase 6**: T047, T048 in parallel after T046.
- **Phase 7**: T052, T053 in parallel after T051.
- **Phase 8**: T056, T057 in parallel after T055.
- **Phase 9**: T062, T063 in parallel after T061.
- **Phase 10**: T068, T069 in parallel after T067.
- **Phase 11**: T070, T071, T072 fully parallel.
- **Phase 12**: T073–T077 fully parallel.

---

## Suggested MVP Scope

Phases 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 = **T001 through T057** (57 tasks).
Delivers: deterministic resolution + promotion primitives with stacking/exclusion + coupon validation & commit + VAT + B2B business/tier + price-token resolution. Audit coverage (US7) and observability/rate limiting (US8) land in the same PR train but are P2.

---

## Totals

- **Total tasks**: 79 (T001–T079)
- **Phase counts**: Setup 5 · Foundational 14 · US1 8 · US2 8 · US3 8 · US4 5 · US5 5 · US6 4 · US7 6 · US8 6 · Properties 3 · Polish 7
- **Parallel markers**: 45 tasks flagged `[P]`
- **User-story labels**: 50 tasks across US1–US8

---

## Amendment A1 — Environments, Docker, Seeding

**Source**: [`docs/missing-env-docker-plan.md`](../../../docs/missing-env-docker-plan.md)

**Hard dependency**: PR A1 + PR 004 + PR 005 must merge before this PR.

### New tasks

- [ ] T080 [US1] Implement `services/backend_api/Features/Seeding/Seeders/_007_PricingSeeder.cs` (`Name="pricing-v1"`, `Version=1`, `DependsOn=["catalog-v1"]`). Seeds VAT rules (KSA 15%, EG 14%), 3 promotions (1 active, 1 scheduled, 1 expired), 5 coupons (3 active, 2 expired), 2 business pricing tiers, 1 BOGO rule, all bilingual where customer-visible.
- [ ] T081 [US1] Integration test `Tests/Pricing.Integration/Seeding/PricingSeederTests.cs`: fresh-apply populates expected rows; second apply no-op; a sample basket price-quote against seeded variants returns deterministic totals (golden-file regression).
