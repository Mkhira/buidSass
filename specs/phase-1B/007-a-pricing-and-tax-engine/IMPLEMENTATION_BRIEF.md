# Implementation Brief — Spec 007-a · Pricing & Tax Engine v1

**Target**: Claude, working solo on branch `007-a-pricing`.
**Rhythm**: implement → self-review → remediate → commit → open PR.
**Fingerprint**: `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62` (unchanged since spec 004).

---

## 0. Ground truth (read first — in order)

1. `specs/phase-1B/007-a-pricing-and-tax-engine/spec.md` — 24 FR, 8 SC, 6 user stories, 10 edge cases.
2. `specs/phase-1B/007-a-pricing-and-tax-engine/plan.md` — 8 phases, module layout.
3. `specs/phase-1B/007-a-pricing-and-tax-engine/data-model.md` — 9 tables, explanation JSON shape.
4. `specs/phase-1B/007-a-pricing-and-tax-engine/research.md` — 12 research decisions (layer order, rounding, bundles, BOGO).
5. `specs/phase-1B/007-a-pricing-and-tax-engine/contracts/pricing-contract.md` — HTTP surface + reason codes.
6. `specs/phase-1B/007-a-pricing-and-tax-engine/tasks.md` — 46 tasks, phase-ordered; mark `[X]` as you complete each.

Do NOT deviate from the research decisions (R1–R12) or the layer order (list → tier → promotion → coupon → tax). Those are locked.

---

## 1. Hard rules (learned from specs 004, 005, 006 — do NOT re-violate)

### DB / migrations
- **UTF-8 WITHOUT BOM** on every EF migration file. CI's charset gate rejects BOM. After `dotnet ef migrations add`, run `tail -c +4 original > stripped && mv stripped original` loop on any file starting with bytes `EF BB BF`.
- `PricingDbContext.OnModelCreating` MUST use the namespace-filtered overload of `ApplyConfigurationsFromAssembly`:
  ```csharp
  modelBuilder.ApplyConfigurationsFromAssembly(
      typeof(PricingDbContext).Assembly,
      type => type.Namespace?.StartsWith("BackendApi.Modules.Pricing", StringComparison.Ordinal) == true);
  ```
  Without this, other modules' entities leak into Pricing's model and break `Identity.Tests` / `Catalog.Tests` / `Search.Tests` with `PendingModelChangesWarning`.
- Every `pricing.*` table uses lowercase snake_case table name in `.ToTable("tax_rates", "pricing")` form. Column names stay EF-default PascalCase (matches 004/005/006).
- Seed the citext extension once at migration time (look at `Catalog_Initial` for the pattern).

### Authorization
- Every admin endpoint MUST use `.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" }).RequirePermission("pricing.xxx.{read|write}")`.
- Seed the new permissions in `IdentityReferenceDataSeeder` (or equivalent seeder — check spec 004/005/006 patterns):
  ```
  pricing.tax.read          pricing.tax.write
  pricing.promotion.read    pricing.promotion.write
  pricing.coupon.read       pricing.coupon.write
  pricing.tier.read         pricing.tier.write
  pricing.explanation.read
  ```
  Seed two roles: `pricing.editor` (all writes except tax), `pricing.admin` (everything).
- Customer `price-cart` endpoint is unauthenticated (shoppers can preview prices — Principle 1).
- Internal `calculate` endpoint uses Admin JWT **plus** `pricing.internal.calculate` permission, OR a dedicated service-to-service bypass — choose the simpler path for now (Admin JWT + permission; spec 011 will refine).

### Audit (Principle 25)
- Every admin mutation (tax rate, promotion, coupon, tier, tier-price, account-tier assignment) writes via `IAuditEventPublisher.PublishAsync` with `actor_id`, `action="pricing.{thing}.{verb}"`, `entity_type`, `entity_id`, `before_state`, `after_state`, `reason`.
- Pattern: Catalog's `BrandAdminEndpoints.cs` is the canonical example.

