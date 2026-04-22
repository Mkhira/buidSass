# Implementation Plan — Pricing & Tax Engine v1 (Spec 007-a)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; new schema `pricing`.
- **Module**: `services/backend_api/Modules/Pricing/` (ADR-003 vertical slices).
- **Deps (NuGet)**: `Microsoft.EntityFrameworkCore` (shared), `FluentValidation`, `Microsoft.Extensions.Caching.Memory` (tax-rate cache), built-in `System.Text.Json` + `System.Security.Cryptography` (SHA-256 hash).

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 5 — Market-configurable | PASS | Tax rates, currencies, rules all keyed by `market_code`. |
| 6 — Multi-vendor-ready | PASS | `owner_id/vendor_id` on promotion + coupon tables; engine filters accordingly. |
| 9 — B2B | PASS | Tier pricing is a first-class layer. |
| 10 — Pricing centralized | PASS | `IPriceCalculator` is the only entry; storefront/cart/checkout/invoice all call it. |
| 18 — Tax invoice ready | PASS | Line-level tax stored per FR-006/FR-007; invoice spec 012 consumes it. |
| 22 — Fixed tech | PASS | .NET + Postgres only. |
| 23 — Modular monolith | PASS | New module; no deployables. |
| 24 — State machines | PASS | No state machine at pricing layer (stateless); quote/coupon redemption states live in their owning specs. |
| 25 — Audit | PASS | FR-019 writes audit rows on every mutation. |
| 27 — UX quality | PASS | Explanation UI in admin; "incl. VAT" display codified. |
| 28 — AI-build standard | PASS | Explicit layer ordering + rounding policy + determinism guarantee. |

**Gate status**: PASS.

## Phase A — Primitives
- `Primitives/IPriceCalculator.cs` + `PricingContext`, `PriceResult`, `ExplanationRow` records.
- `Primitives/PriceCalculator.cs` — orchestrates the 5 layers.
- `Primitives/Layers/{ListPriceLayer,B2BTierLayer,PromotionLayer,CouponLayer,TaxLayer}.cs`.
- `Primitives/Rounding/BankersRounding.cs`.
- `Primitives/Explanation/ExplanationHasher.cs` (canonical JSON → SHA-256).

## Phase B — Persistence
- Entities: `TaxRate`, `Promotion`, `Coupon`, `CouponRedemption`, `B2BTier`, `AccountB2BTier`, `ProductTierPrice`, `PriceExplanation`, `Bundle` (catalog-side bundles reference this? No — bundle pricing sits on the SKU in spec 005; pricing owns the *rule* catalog only here).
- `PricingDbContext` + `Pricing_Initial` migration.

## Phase C — Caches
- `TaxRateCache` — `IMemoryCache` keyed `(market, kind)`, 5 min TTL, invalidated on admin mutation via in-proc event (SC-005).
- `PromotionCache` — similarly keyed; preloaded at boot.

## Phase D — Customer surface
- `Customer/PriceCart/{Request,Handler,Endpoint}.cs` — validates + calls engine in `Preview` mode.

## Phase E — Admin surface
- CRUD slices under `Admin/{TaxRates,Promotions,Coupons,B2BTiers,ProductTierPrices}/*`.
- Each writes an audit event (spec 003 `IAuditEventPublisher`).

## Phase F — Determinism tests
- Property-based test (`FsCheck`-style via xUnit) generating random carts × random rules; re-price twice, compare hashes.

## Phase G — Observability
- Metric `pricing_calculate_duration_ms` (histogram).
- Metric `pricing_coupon_redemptions_total` (counter, tagged).
- Structured log per `Calculate`: `explanationHash`, `layers`, `grandTotalMinor`, `couponCode?`.

## Phase H — Polish
- AR editorial pass on `pricing.ar.icu` (reason codes).
- OpenAPI regen.
- Fingerprint + DoD.

## Complexity Tracking
| Item | Why it stays | Mitigation |
|---|---|---|
| Half-even rounding at each layer | Eliminates 0.01 drift across cart lines. | Encapsulated in `BankersRounding`. |
| Immutable `price_explanations` table | Refund/invoice correctness depends on byte stability. | Append-only; unique key `(quotation_id)`/`(order_id)`. |
| Layered pipeline (5 layers) | Required by Principle 10 auditability. | Each layer is a small class with a single `Apply(ctx) => ctx.AddExplanation(...)` signature. |

## Critical files
- Create: `services/backend_api/Modules/Pricing/**`, `tests/Pricing.Tests/**`.
- Edit: `Program.cs` to register `AddPricingModule`.

**Post-design re-check**: PASS.
