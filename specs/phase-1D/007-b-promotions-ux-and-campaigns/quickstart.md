# Quickstart: Promotions UX & Campaigns (Spec 007-b)

**Date**: 2026-04-28
**Audience**: implementer picking up the 007-b PR(s) on the `phase_1D_creating_specs` branch.
**Prerequisites**: 007-a `pricing-and-tax-engine` at DoD on `main`; 015 `admin-foundation` contract merged; 003 platform middleware (`Idempotency-Key`, `RowVersion`, `IAuditEventPublisher`, `MessageFormat.NET`) in place; 004 `identity-and-access` permissions surface live.

This guide walks you from a clean checkout to a green PR. Each section maps to a phase from `plan.md §Implementation Phases`. Where useful, the snippet is *almost* code (file shape, key imports) — full implementation is the implementer's job.

---

## 0. Prerequisites

```bash
# Confirm 007-a is at DoD on this branch
ls services/backend_api/Modules/Pricing/Internal/Calculate
# Expect: PriceCalculator.cs, layer files, etc.

# Confirm shared middleware is wired
ls services/backend_api/Modules/Shared
# Expect: ICustomerPostSignInHook.cs, IOrderFromCheckoutHandler.cs, etc.

# Run existing test suite to confirm green baseline
dotnet test services/backend_api/tests/Pricing.Tests/Pricing.Tests.csproj
```

If 007-a is not yet merged, **stop**. 007-b is a strict consumer and cannot land first.

---

## 1. Module wiring (Phase A + B)

### 1.1 Primitives

Create `services/backend_api/Modules/Pricing/Primitives/`:

- `LifecycleState.cs` — `enum { Draft, Scheduled, Active, Deactivated, Expired }`.
- `LifecycleStateMachine.cs` — pure-function transition validator: `bool TryTransition(LifecycleState from, LifecycleStateTrigger trigger, DateTimeOffset nowUtc, out LifecycleState to, out string? reasonCode)`.
- `BusinessPricingState.cs` — `enum { Active, Deactivated }`.
- `BusinessPricingStateMachine.cs` — same pattern.
- `CommercialReasonCode.cs` — `static readonly string CommercialRowVersionConflict = "commercial.row.version_conflict"; ...` for all 32 codes.
- `CommercialActorKind.cs` — `enum { Operator, B2BAuthor, Approver, SuperAdmin, System }`.
- `CommercialThresholdPolicy.cs` — value object resolved from a `pricing.commercial_thresholds` row.
- `HighImpactGate.cs` — `static bool IsTriggered(IRuleSubject rule, CommercialThresholdPolicy threshold)` returning `true` if any criterion trips.

Tests for each primitive live in `tests/Pricing.Tests/Unit/`. Aim for property-style tests on the state machines (e.g., `[Theory] foreach valid transition: round-trip; foreach invalid transition: false return`).

### 1.2 Migrations

Three EF migrations land in `services/backend_api/Modules/Pricing/Persistence/Migrations/`:

```bash
cd services/backend_api/Modules/Pricing
dotnet ef migrations add AddLifecycleColumnsToCouponsAndPromotions \
  --context PricingDbContext --output-dir Persistence/Migrations
dotnet ef migrations add ExtendProductTierPricesForCompanyOverrides \
  --context PricingDbContext --output-dir Persistence/Migrations
dotnet ef migrations add AddCommercialAuthoringTables \
  --context PricingDbContext --output-dir Persistence/Migrations
```

Read `data-model.md §2` for the exact column shapes; copy the SQL into the migrations as needed.

**Verify the warning suppression** in `PricingModule.cs`:
```csharp
services.AddDbContext<PricingDbContext>(o =>
{
    o.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__pricing_migrations"));
    o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)); // PROJECT-MEMORY RULE
});
```

Run integration tests; they should pass against the augmented schema with no behavioral change yet.

---

## 2. Reference seeder (Phase C)

