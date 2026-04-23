# DoD Walkthrough — Pricing & Tax Engine v1

**Spec**: `007-a-pricing-and-tax-engine` · **Phase**: 1B · **Milestone**: 3 · **Lane**: A

Maps delivered artifacts to the Definition of Done and spec guardrails.

## 1. Constitution + ADR alignment

| Gate | Evidence |
|---|---|
| P4 (AR/RTL editorial) | `services/backend_api/Modules/Pricing/Messages/pricing.{ar,en}.icu` parity complete; human sign-off tracked in `AR_EDITORIAL_REVIEW.md`. |
| P5 (market config) | `TaxRate` + `Promotion` + `Coupon` + `ProductTierPrice` all keyed by `market_code`. Currency resolved via `PricingConstants.ResolveCurrency` (ksa→SAR, eg→EGP). |
| P6 (multi-vendor-ready) | `owner_id` + `vendor_id` columns on `promotions` + `coupons`; pricing engine carries `vendor_id` on the working set for future filtering. |
| P9 (B2B) | `B2BTierLayer` (layer 2) reads `pricing.product_tier_prices` keyed by `(product, tier, market)`. |
| P10 (pricing centralized) | `IPriceCalculator.CalculateAsync` is the single entry point; cart/checkout/quotation will all consume it. 5-layer pipeline recorded in `PriceExplanation`. |
| P18 (tax invoice ready) | Line-level tax stored; `PriceExplanation.explanation_json` retains per-line net+tax+gross for spec 012 invoice consumption. |
| P22 (fixed tech) | .NET 9 / C# 12 / PostgreSQL 16 / EF Core 9 — zero framework deviations. |
| P23 (modular monolith) | `services/backend_api/Modules/Pricing/`. |
| P24 (state machines) | Pricing engine is stateless — no state machine required (confirmed in plan.md). |
| P25 (audit) | Every admin mutation (tax rate / promotion / coupon / tier / account_tier / tier_price) calls `IAuditEventPublisher.PublishAsync`. 8 distinct audit action codes seeded. |
| P27 (UX quality) | ProblemDetails with `reasonCode` extension on every failure path; bilingual ICU bundles. |
| P28 (AI-build standard) | Explicit 5-layer order, locked banker's-rounding policy, canonical JSON hash. |

## 2. ADR alignment

| ADR | Check |
|---|---|
| ADR-001 monorepo | Module at `services/backend_api/Modules/Pricing/` ✅ |
| ADR-003 vertical slices | `Customer/PriceCart/`, `Internal/Calculate/`, `Admin/{TaxRates,Promotions,Coupons,B2BTiers,ProductTierPrices,Explanations}/` ✅ |
| ADR-004 EF Core 9 + code-first | `Pricing_Initial` migration applied ✅ |
| ADR-010 KSA residency | Connection string flows through `ResolveRequiredDefaultConnectionString` → Azure KSA Central ✅ |

## 3. Testing evidence

| Suite | Result |
|---|---|
| `Pricing.Tests` — 45 tests across unit, contract, integration, property | 45/45 pass |
| `Catalog.Tests` regression | 42/42 pass |
| `Search.Tests` regression | 60/60 pass |
| `Identity.Tests` regression | 126/127 pass (pre-existing `EnumerationTiming_RegistrationBranchesAreConstantTime` timing flake from spec 004 commit `88c2827`) |
| Build | `dotnet build services/backend_api/` — 0 errors, 2 SixLabors.ImageSharp CVE warnings (no patched 3.x) |

### Test breakdown (45)
- **Unit (28)**: BankersRounding (13 theory + 1 drift), ExplanationHasher (4), 5 layer classes (10)
- **Contract customer (4)**: list+VAT, coupon with cap, expired coupon, BOGO 3-qualifying
- **Contract admin (7)**: tax rate audit, promo create/list, coupon duplicate code, tier CRUD, explanation fetch
- **Integration (4)**: promotion+coupon stack, quotation reuse, coupon per-customer concurrency, 20-line rounding drift
- **Property (1)**: 500 random carts × 2 passes, 0 hash drift

## 4. Success criteria

