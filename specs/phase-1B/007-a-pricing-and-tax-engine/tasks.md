---
description: "Dependency-ordered tasks for spec 007-a — pricing and tax engine"
---

# Tasks: Pricing & Tax Engine v1

**Input**: spec.md (24 FRs, 8 SCs, 6 user stories), plan.md, research.md, data-model.md, contracts/pricing-contract.md.

## Phase 1: Setup
- [ ] T001 Module tree `services/backend_api/Modules/Pricing/{Primitives,Primitives/Layers,Primitives/Rounding,Primitives/Explanation,Customer/PriceCart,Admin/{TaxRates,Promotions,Coupons,B2BTiers,ProductTierPrices,Explanations},Internal/Calculate,Entities,Persistence/{Configurations,Migrations},Messages}` + tests `tests/Pricing.Tests/{Unit,Integration,Contract,Property}`
- [ ] T002 Register `AddPricingModule` + wire in `Program.cs`
- [ ] T003 [P] Add NuGet deps: `FluentValidation.AspNetCore`, `Microsoft.Extensions.Caching.Memory`

## Phase 2: Foundational
### Primitives
- [ ] T004 [P] `IPriceCalculator` + DTOs in `Modules/Pricing/Primitives/*.cs`
- [ ] T005 [P] `BankersRounding` in `Modules/Pricing/Primitives/Rounding/BankersRounding.cs`
- [ ] T006 [P] `ExplanationHasher` (canonical JSON → SHA-256) in `Modules/Pricing/Primitives/Explanation/ExplanationHasher.cs`
- [ ] T007 [P] `ListPriceLayer` in `Modules/Pricing/Primitives/Layers/ListPriceLayer.cs`
- [ ] T008 [P] `B2BTierLayer` in `Modules/Pricing/Primitives/Layers/B2BTierLayer.cs`
- [ ] T009 [P] `PromotionLayer` in `Modules/Pricing/Primitives/Layers/PromotionLayer.cs`
- [ ] T010 [P] `CouponLayer` in `Modules/Pricing/Primitives/Layers/CouponLayer.cs`
- [ ] T011 [P] `TaxLayer` in `Modules/Pricing/Primitives/Layers/TaxLayer.cs`
- [ ] T012 `PriceCalculator` orchestrator in `Modules/Pricing/Primitives/PriceCalculator.cs`

### Persistence
- [ ] T013 9 entities in `Modules/Pricing/Entities/*.cs`
- [ ] T014 EF configs in `Modules/Pricing/Persistence/Configurations/*.cs`
- [ ] T015 `PricingDbContext` + unique/partial indexes
- [ ] T016 Migration `Pricing_Initial`; apply on A1 Postgres
- [ ] T017 Seed EG 14 % + KSA 15 % VAT rates via `PricingDevDataSeeder`
- [ ] T018 `TaxRateCache` + `PromotionCache` with in-proc invalidation events
- [ ] T019 Banker's rounding unit tests `tests/Pricing.Tests/Unit/BankersRoundingTests.cs`
- [ ] T020 Layer unit tests per layer in `tests/Pricing.Tests/Unit/Layers/*.cs`
- [ ] T021 `PricingTestFactory` + Testcontainers Postgres in `tests/Pricing.Tests/Infrastructure/*.cs`

## Phase 3: US1/US2/US3 — Customer pricing (P1) 🎯 MVP
- [ ] T022 [P] [US1] Contract test `PriceCart_ListPlusVat_BreakdownVisible` at `tests/Pricing.Tests/Contract/Customer/PriceCartContractTests.cs`
- [ ] T023 [P] [US2] Contract test `PriceCart_Tier2Account_AppliesTierPrice` in same file
- [ ] T024 [P] [US3] Contract test `PriceCart_Coupon_AppliesPercentWithCap` in same file
- [ ] T025 [P] [US3] Contract test `PriceCart_ExpiredCoupon_ReturnsReasonCode` in same file
- [ ] T026 [US1] Implement `Customer/PriceCart/{Request,Validator,Handler,Endpoint}.cs`
- [ ] T027 [US1] Populate `Messages/pricing.{ar,en}.icu`

## Phase 4: US4 — Promotion layer (P2)
- [ ] T028 [P] [US4] Contract test `Bogo_ThreeQualifying_OneFree` at `tests/Pricing.Tests/Contract/Customer/BogoContractTests.cs`
- [ ] T029 [P] [US4] Integration test `BogoThenCoupon_StackCorrectly` at `tests/Pricing.Tests/Integration/PromotionStackTests.cs`
- [ ] T030 [US4] Extend `PromotionLayer` for bogo + percent_off + amount_off kinds

## Phase 5: US5 — Quotations integration (P2)
- [ ] T031 [P] [US5] Integration test `QuoteIssued_ReusedOnAcceptance` at `tests/Pricing.Tests/Integration/QuotationReuseTests.cs`
- [ ] T032 [US5] Implement `Internal/Calculate` endpoint with `mode: preview|issue` and `quotationId` lookup of stored explanation

## Phase 6: US6 — Admin surface (P2)
- [ ] T033 [P] [US6] Contract tests for tax-rates CRUD at `tests/Pricing.Tests/Contract/Admin/TaxRatesContractTests.cs`
- [ ] T034 [P] [US6] Contract tests for promotions CRUD at `tests/Pricing.Tests/Contract/Admin/PromotionsContractTests.cs`
- [ ] T035 [P] [US6] Contract tests for coupons CRUD at `tests/Pricing.Tests/Contract/Admin/CouponsContractTests.cs`
- [ ] T036 [P] [US6] Contract tests for tiers + tier-prices at `tests/Pricing.Tests/Contract/Admin/TiersContractTests.cs`
- [ ] T037 [P] [US6] Contract test `GetExplanation_ByOrderId_ReturnsImmutable` at `tests/Pricing.Tests/Contract/Admin/ExplanationsContractTests.cs`
- [ ] T038 [US6] Implement admin CRUD slices (audit on every write via `IAuditEventPublisher`)

## Phase 7: Determinism + Concurrency
- [ ] T039 [P] Property-based test `Determinism_10kCarts_NoHashDrift` at `tests/Pricing.Tests/Property/DeterminismTests.cs` (SC-002)
- [ ] T040 [P] Concurrency test `CouponPerCustomer_100ConcurrentRedeems` at `tests/Pricing.Tests/Integration/CouponConcurrencyTests.cs` (SC-004)
- [ ] T041 [P] Rounding-drift test `20LineCart_ZeroDrift` (SC-003)

## Phase 8: Observability + Polish
- [ ] T042 [P] Metric `pricing_calculate_duration_ms` + `pricing_coupon_redemptions_total`
- [ ] T043 [P] Structured log on every `Calculate`
- [ ] T044 [P] AR editorial pass on `pricing.ar.icu`
- [ ] T045 [P] OpenAPI regen + contract diff green (Guardrail #2)
- [ ] T046 Fingerprint + DoD walk-through

**Totals**: 46 tasks across 8 phases. MVP = Phases 1 + 2 + 3.