`services/backend_api/Modules/Pricing/Seeding/PricingThresholdsSeeder.cs`:

```csharp
public sealed class PricingThresholdsSeeder : IModuleSeeder
{
    public string Name => "pricing.thresholds";

    public async Task<SeedResult> SeedAsync(SeedContext ctx, CancellationToken ct)
    {
        var rows = new[]
        {
            CommercialThreshold.Default("SA", coupon: 1800, promotion: 1800,
                pctOff: 30, amountOff: 5_000_000, durationDays: 14),
            CommercialThreshold.Default("EG", coupon: 1800, promotion: 1800,
                pctOff: 30, amountOff: 25_000_000, durationDays: 14),
        };
        // Upsert by primary key market_code; idempotent.
        return await ctx.UpsertAsync(rows, x => x.MarketCode, ct);
    }
}
```

Register in `PricingModule.cs` so `seed --mode=apply` runs it.

Smoke:
```bash
seed --dataset=pricing.thresholds --mode=dry-run
seed --dataset=pricing.thresholds --mode=apply
psql -d dental -c "SELECT market_code, gate_enabled, threshold_percent_off FROM pricing.commercial_thresholds;"
```

Expect 2 rows; both `gate_enabled=true`; both `threshold_percent_off=30`.

---

## 3. Cross-module shared declarations (Phase D)

Create the four interface files under `Modules/Shared/` per `contracts §12` / `data-model §7`. **No implementation in this phase** — these are just contract declarations.

Once merged, spec 005 / 021 / 010 / 025 implementers can pull them and start authoring their PRs in parallel.

---

## 4. First slice — `CreateCoupon` (Phase E)

Goal: get one full slice green end-to-end before fanning out.

### 4.1 Files

```
Modules/Pricing/Admin/Coupons/CreateCoupon/
├── Endpoint.cs                      # POST /v1/admin/commercial/coupons
├── Request.cs                       # CreateCouponRequest (matches contracts §2.1)
├── Response.cs                      # CreateCouponResponse
├── Handler.cs                       # MediatR IRequestHandler<...>
├── Validator.cs                     # FluentValidation: bilingual labels, schedule, value, markets, usage limits
└── Mapper.cs                        # Domain ↔ DTO
```

### 4.2 Handler outline

```csharp
public sealed class CreateCouponHandler : IRequestHandler<CreateCouponCommand, CreateCouponResult>
{
    private readonly PricingDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _time;
    private readonly ICurrentActor _actor;

    public async Task<CreateCouponResult> Handle(CreateCouponCommand cmd, CancellationToken ct)
    {
        // 1. Uniqueness check: WHERE UPPER(code) = UPPER(@code) — let the unique index catch concurrent inserts
        var exists = await _db.Coupons.AnyAsync(c => c.CodeUpper == cmd.Code.ToUpperInvariant(), ct);
        if (exists) return CreateCouponResult.Fail(CommercialReasonCode.CouponCodeDuplicate);

        // 2. Build the entity in `draft` state with full lifecycle metadata
        var coupon = Coupon.NewDraft(cmd, _actor.Id, _time.GetUtcNow());

        // 3. Persist + audit in one transaction
        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync(ct);
        await _audit.PublishAsync(new CouponCreatedAuditEvent(coupon.Id, _actor.Id, after: coupon.ToJson()), ct);

        return CreateCouponResult.Ok(coupon);
    }
}
```

### 4.3 Tests

In `tests/Pricing.Tests/Integration/Admin/Coupons/CreateCouponTests.cs`:

```csharp
[Fact]
public async Task Creates_Draft_With_Lifecycle_Metadata()
{
    // arrange: WebApplicationFactory + Testcontainers Postgres + commercial.operator user
    var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", validRequest);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await resp.Content.ReadFromJsonAsync<CreateCouponResponse>();
    created!.State.Should().Be("draft");
    var auditRows = await GetCommercialAuditEvents(created.Id);
    auditRows.Should().ContainSingle(r => r.Kind == "coupon.created");
}

[Fact]
public async Task Rejects_Duplicate_Code_CaseInsensitive()
{
    await client.PostAsJsonAsync("/v1/admin/commercial/coupons", new { code = "WELCOME10", ...});
    var resp = await client.PostAsJsonAsync("/v1/admin/commercial/coupons", new { code = "welcome10", ...});
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("coupon.code.duplicate");
}
```