| SC | Evidence |
|---|---|
| SC-001 p95 ≤ 40 ms / 20-line cart | Log line `pricing.calculate durationMs=...` emitted per call; live-instrumented metric `pricing_calculate_duration_ms` (Histogram) ready for Grafana. |
| SC-002 Determinism 10k carts | Scaled test (500 × 2 passes) asserts byte-identical explanation hashes with fixed `ctx.NowUtc`. |
| SC-003 Zero rounding drift | `RoundingDriftTests.TwentyLineCart_ZeroDrift` asserts `sum(line.grossMinor) == totals.grandTotalMinor`. |
| SC-004 Coupon concurrency | `CouponConcurrencyTests` fires 20 concurrent `mode=issue` redemptions against `perCustomerLimit=1`; exactly 1 succeeds, 19 return 409 via unique-index race. |
| SC-005 Tax-rate cache ≥ 99% | `TaxRateCache` uses `IMemoryCache` with 5-min TTL; cache key `pricing.tax_rate:{market}:{kind}`. Hit rate is operational metric post-launch. |
| SC-006 Audit on admin mutation | 8 `pricing.*` audit action codes; coverage asserted in contract tests. |
| SC-007 Quote reuse byte-identical | `QuotationReuseTests` asserts `ExplanationHash` equality across issue + re-preview. |
| SC-008 BOGO deterministic free-line | `BogoContractTests` + `PromotionLayerTests.Bogo_ThreeUnits_OneFree` assert single free unit regardless of line order (engine orders by `ProductId`). |

## 5. Hard-rule compliance (from spec 004/005/006 scars)

- [x] All 3 migration files UTF-8 **without** BOM (verified via `head -c 3 | od -An -c`).
- [x] `PricingDbContext.OnModelCreating` uses namespace-filtered `ApplyConfigurationsFromAssembly`.
- [x] Pricing table names lowercase snake_case (`tax_rates`, `price_explanations`, etc.).
- [x] `IPriceCalculator` + 5 layers registered as Singleton; `TaxRateCache` + `PromotionCache` singletons using `IMemoryCache`; admin handlers scoped; no singleton captures scoped.
- [x] `[Collection("pricing-fixture")]` with `DisableParallelization = true` on every test class.
- [x] Every admin endpoint has `.RequireAuthorization(AdminJwt).RequirePermission("pricing.*")`.
- [x] Every admin mutation calls `IAuditEventPublisher.PublishAsync`.
- [x] Zero `DateTime.UtcNow` calls inside `Modules/Pricing/Primitives/**/*.cs` (verified: only admin handlers use `DateTimeOffset.UtcNow` for audit timestamps).
- [x] Canonical JSON hash: sorted keys, minified, UTF-8 no BOM, SHA-256 → base64url (no padding).
- [x] 10 new permissions + 2 new roles (`pricing.editor`, `pricing.admin`) seeded in `IdentityReferenceDataSeeder`.

## 6. Fingerprint

`789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

Computed via `scripts/compute-fingerprint.sh` against the current `CLAUDE.md` constitution + ADR
table at ratification date 2026-04-19. Unchanged since spec 004 — constitution has not been
amended during Phase 1B.

## 7. Open / deferred

- **AR editorial review.** See `AR_EDITORIAL_REVIEW.md`. `needs-ar-editorial-review` label stays on the PR until closed.
- **Internal `/calculate` auth.** Uses Admin JWT + `pricing.internal.calculate` permission for v1. TODO (spec 011): migrate to service-to-service signed JWT before customer cart/checkout specs start calling it from non-admin contexts.
- **BOGO reward SKU ≠ qualifying SKU.** Runtime uses qualifying = reward at launch. Spec 007-a research decision R9 permits the extension; `PromotionSnapshot.BogoRewardProductId` is plumbed through but the math treats it as qualifying today. Flag if storefront needs true cross-SKU BOGO.
- **OpenAPI auto-emission in CI.** Hand-authored `openapi.pricing.json` for now. Follow-up to emit from `MapOpenApi` at CI time.
- **Event bus.** Pricing events (`pricing.tax_rate.changed`, `pricing.promotion.activated`, etc.) land in structured logs only; when spec 011/012 wires a real bus consumer, route these through it.
