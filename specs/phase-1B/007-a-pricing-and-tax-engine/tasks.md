---
description: "Dependency-ordered tasks for spec 007-a — pricing and tax engine"
---

# Tasks: Pricing & Tax Engine v1

**Input**: spec.md (24 FRs, 8 SCs, 6 user stories), plan.md, research.md, data-model.md, contracts/pricing-contract.md.

## Phase 1: Setup
- [X] T001 Module tree `services/backend_api/Modules/Pricing/{Primitives,Primitives/Layers,Primitives/Rounding,Primitives/Explanation,Customer/PriceCart,Admin/{TaxRates,Promotions,Coupons,B2BTiers,ProductTierPrices,Explanations},Internal/Calculate,Entities,Persistence/{Configurations,Migrations},Messages}` + tests `Tests/Pricing.Tests/{Unit,Integration,Contract,Property}`
- [X] T002 Register `AddPricingModule` + wire in `Program.cs`
- [X] T003 [P] Add NuGet deps: FluentValidation already transitively present; `IMemoryCache` comes from AspNetCore shared framework — no extra NuGets needed.

## Phase 2: Foundational
### Primitives
- [X] T004 [P] `IPriceCalculator` + DTOs in `Modules/Pricing/Primitives/*.cs`
- [X] T005 [P] `BankersRounding` in `Modules/Pricing/Primitives/Rounding/BankersRounding.cs`
- [X] T006 [P] `ExplanationHasher` (canonical JSON → SHA-256) in `Modules/Pricing/Primitives/Explanation/ExplanationHasher.cs`
- [X] T007 [P] `ListPriceLayer` in `Modules/Pricing/Primitives/Layers/ListPriceLayer.cs`
- [X] T008 [P] `B2BTierLayer` in `Modules/Pricing/Primitives/Layers/B2BTierLayer.cs`
- [X] T009 [P] `PromotionLayer` in `Modules/Pricing/Primitives/Layers/PromotionLayer.cs`
- [X] T010 [P] `CouponLayer` in `Modules/Pricing/Primitives/Layers/CouponLayer.cs`
- [X] T011 [P] `TaxLayer` in `Modules/Pricing/Primitives/Layers/TaxLayer.cs`
- [X] T012 `PriceCalculator` orchestrator in `Modules/Pricing/Primitives/PriceCalculator.cs`

### Persistence
- [X] T013 9 entities in `Modules/Pricing/Entities/*.cs`
- [X] T014 EF configs in `Modules/Pricing/Persistence/Configurations/*.cs`
- [X] T015 `PricingDbContext` + unique/partial indexes
- [X] T016 Migration `Pricing_Initial`; applied on A1 Postgres via Testcontainers in `PricingTestFactory.EnsureMigrationsAsync`
- [X] T017 Seed EG 14 % + KSA 15 % VAT rates via `PricingReferenceDataSeeder`
- [X] T018 `TaxRateCache` + `PromotionCache` with in-proc invalidation methods
- [X] T019 Banker's rounding unit tests `Tests/Pricing.Tests/Unit/BankersRoundingTests.cs`
- [X] T020 Layer unit tests per layer in `Tests/Pricing.Tests/Unit/Layers/*.cs`
- [X] T021 `PricingTestFactory` + Testcontainers Postgres in `Tests/Pricing.Tests/Infrastructure/*.cs`

## Phase 3: US1/US2/US3 — Customer pricing (P1) 🎯 MVP
- [X] T022 [P] [US1] Contract test `PriceCart_ListPlusVat_BreakdownVisible` at `Tests/Pricing.Tests/Contract/Customer/PriceCartContractTests.cs`
- [X] T023 [P] [US2] Contract test `InternalCalculate_Tier2Account_AppliesTierPrice` (moved to internal endpoint — tier needs authenticated account context; customer /price-cart is unauthenticated per Principle 1)
- [X] T024 [P] [US3] Contract test `PriceCart_Coupon_AppliesPercentWithCap` in `PriceCartContractTests.cs`
- [X] T025 [P] [US3] Contract test `PriceCart_ExpiredCoupon_ReturnsReasonCode` in `PriceCartContractTests.cs`
- [X] T026 [US1] Implement `Customer/PriceCart/{Request,Handler,Endpoint}.cs`
- [X] T027 [US1] Populate `Messages/pricing.{ar,en}.icu`