### 4.4 Smoke (manual)

```bash
dotnet run --project services/backend_api
# In another shell, with a commercial.operator JWT:
curl -X POST http://localhost:5000/v1/admin/commercial/coupons \
  -H "Authorization: Bearer $JWT" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d @samples/create-coupon.json
```

Repeat for the remaining 7 coupon slices; structure each identically.

---

## 5. First Promotion slice — `CreatePromotion` (Phase F)

Mirror of §4 against `Modules/Pricing/Admin/Promotions/CreatePromotion/`. The non-trivial extra is the SKU-overlap warning logic in `Handler.cs`:

```csharp
// On Schedule (not Create), if stacks_with_other_promotions=false:
var overlaps = await _db.Promotions
    .Where(p => p.State == LifecycleState.Active || p.State == LifecycleState.Scheduled)
    .Where(p => p.AppliesTo.Overlaps(cmd.AppliesTo))
    .Select(p => p.Id)
    .ToListAsync(ct);

if (overlaps.Count > 0 && !cmd.AcknowledgeOverlap)
    return Fail(CommercialReasonCode.PromotionOverlapWarning, new { overlapping_rule_ids = overlaps });
```

---

## 6. First Business Pricing slice — `EditTierRow` (Phase G)

`Modules/Pricing/Admin/BusinessPricing/EditTierRow/`. Note the XOR check constraint in the DB does the heavy lifting — your handler just sets `tier_id` and leaves `company_id` null.

For `BulkImportTierRows`, two endpoints land:
1. `/preview` — parses CSV, computes the parsed-effect report, persists a transient `bulk_import_previews` row with the snapshot ETag, returns a 15-min-TTL `preview_token`.
2. `/commit` — accepts the token + `idempotency_key`; verifies the underlying tier rows have not changed (ETag match); commits in a single transaction; emits `business_pricing.bulk_imported`.

---

## 7. Campaigns slices + banner-link lookup (Phase H)

The non-trivial part is the `CampaignLinkBrokenWatcher` subscriber: when a `CouponDeactivated` or `PromotionDeactivated` event fires, it updates `pricing.campaign_links.link_broken_at_utc=now()` and `pricing.campaigns.link_broken=true` for any campaign that has the deactivated rule as its target.

The banner-link lookup endpoint (`GET /v1/admin/commercial/campaigns/lookups`) is consumed by spec 024's CMS banner editor; spec 024 does not exist on `main` yet, so the lookup ships behind a feature-flag-free public endpoint with no consumer at first — perfectly fine.

---

## 8. PreviewProfile slices + Preview tool (Phase I)

The Preview tool is the most consequential single endpoint. Implementation outline:

```csharp
public sealed class PreviewPriceExplanationHandler
    : IRequestHandler<PreviewPriceExplanationCommand, PreviewPriceExplanationResult>
{
    private readonly IPriceCalculator _calc;            // 007-a; do NOT modify
    private readonly IPreviewProfileQuery _profiles;
    private readonly IInFlightRuleOverlay _overlay;     // 007-a's preview-mode hook

    public async Task<PreviewPriceExplanationResult> Handle(...)
    {
        var profile = await _profiles.LoadAsync(cmd.PreviewProfileId, ct);

        // Run engine WITHOUT the in-flight rule
        var without = await _calc.CalculateAsync(BuildCtx(profile, overlayRule: null), ct);

        // Run engine WITH the in-flight rule (overlay scoped to this call)
        using (_overlay.Push(cmd.InFlightRule))
        {
            var with = await _calc.CalculateAsync(BuildCtx(profile, overlayRule: cmd.InFlightRule), ct);
            return BuildResult(profile, with, without);
        }
    }
}
```