### Tests
- **Serialize Testcontainer fixtures**: every test class under `Pricing.Tests` MUST carry `[Collection("pricing-fixture")]`. Define `PricingCollection.cs` with `[CollectionDefinition("pricing-fixture", DisableParallelization = true)]` — without this, parallel xUnit classes spawn too many Postgres containers and flake.
- `PricingTestFactory` mirrors `SearchTestFactory` shape: Postgres 16 Alpine container + `WithCleanUp(true)`, migrate AppDb/Identity/Catalog/Pricing, override DI for `IAuditEventPublisher` only if needed (prefer real).
- `ResetDatabaseAsync` truncates the full `pricing.*` table set + `identity.*` + `public.audit_log_entries` + `public.seed_applied`. Check `SearchTestFactory.ResetDatabaseAsync` — copy the pattern, add pricing tables.
- Don't mock out the DB for pricing tests. Property-based determinism + concurrency tests need real Postgres.

### Packaging / DI scope
- **Singleton cannot capture Scoped.** If any hosted service or singleton needs `PricingDbContext` / `ISearchEngine` / any scoped service, resolve it inside `StartAsync`/method body via `IServiceScopeFactory.CreateAsyncScope()`. This burned spec 006; don't repeat it.
- `IPriceCalculator` and the 5 layer classes are **stateless** → can be `AddSingleton`. They depend on `ITaxRateCache`/`IPromotionCache` (also singleton — they wrap `IMemoryCache` + invalidation events).
- Admin handlers that write to DB → scoped (they take `PricingDbContext`, `IAuditEventPublisher`).

### Determinism
- **Never call `DateTime.UtcNow` inside the engine.** Every time input flows through `PricingContext.NowUtc` (R5). Admin mutation handlers CAN use `DateTimeOffset.UtcNow` for audit timestamps.
- Canonical JSON serialization for `explanation_hash`: sort keys alphabetically, use `JsonSerializerOptions { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = null }`, include all fields (no conditional omit), encode as UTF-8 no BOM, then SHA-256 hash.
- Determinism test (T039) must pass 10k random carts × 2 passes = 0 hash drift.

### Rounding
- `BankersRounding.ToMinor(decimal amount)` → `long` minor units; `MidpointRounding.ToEven` built into .NET's `Math.Round`.
- At each layer boundary (end of ListPriceLayer, B2BTierLayer, etc.), recompute line net/discount in minor units and round via banker's.
- Self-assert: after all layers, `sum(line.netMinor) + sum(line.taxMinor) == totals.grandTotalMinor`. If not, `throw new InvalidOperationException("pricing.internal.rounding_drift")`. Fail fast, log the inputs hash for triage.

### Reason codes
- Use consistent prefix `pricing.*`. Enumerated in `contracts/pricing-contract.md`.
- ProblemDetails returns `application/problem+json` with `reasonCode` in extensions. Copy the `CustomerSearchResponseFactory` pattern from spec 006 — don't reinvent.