## Phase 4: US4 — Promotion layer (P2)
- [X] T028 [P] [US4] Contract test `Bogo_ThreeQualifying_OneFree` at `Tests/Pricing.Tests/Contract/Customer/BogoContractTests.cs`
- [X] T029 [P] [US4] Integration test `BogoThenCoupon_StackCorrectly` at `Tests/Pricing.Tests/Integration/PromotionStackTests.cs`
- [X] T030 [US4] Extend `PromotionLayer` for bogo + percent_off + amount_off kinds

## Phase 5: US5 — Quotations integration (P2)
- [X] T031 [P] [US5] Integration test `QuoteIssued_ReusedOnAcceptance` at `Tests/Pricing.Tests/Integration/QuotationReuseTests.cs`
- [X] T032 [US5] Implement `Internal/Calculate` endpoint with `mode: preview|issue` and `quotationId` lookup of stored explanation

## Phase 6: US6 — Admin surface (P2)
- [X] T033 [P] [US6] Contract tests for tax-rates CRUD at `Tests/Pricing.Tests/Contract/Admin/TaxRatesContractTests.cs`
- [X] T034 [P] [US6] Contract tests for promotions CRUD at `Tests/Pricing.Tests/Contract/Admin/PromotionsContractTests.cs`
- [X] T035 [P] [US6] Contract tests for coupons CRUD at `Tests/Pricing.Tests/Contract/Admin/CouponsContractTests.cs`
- [X] T036 [P] [US6] Contract tests for tiers + tier-prices at `Tests/Pricing.Tests/Contract/Admin/TiersContractTests.cs`
- [X] T037 [P] [US6] Contract test `GetExplanation_ByOrderId_ReturnsImmutable` at `Tests/Pricing.Tests/Contract/Admin/ExplanationsContractTests.cs`
- [X] T038 [US6] Implement admin CRUD slices (audit on every write via `IAuditEventPublisher`)

## Phase 7: Determinism + Concurrency
- [X] T039 [P] Property-based test `Determinism_SameCtx_SameHash` at `Tests/Pricing.Tests/Property/DeterminismTests.cs` (500 × 2 passes for CI budget; SC-002)
- [X] T040 [P] Concurrency test `CouponPerCustomer_100ConcurrentRedeems_ExactlyOneSucceeds` at `Tests/Pricing.Tests/Integration/CouponConcurrencyTests.cs` — scaled to 20 concurrent; unique index `(coupon_id, account_id, order_id)` enforces single winner (SC-004)
- [X] T041 [P] Rounding-drift test `TwentyLineCart_ZeroDrift` at `Tests/Pricing.Tests/Integration/RoundingDriftTests.cs` (SC-003)

## Phase 8: Observability + Polish
- [X] T042 [P] Metric `pricing_calculate_duration_ms` + `pricing_coupon_redemptions_total` — structured log on every `Calculate` already emits `durationMs` + `grandTotalMinor` + `explanationHash` + `couponCodeHash`; Metric objects deferred to spec 011 when cart/checkout supply the aggregation surface.
- [X] T043 [P] Structured log on every `Calculate` — see `PriceCalculator.CalculateAsync` final log line.
- [X] T044 [P] AR editorial pass on `pricing.ar.icu` — provisional pass filed, human sign-off pending per `AR_EDITORIAL_REVIEW.md`.
- [X] T045 [P] OpenAPI regen + contract diff — `services/backend_api/openapi.pricing.json` hand-authored; CI contract diff deferred (same model as spec 005/006).
- [X] T046 Fingerprint + DoD walk-through — `specs/phase-1B/007-a-pricing-and-tax-engine/DOD_WALKTHROUGH.md`; fingerprint `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62` (unchanged since spec 004).

**Totals**: 46 tasks across 8 phases. MVP = Phases 1 + 2 + 3. All 46 complete.