Verify with the integration test `Preview_Output_Matches_Runtime_For_Same_Profile` (research §R2 verification hook).

p95 budget check (load test in `tests/Pricing.Tests/Integration/Performance/`):

```csharp
[Fact]
public async Task Preview_p95_Under_200ms_For_20Line_Cart()
{
    var samples = new List<long>();
    for (int i = 0; i < 100; i++)
    {
        var sw = Stopwatch.StartNew();
        await client.PostAsJsonAsync("/v1/admin/commercial/preview/price-explanation", payload20Lines);
        samples.Add(sw.ElapsedMilliseconds);
    }
    samples.OrderBy(x => x).ElementAt(95).Should().BeLessThan(200);
}
```

---

## 9. Approval gate (Phase J)

The `RecordApproval` handler must:
1. Reject `403 commercial.self_approval.forbidden` if `_actor.Id == draft.AuthorActorId`.
2. Insert into `commercial_approvals` with the unique `(target_kind, target_id)` constraint catching layer-2 races.
3. In the same transaction, advance the draft via the existing `ScheduleCoupon` / `SchedulePromotion` handler.

Test the race:
```csharp
[Fact]
public async Task TwoApprovers_Concurrent_Only_One_ActivationRecorded()
{
    // arrange: a draft above threshold
    var (approver1Token, approver2Token) = (...);
    var t1 = client.PostAsJsonAsync(".../approvals", payload, approver1Token);
    var t2 = client.PostAsJsonAsync(".../approvals", payload, approver2Token);
    await Task.WhenAll(t1, t2);

    var responses = (await t1, await t2);
    var oks = new[] { responses.Item1, responses.Item2 }.Count(r => r.IsSuccessStatusCode);
    oks.Should().Be(1);
    var coupon = await GetCoupon(draft.Id);
    coupon.State.Should().Be("active");
    var approvals = await GetApprovals(draft.Id);
    approvals.Should().HaveCount(1);
}
```

---

## 10. Workers (Phase N)

### `LifecycleTimerWorker.cs`

```csharp
public sealed class LifecycleTimerWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly TimeProvider _time;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var scope = _sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            // Advisory lock; non-blocking
            var locked = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(hashtext('pricing.lifecycle_timer')) AS \"Value\"")
                .FirstAsync(ct);

            if (locked)
            {
                try
                {
                    await TickAsync(db, _time.GetUtcNow(), ct);
                }
                finally
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_unlock(hashtext('pricing.lifecycle_timer'))", ct);
                }
            }

            await Task.Delay(TickInterval, _time, ct);
        }
    }

    private static Task TickAsync(PricingDbContext db, DateTimeOffset nowUtc, CancellationToken ct) =>
        // Execute the four SQL UPDATEs from research §R1 in one transaction.
        db.Database.ExecuteSqlRawAsync("/* ... */", ct);
}
```

Time-driven test:
```csharp
[Fact]
public async Task LifecycleTimerWorker_Drift_Within_60s()
{
    // schedule 100 coupons with valid_from = now + 30s
    fakeTime.Advance(TimeSpan.FromSeconds(90));
    await worker.TriggerOneTickAsync();
    var rows = await db.Coupons.Where(c => c.State == LifecycleState.Active).CountAsync();
    rows.Should().Be(100);
}
```

### `BrokenReferenceAutoDeactivationWorker.cs`

Daily; predicate per research §R13. Should reuse the `DeactivateCoupon` / `DeactivatePromotion` handlers rather than open-coding the state transition (preserves audit-row parity).

---

## 11. `promotions-v1` dev seeder (Phase S)

`Modules/Pricing/Seeding/PromotionsV1DevSeeder.cs` — `SeedGuard` so it never runs in production:

```csharp
public sealed class PromotionsV1DevSeeder : IModuleSeeder
{
    public string Name => "promotions.v1";
    public bool RunInProduction => false; // SeedGuard

    public async Task<SeedResult> SeedAsync(SeedContext ctx, CancellationToken ct)
    {
        // 6 coupons (one per state × {percent_off, amount_off})
        // 4 promotions
        // 3 tier rows + 2 company overrides
        // 3 campaigns
        // All bilingual editorial-grade labels (flag in AR_EDITORIAL_REVIEW.md until reviewed)
    }
}
```

Smoke:
```bash
seed --dataset=promotions.v1 --mode=apply
psql -d dental -c "SELECT state, COUNT(*) FROM pricing.coupons GROUP BY state;"
```

Expect ≥ 1 row per state for Coupons + Promotions.

---

## 12. Tests checklist (per spec — `tests/Pricing.Tests/`)

- [ ] Unit: lifecycle state machine (every valid + invalid transition).
- [ ] Unit: business-pricing state machine.
- [ ] Unit: high-impact gate (each criterion individually + combined).
- [ ] Unit: threshold-policy resolution (per market, with NULLs).
- [ ] Unit: reason-code mapper (every code has an ICU key in both locales — see R10 verification hook).
- [ ] Integration: every authoring slice (Acceptance Scenarios from spec.md, all 7 user stories).
- [ ] Integration: optimistic-concurrency edit guard (TwoOperators_EditingSameRule_409).
- [ ] Integration: preview p95 ≤ 200 ms (R2 + SC-002).
- [ ] Integration: lifecycle timer drift ≤ 60 s (R1 + SC-005).
- [ ] Integration: broken-reference auto-deactivation (R13).
- [ ] Integration: cross-module subscriber tests (R3) using fake publishers.
- [ ] Integration: in-flight grace event payload (R4) — assert `in_flight_grace_seconds` in deactivation event.
- [ ] Integration: bulk-import preview-then-commit + token expiry (R7).
- [ ] Integration: thresholds seeder idempotent + post-seed gate behavior (R8).
- [ ] Integration: approval gate concurrency (R12).
- [ ] Contract: every reason code reachable from a real handler path.
- [ ] Contract: every spec.md Acceptance Scenario (7 stories × ~5 scenarios = ~35 contract tests).
- [ ] Audit-coverage script (SC-003): 100 % of operator actions produce a `commercial_audit_events` row + an `audit_log_entries` row.
- [ ] AR editorial review pass (R17).
- [ ] OpenAPI artifact regenerated and committed (R18).

---

## 13. Definition of Done (per spec)

Before opening the PR:
- [ ] `dotnet test services/backend_api/tests/Pricing.Tests/` is green.
- [ ] `dotnet build services/backend_api/services.sln` is warning-free.
- [ ] `seed --dataset=pricing.thresholds --mode=apply` is idempotent (run twice, no duplicate rows).
- [ ] `seed --dataset=promotions.v1 --mode=apply` produces the expected per-state distribution (R8 + S).
- [ ] `LifecycleTimerWorker` drift test passes (SC-005).
- [ ] Preview p95 load test passes (SC-002).
- [ ] AR strings in `AR_EDITORIAL_REVIEW.md` flagged for review (R17 — does not block the PR; blocks the launch).
- [ ] OpenAPI artifact in `openapi.pricing.commercial.json` regenerated; PR diff reviewed.
- [ ] Constitution + ADR fingerprint computed via `scripts/compute-fingerprint.sh` and pasted into the PR body.
- [ ] DoD checklist from `docs/dod.md` completed.
- [ ] Manual smoke through Postman / curl for one slice from each top-level surface (coupon, promotion, business-pricing, campaign, preview, approval, threshold).
- [ ] PR description references this spec and the 5 clarification answers.

The PR is ready to merge when CI is green, the design-review checklist is signed off, and the constitution + ADR fingerprint matches the locked v1.0.0 state.