### ICU messages
- `Modules/Pricing/Messages/pricing.ar.icu` + `pricing.en.icu`. One key per reason code + user-facing surface.
- After implementation, file `AR_EDITORIAL_REVIEW.md` under `specs/phase-1B/007-a-pricing-and-tax-engine/` with 15-ish provisional translations marked `needs-ar-editorial-review` (see spec 005's version as the template).

### Observability (FR-018, Phase 8)
- Metric: `pricing_calculate_duration_ms` (Histogram) — look at `SearchMetrics.cs`.
- Metric: `pricing_coupon_redemptions_total` (Counter, tag by `couponId`).
- Structured log on every `Calculate`: `explanation_hash` (base64), `grand_total_minor`, `market`, `layer_count`, `coupon_code_hash` (SHA-256, not raw).

### Layer-order quality
- Each layer class has the signature: `void Apply(PricingWorkingSet ws)` where `PricingWorkingSet` holds the mutable in-flight state (lines, explanation rows, `nowUtc`, `accountContext`, etc.). Layers never call each other; the orchestrator calls them in fixed order.
- Layer must be idempotent on re-run with the same working-set snapshot (for determinism test).

---

## 2. Module layout (create these directories)

```
services/backend_api/Modules/Pricing/
├── Primitives/
│   ├── IPriceCalculator.cs
│   ├── PriceCalculator.cs
│   ├── PricingContext.cs
│   ├── PriceResult.cs
│   ├── ExplanationRow.cs
│   ├── PricingWorkingSet.cs
│   ├── Rounding/
│   │   └── BankersRounding.cs
│   ├── Explanation/
│   │   └── ExplanationHasher.cs
│   ├── Layers/
│   │   ├── ListPriceLayer.cs
│   │   ├── B2BTierLayer.cs
│   │   ├── PromotionLayer.cs
│   │   ├── CouponLayer.cs
│   │   └── TaxLayer.cs
│   └── Caches/
│       ├── TaxRateCache.cs
│       └── PromotionCache.cs
├── Entities/
│   ├── TaxRate.cs
│   ├── Promotion.cs
│   ├── Coupon.cs
│   ├── CouponRedemption.cs
│   ├── B2BTier.cs
│   ├── AccountB2BTier.cs
│   ├── ProductTierPrice.cs
│   ├── PriceExplanation.cs
│   └── BundleMembership.cs
├── Persistence/
│   ├── PricingDbContext.cs
│   ├── PricingDbContextDesignTimeFactory.cs
│   ├── Configurations/
│   │   └── PricingEntityConfigurations.cs
│   └── Migrations/
│       └── (generated)
├── Seeding/
│   └── PricingReferenceDataSeeder.cs   // seeds EG 14% + KSA 15% VAT
├── Customer/
│   └── PriceCart/
│       ├── Request.cs
│       ├── Validator.cs
│       ├── Handler.cs
│       └── Endpoint.cs
├── Admin/
│   ├── Common/
│   │   └── AdminPricingResponseFactory.cs
│   ├── TaxRates/        {List, Create, Patch}
│   ├── Promotions/      {List, Create, Update, Activate, Deactivate, Delete}
│   ├── Coupons/         {List, Create, Update, Deactivate, Redemptions}
│   ├── B2BTiers/        {List, Create, Update, Delete, AssignToAccount}
│   ├── ProductTierPrices/ {Upsert, Delete}
│   └── Explanations/    {GetByOwnerId}
├── Internal/
│   └── Calculate/
│       ├── Request.cs
│       ├── Handler.cs
│       └── Endpoint.cs
├── Messages/
│   ├── pricing.ar.icu
│   └── pricing.en.icu
└── PricingModule.cs

tests/Pricing.Tests/
├── Unit/
│   ├── BankersRoundingTests.cs
│   ├── ExplanationHasherTests.cs
│   └── Layers/
│       ├── ListPriceLayerTests.cs
│       ├── B2BTierLayerTests.cs
│       ├── PromotionLayerTests.cs
│       ├── CouponLayerTests.cs
│       └── TaxLayerTests.cs
├── Contract/
│   ├── Customer/
│   │   ├── PriceCartContractTests.cs
│   │   └── BogoContractTests.cs
│   └── Admin/
│       ├── TaxRatesContractTests.cs
│       ├── PromotionsContractTests.cs
│       ├── CouponsContractTests.cs
│       ├── TiersContractTests.cs
│       └── ExplanationsContractTests.cs
├── Integration/
│   ├── PromotionStackTests.cs
│   ├── QuotationReuseTests.cs
│   └── CouponConcurrencyTests.cs
├── Property/
│   └── DeterminismTests.cs
└── Infrastructure/
    ├── PricingCollection.cs
    ├── PricingTestFactory.cs
    ├── PricingAdminAuthHelper.cs
    └── PricingTestSeedHelper.cs
```

---

## 3. Execution order (strict)

Follow `tasks.md` phases in order. Do NOT start Phase 2 until Phase 1 is green.

### Phase 1 — Setup (T001–T003)
- Create the module tree + test project.
- `Program.cs` adds `AddPricingModule(configuration, hostEnvironment)` + `UsePricingModuleEndpoints()`.
- Add `FluentValidation.AspNetCore` + `Microsoft.Extensions.Caching.Memory` to `backend_api.csproj`.

### Phase 2 — Foundational (T004–T021)
Primitives first, then persistence, then unit tests.

**Primitives** (T004–T012):
- `PricingContext` is a record with: `MarketCode, Locale, AccountContext?, Lines (IReadOnlyList<ContextLine>), CouponCode?, QuotationId?, NowUtc, Mode (Preview|Issue)`.
- `ContextLine` = `(ProductId, Qty, ListPriceMinor, Restricted, CategoryIds)`.
- `AccountContext` = `(AccountId, TierSlug?, VerificationState)`.
- `PriceResult` = `(Lines, Totals, Currency, ExplanationHash, ExplanationId?)`.
- `PricingWorkingSet` is the mutable in-flight struct passed between layers: `(List<WorkingLine> Lines, List<ExplanationRow> ExplanationRows, PricingContext Ctx)`.

**Persistence** (T013–T017):
- Generate migration with `dotnet ef migrations add Pricing_Initial --context PricingDbContext --output-dir Modules/Pricing/Persistence/Migrations --project services/backend_api/backend_api.csproj`.
- Strip BOM from the generated files.
- Seed VAT rates: EG 1400 bps, KSA 1500 bps, `effective_from = 2020-01-01Z`, `effective_to = NULL`, `created_by_account_id = Guid.Empty` (system).

**Tests** (T019–T021):
- Banker's rounding unit tests: round 0.5 → 0, round 1.5 → 2, round 2.5 → 2, round -0.5 → 0, round -1.5 → -2. Plus 20 edge cases.
- Per-layer unit tests exercise each layer in isolation with a hand-built `PricingWorkingSet`.

### Phase 3 — MVP customer path (T022–T027)
- Write the 4 contract tests FIRST (T022–T025) — they will fail until T026 lands.
- Implement `Customer/PriceCart` handler: resolves products from `CatalogDbContext`, resolves tier from `IdentityDbContext` if authenticated, calls `IPriceCalculator.Calculate(ctx)` in Preview mode.
- Populate `pricing.{ar,en}.icu` with the reason codes from the contract.

### Phase 4 — Promotion layer (T028–T030)
- BOGO math in `PromotionLayer`. Tricky bit: when multiple qualifying items exist in a line with qty=3, apply reward to exactly N rewards per `qualify_qty`. Enforce deterministic line-iteration order.

### Phase 5 — Quotation reuse (T031–T032)
- `Internal/Calculate` endpoint: if `ctx.QuotationId` is set and `price_explanations` has a row with `owner_kind=quote, owner_id=quotationId`, return the stored JSON verbatim — parse back into `PriceResult`.
- In `Issue` mode, INSERT a new `price_explanations` row.

### Phase 6 — Admin surface (T033–T038)
- Write contract tests for each CRUD slice first (they'll fail).
- Implement: handlers should look like `BrandAdminEndpoints.cs` / `CategoryAdminEndpoints.cs` from spec 005 — static methods per HTTP verb, `.RequirePermission(...)`, audit on every write.
- Caching invalidation: on tax-rate or promotion mutation, raise an in-proc event (e.g., `TaxRateCache.Invalidate(marketCode)`) — singleton caches expose invalidation methods called by admin handlers.

### Phase 7 — Determinism + concurrency (T039–T041)
- `DeterminismTests` (T039): generate 10,000 random carts with random rules (fixed seed), price twice, assert hash equality. Use `System.Random` with known seed, or `xUnit.Combinatorial`.
- `CouponConcurrencyTests` (T040): fire 100 parallel `POST /v1/internal/pricing/calculate` with `mode=issue` and the same per-customer-limit-1 coupon + same `accountId`. Exactly 1 must succeed; 99 must get `pricing.coupon.limit_reached`. Uses optimistic concurrency via `coupons.row_version` + unique index on `coupon_redemptions (coupon_id, account_id)`.
- `RoundingDriftTests` (T041): 20-line cart, assert `totals.grandTotalMinor == sum(line.grossMinor)`.

### Phase 8 — Observability + polish (T042–T046)
- Add `PricingMetrics` under `Modules/Observability/`.
- Structured log emitter inside `PriceCalculator.Calculate`.
- Hand-author `openapi.pricing.json` at `services/backend_api/openapi.pricing.json` (match the pattern from `openapi.search.json`).
- Write `DOD_WALKTHROUGH.md` + `AR_EDITORIAL_REVIEW.md` under the spec folder.
- Compute fingerprint (should stay `789f…`; if it changes, constitution/ADR drifted).

---

## 4. Quality bar (self-review checklist before committing)

- [ ] All 46 tasks in `tasks.md` marked `[X]` **honestly** (don't mark a task done if the test is a stub).
- [ ] `dotnet build services/backend_api/` → 0 errors. Known CVE warnings on SixLabors are OK.
- [ ] `dotnet test tests/Pricing.Tests/` → 100% pass.
- [ ] `dotnet test tests/Catalog.Tests/` → 42/42 (no regression).
- [ ] `dotnet test tests/Identity.Tests/` → 126–127/127 (the flaky `EnumerationTiming` timing test from spec 004 is pre-existing; everything else must pass).
- [ ] `dotnet test tests/Search.Tests/` → 60/60 (no regression).
- [ ] No BOM on any migration file: `find services/backend_api/Modules/Pricing -path '*Migrations*' -name '*.cs' -exec sh -c 'head -c 3 "$1" | od -c | head -1' _ {} \;` — no output starting with `efbbbf`.
- [ ] No singleton capturing scoped. Grep `AddSingleton` + `AddHostedService` in `PricingModule.cs` and verify none take scoped services in constructors.
- [ ] Every admin endpoint has `.RequirePermission("pricing....")`.
- [ ] Every admin mutation calls `IAuditEventPublisher.PublishAsync`.
- [ ] No `DateTime.UtcNow` inside `Modules/Pricing/Primitives/**/*.cs` (ctx.NowUtc only).
- [ ] `explanation_hash` computation is canonicalized (sorted keys, minified, UTF-8 no BOM).

Then **do one self-review pass** the same way I did on 006:
- Read every changed file end-to-end.
- Check for: wrong-direction bugs, NRE on nullable inputs, blanket catches, missing cancellation tokens, singleton/scoped DI mismatches, stale-job scenarios, raw query → Meili-equivalent pathway mismatches, audit gaps, SSE/streaming hardening.
- Compile findings into a short report. Fix in-session (don't hand to Codex).

---

## 5. Open points I'm allowed to decide unilaterally

- **Internal `calculate` auth**: Admin JWT + `pricing.internal.calculate` permission for now. Document that spec 011 will move this to service-to-service signed JWT.
- **Event bus**: spec 003 says events go through a bus; if there's no concrete implementation yet, log `pricing.*.event` records through the logger with a TODO to wire the bus once spec 011/012 adds a consumer. Don't invent infrastructure.
- **Bundle memberships** (table #9): create the table but don't implement any behavior — pricing engine doesn't consume it. Add a comment `// Reserved for analytics / admin bundle inspection — no runtime use in v1`.
- **Currency derivation**: `market_code → currency` is a const map (`{ "ksa": "SAR", "eg": "EGP" }`). Put this in `PricingConstants.cs` inside `Primitives/`.

## 6. Open points I MUST flag before continuing

- **Spec 004 `AccountContext` shape**: verify what `IdentityDbContext` exposes for tier lookup. If `pricing.account_b2b_tiers` is the source of truth (this spec creates that table), then the tier lookup is local — no cross-module call needed.
- **Spec 005 product lookup**: need `ProductId, ListPriceHint, Restricted, CategoryIds, MarketCodes`. Read via `CatalogDbContext` (read-only, `AsNoTracking`). If any field I need doesn't exist on `Product`, flag rather than invent.

---

## 7. Commit + PR rhythm

1. When Phase 8 green + self-review clean: stage, commit with fingerprint trailer, push.
2. Commit title: `feat(spec-007-a): pricing & tax engine v1 — layered pipeline, tier/promo/coupon/vat, determinism + audit`.
3. PR body includes: task status, constitution/ADR mapping, test results, follow-ups (if any), fingerprint HTML comment as line 1.
4. No emojis. No "generated with Claude" footer unless it was in earlier PRs (check spec 006 PR for the actual footer format).

---

**Now begin at Phase 1.** Read `tasks.md` again, then start `T001`.
