# Implementation Plan: Pricing & Tax Engine (007-a)

**Branch**: `phase_1B_creating_specs` (spec branch; implementation PRs spin off per slice) | **Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

## Summary

Deliver the Phase 1B pricing-and-tax engine as a vertical slice at `services/backend_api/Features/Pricing/`, callable from search (token), cart, checkout, orders, and admin. The engine runs a deterministic 9-stage resolution pipeline (base → compare-at → business → tier → promos → coupon → net → tax → total), returns a fully-auditable breakdown, and enforces B2B precedence, KSA 15% / EG configurable VAT, banker's-rounded integer-minor-unit arithmetic, and promotion primitives (percentage, fixed, BOGO, bundle, tier) with explicit stacking + exclusion semantics. Coupon lifecycle engine lives here; authoring UX is 007-b. Audit on all authored-data writes (Principle 25); high-volume resolutions stream to observability with hashed identifiers.

**Technical approach**: .NET 9 + MediatR (ADR-003) with one handler per API verb. Pricing domain types (`Money`, `Rate`, `MinorUnits`) are primitive-obsession–free value objects that forbid implicit rounding. EF Core 9 (ADR-004) owns authored-data tables; snapshot tables use append-only inserts. Property-based testing via FsCheck with ≥ 5 000 generated baskets per run. Golden-file regression tests (JSON fixtures, per market) guard rounding + VAT math. OpenTelemetry + Serilog on every resolution with a pricing-specific span schema. HMAC-signed price tokens verified via a rotating key vault entry.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core 9, MediatR, FluentValidation, EF Core 9 (Npgsql), FsCheck.xUnit, Serilog + OpenTelemetry, `System.Security.Cryptography.HMACSHA256`, `Microsoft.Extensions.Caching.Memory` (for in-process tax rule + promo cache), `NodaTime` for clock abstraction
**Storage**: PostgreSQL (ADR-004) in Azure Saudi Arabia Central (ADR-010); schema `pricing`
**Testing**: xUnit + FluentAssertions; FsCheck property tests; golden-file JSON fixtures per market; WebApplicationFactory integration tests; k6 for latency SCs
**Target Platform**: Linux container, .NET 9 runtime
**Project Type**: web-service (pricing API)
**Performance Goals**: `resolve-basket` p95 ≤ 250 ms @ 50 lines (SC-002); `resolve-token` p95 ≤ 100 ms (SC-003); 0 rounding drift (SC-004)
**Constraints**: Single-region residency; integer minor units only; 120-s token TTL; single coupon per basket (Phase 1B); observability logs never carry raw customer id, coupon code, or basket contents
**Scale/Scope**: 10 k products × 2 markets; peak resolution QPS ~200 (cart + PDP); promo catalogue ≤ 500 active rules; coupons ≤ 10 k active codes; tax rules ≤ 10 per market

## Constitution Check

| Principle | Gate | Status |
|---|---|---|
| 3 — B2B first-class | US5 covers business + tier; pipeline enforces B2B precedence (§3.6) | PASS |
| 5 — Market configuration | Tax rules authored per market with no code change (SC-007); currency per market in DB | PASS |
| 8 — Restricted products still priced | §3.5 mandates engine prices restricted products normally | PASS |
| 10 — Centralised pricing | Engine is the single source of truth for price math; callers never compute totals | PASS |
| 18 — Tax & invoice | VAT computation + per-line itemisation per §3.3; PricingSnapshot feeds invoice spec 012 | PASS |
| 24 — Explicit state models | Promotion + Coupon state machines in §6.2 | PASS |
| 25 — Audit | §3.7 + US7: every authored write audited with before/after | PASS |
| 27 — UX states | §5 enumerates loading/success/partial/stale-token/restricted/error | PASS |
| 28 — AI-build standard | Vertical slice per handler (ADR-003); explicit FRs + acceptance criteria | PASS |
| 29 — Required output | Spec covers all 12 required sections | PASS |
| ADR-003 | Vertical slice + MediatR | PASS |
| ADR-004 | EF Core 9 code-first migrations | PASS |
| ADR-010 | Azure SA Central single-region | PASS |

**No violations. No complexity-tracking entries.**

## Project Structure

### Documentation

```text
specs/phase-1B/007-pricing-and-tax-engine/
├── plan.md
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── pricing.openapi.yaml
│   └── events.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source

```text
services/backend_api/
├── Features/
│   └── Pricing/
│       ├── Resolve/                 # resolve-basket, resolve-token, validate-coupon handlers
│       ├── Pipeline/                # stage implementations (Base, Business, Tier, Promo, Coupon, Tax)
│       ├── Promotions/              # PromotionRule CRUD + state machine
│       ├── Coupons/                 # Coupon CRUD + redemption commit endpoint
│       ├── BusinessPricing/         # BusinessPricingEntry CRUD
│       ├── TierPricing/             # TierPricingEntry CRUD
│       ├── Tax/                     # TaxRule CRUD + rate resolver
│       ├── Tokens/                  # HMAC price-token issuer + verifier
│       ├── Money/                   # Money/MinorUnits/Rate value objects + banker's rounding
│       ├── Snapshots/               # PricingSnapshot writer (called by 011 orders)
│       ├── Observability/           # structured resolution logger
│       ├── Persistence/             # EF Core DbContext + configurations
│       └── Shared/                  # DTOs, error codes, predicate schema
└── Tests/
    ├── Pricing.Unit/                # pipeline stages, Money arithmetic, predicate evaluation
    ├── Pricing.Properties/          # FsCheck property tests
    ├── Pricing.Goldens/             # golden-file fixtures (ksa/, eg/)
    ├── Pricing.Integration/         # WebApplicationFactory + Postgres
    └── Pricing.Contract/            # OpenAPI snapshot
```
