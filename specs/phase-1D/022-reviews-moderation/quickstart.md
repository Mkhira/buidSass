# Quickstart: Reviews & Moderation (Spec 022)

**Date**: 2026-04-28
**Audience**: implementer picking up the 022 PR(s) on the `phase_1D_creating_specs` branch.
**Prerequisites**: 011 `orders` at DoD with `IOrderLineDeliveryEligibilityQuery`; 015 `admin-foundation` contract merged; 003 platform middleware (`Idempotency-Key`, `RowVersion`, `IAuditEventPublisher`, `MessageFormat.NET`, rate-limit middleware) in place; 004 `identity-and-access` permissions surface live; 006 `search` `IArabicNormalizer` made public.

This guide walks you from a clean checkout to a green PR. Each section maps to a phase from `plan.md §Implementation Phases`.

---

## 0. Prerequisites

```bash
# Confirm the 6 cross-module dependencies are merged
ls services/backend_api/Modules/Orders services/backend_api/Modules/Returns
ls services/backend_api/Modules/Search/Internal/IArabicNormalizer.cs

# Run existing test suite to confirm green baseline
dotnet test services/backend_api/services.sln
```

If any cross-module dependency is missing on `main`, 022 ships against a documented contract stub + fakes (the integration tests inject the fake) — but the production wiring requires the real implementation.

---

## 1. Module wiring (Phases A + B)

### 1.1 Module skeleton

Create `services/backend_api/Modules/Reviews/`. Add `ReviewsModule.cs`:

```csharp
public static class ReviewsModule
{
    public static IServiceCollection AddReviewsModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ReviewsDbContext>(o =>
        {
            o.UseNpgsql(config.GetConnectionString("Default"),
                npg => npg.MigrationsHistoryTable("__reviews_migrations", "reviews"));
            o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)); // PROJECT-MEMORY
        });

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ReviewsModule>());

        services.AddScoped<IRatingAggregateReader, RatingAggregateReader>();
        services.AddSingleton<ProfanityFilter>();
        services.AddHostedService<RatingAggregateRebuildWorker>();
        services.AddHostedService<ReviewIntegrityScanWorker>();

        services.AddScoped<IRefundCompletedSubscriber, RefundCompletedHandler>();
        services.AddScoped<IRefundReversedSubscriber, RefundReversedHandler>();
        services.AddScoped<ICustomerAccountLifecycleSubscriber, CustomerAccountLifecycleHandler>();

        // Permissions
        services.RegisterReviewsPermissions();

        return services;
    }
}
```

Wire from `Program.cs`: `services.AddReviewsModule(builder.Configuration);`.

### 1.2 Primitives (Phase A)

Create `Modules/Reviews/Primitives/` files per `plan.md`:
- `ReviewState.cs` (5-state enum)
- `ReviewStateMachine.cs` (`TryTransition(from, trigger, nowUtc, out to, out reasonCode)`)
- `ReviewActorKind.cs`
- `ReviewReasonCode.cs` (35 codes from contract §10)
- `ReviewMarketPolicy.cs`
- `QualifiedReporterPolicy.cs` (pure: `Evaluate(reporter, marketSchema) → bool`)
- `ReviewerDisplayRenderer.cs` (FR-016a: `Render(handle?, firstName, lastName) → string`)
- `ReviewTriggerKind.cs`

Tests for each primitive in `tests/Reviews.Tests/Unit/Primitives/`.

### 1.3 Migrations (Phase B)

```bash
cd services/backend_api/Modules/Reviews
dotnet ef migrations add CreateReviewsSchemaAndTables \
  --context ReviewsDbContext --output-dir Persistence/Migrations
```

Manually adjust the migration to:
- `CREATE SCHEMA reviews;`
- `CREATE TYPE reviews.review_state AS ENUM (...)`.
- All 7 tables with their unique partial indexes + check constraints (data-model §2).
- The `raise_immutable_audit_violation()` function (or reuse if present).
- `BEFORE UPDATE OR DELETE` triggers on the 3 append-only tables.

Run integration tests to confirm migrations apply cleanly on Testcontainers Postgres.

---

## 2. Reference seeder (Phase C)

`Modules/Reviews/Seeding/ReviewsReferenceDataSeeder.cs`:

```csharp
public sealed class ReviewsReferenceDataSeeder : IModuleSeeder
{
    public string Name => "reviews.reference";
    public bool RunInProduction => true;

    public async Task<SeedResult> SeedAsync(SeedContext ctx, CancellationToken ct)
    {
        // 1. KSA + EG market schemas
        var schemas = new[] {
            ReviewsMarketSchema.Default("SA"),
            ReviewsMarketSchema.Default("EG"),
        };
        await ctx.UpsertAsync(schemas, x => x.MarketCode, ct);

        // 2. Per-market profanity wordlists (initial seed; admins maintain via PolicyAdmin endpoints)
        var wordlists = LoadInitialWordlists(); // EN + AR seed terms
        await ctx.UpsertAsync(wordlists, x => new { x.MarketCode, x.Term }, ct);

        return SeedResult.Ok($"seeded {schemas.Length} market schemas + {wordlists.Length} wordlist terms");
    }
}
```

Smoke:
```bash
seed --dataset=reviews.reference --mode=apply
psql -d dental -c "SELECT market_code, eligibility_window_days FROM reviews.reviews_market_schemas;"
psql -d dental -c "SELECT market_code, COUNT(*) FROM reviews.reviews_filter_wordlists GROUP BY market_code;"
```

---

## 3. Cross-module shared declarations (Phase D)

Create the 7 interface files under `Modules/Shared/` per data-model §7. Once merged, specs 011 / 013 / 005 / 019 / 025 implementers can pull and start their PRs in parallel.

Note: the existing `ICustomerAccountLifecycleSubscriber` from spec 020 is reused; do NOT redeclare. Just register a new handler.

---

## 4. First slice — `SubmitReview` (Phase F)

The MVP slice. Files:

```
Modules/Reviews/Customer/SubmitReview/
├── Endpoint.cs                      # POST /v1/customer/reviews
├── Request.cs                       # SubmitReviewRequest (matches contract §2.1)
├── Response.cs
├── Handler.cs                       # MediatR IRequestHandler
├── Validator.cs                     # FluentValidation
└── Mapper.cs
```

### Handler outline:

```csharp
public sealed class SubmitReviewHandler : IRequestHandler<SubmitReviewCommand, SubmitReviewResult>
{
    private readonly ReviewsDbContext _db;
    private readonly IOrderLineDeliveryEligibilityQuery _eligibility;
    private readonly ProfanityFilter _filter;
    private readonly MediaAttachmentDetector _mediaDetector;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    private readonly TimeProvider _time;
    private readonly ICurrentActor _actor;
    private readonly ReviewMarketPolicy _policyResolver;
    private readonly RatingAggregateRecomputer _aggregateRecomputer;

    public async Task<SubmitReviewResult> Handle(SubmitReviewCommand cmd, CancellationToken ct)
    {
        // 1. Eligibility
        var elig = await _eligibility.IsEligibleForReviewAsync(_actor.CustomerId, cmd.ProductId, ct);
        if (!elig.Eligible)
            return SubmitReviewResult.Fail(elig.ReasonCode!);

        var policy = await _policyResolver.GetAsync(cmd.MarketCode, ct);

        // 2. Eligibility window
        if (_time.GetUtcNow() - elig.DeliveredAt!.Value > TimeSpan.FromDays(policy.EligibilityWindowDays))
            return SubmitReviewResult.Fail(ReviewReasonCode.EligibilityWindowClosed);

        // 3. Uniqueness — let the unique partial index catch concurrent inserts
        // (no pre-check; rely on DB constraint)

        // 4. Filter + media
        var filterResult = _filter.Scan(cmd.Body, cmd.Headline, cmd.MarketCode);
        var hasMedia = _mediaDetector.HasMedia(cmd.MediaUrls);

        var initialState = (filterResult.Tripped || hasMedia) ? ReviewState.PendingModeration : ReviewState.Visible;
        var review = Review.NewSubmission(cmd, _actor, _time.GetUtcNow(), elig.OrderLineId!.Value,
            elig.DeliveredAt.Value, initialState, filterResult.MatchedTerms, hasMedia);

        _db.Reviews.Add(review);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ux_reviews_customer_product_active"))
        {
            return SubmitReviewResult.Fail(ReviewReasonCode.AlreadyReviewed);
        }

        // 5. Audit + denormalized decision row
        await _audit.PublishAsync(new ReviewSubmittedAuditEvent(review.Id, _actor.Id, after: review.ToJson()), ct);

        // 6. Aggregate refresh (only if state Visible)
        if (initialState == ReviewState.Visible)
            await _aggregateRecomputer.RefreshAsync(review.ProductId, review.MarketCode, ct);

        // 7. Domain events (after commit)
        await _bus.Publish(new ReviewSubmitted(review.Id, /* ... */), ct);
        if (initialState == ReviewState.Visible)
            await _bus.Publish(new ReviewPublished(review.Id, /* ... */), ct);
        else
            await _bus.Publish(new ReviewHeldForModeration(review.Id, /* ... */), ct);

        return SubmitReviewResult.Ok(review);
    }
}
```

### Tests:

```csharp
[Fact]
public async Task Submit_HappyPath_PublishesVisible()
{
    var resp = await _client.PostAsJsonAsync("/v1/customer/reviews", validRequest);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await resp.Content.ReadFromJsonAsync<SubmitReviewResponse>();
    body!.State.Should().Be("visible");
    body.PendingReview.Should().BeFalse();
}

[Fact]
public async Task Submit_NoDeliveredOrderLine_Returns400()
{
    _fakeEligibility.SetIneligible("review.eligibility.no_delivered_purchase");
    var resp = await _client.PostAsJsonAsync("/v1/customer/reviews", validRequest);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync()).Should().Contain("review.eligibility.no_delivered_purchase");
}

[Fact]
public async Task Submit_FilterTrips_HoldsForModeration()
{
    var resp = await _client.PostAsJsonAsync("/v1/customer/reviews", requestWithProfanity);
    var body = await resp.Content.ReadFromJsonAsync<SubmitReviewResponse>();
    body!.State.Should().Be("pending_moderation");
    body.PendingReview.Should().BeTrue();
}

[Fact]
public async Task Submit_WithMediaCleanText_HoldsForModeration()
{
    var resp = await _client.PostAsJsonAsync("/v1/customer/reviews", requestWithMediaAndCleanText);
    var body = await resp.Content.ReadFromJsonAsync<SubmitReviewResponse>();
    body!.State.Should().Be("pending_moderation");
}
```

---

## 5. Edit slice (Phase F continued)

`UpdateReview/` re-runs the filter + media detection on the edit; if either trips, transitions to `pending_moderation` and re-stamps `pending_moderation_started_at`. EF Core's optimistic-concurrency advances `xmin` on save, invalidating any concurrent moderator decision.

Test:
```csharp
[Fact]
public async Task Edit_DuringPending_AdvancesRowVersion_AndModeratorDecisionFails()
{
    // arrange: review in pending_moderation; moderator reads with row_version v1
    // act 1: customer edits → row_version → v2
    // act 2: moderator submits decide with If-Match: v1
    // assert: 409 reviews.moderation.version_conflict
}
```

---

## 6. Reporting slice (Phase G)

`ReportReview/` flow:
1. Verify caller is signed in (else 401).
2. Verify caller is NOT the review's author.
3. Evaluate `QualifiedReporterPolicy` against the caller; capture `is_qualified` snapshot on the row.
4. Insert `ReviewFlag` (let the unique constraint catch double-reports).
5. Re-count qualified reports within the window; if `>= threshold` AND review is currently `visible`, transition to `flagged`.

Test the threshold:
```csharp
[Fact]
public async Task Report_ThirdQualifiedReporterWithin30Days_FlagsReview()
{
    // arrange: visible review; 2 prior qualified reports
    // act: 3rd qualified reporter reports
    // assert: review state = flagged; ReviewFlagged event fired
}

[Fact]
public async Task Report_UnqualifiedReporter_DoesNotIncrementCounter()
{
    // arrange: visible review; 2 prior qualified reports
    // act: unqualified reporter reports (account < 14 days, no delivered orders)
    // assert: review state stays visible; flag persisted but is_qualified=false
}
```

---

## 7. Admin moderation slices (Phase H)

`DecideModeration/` slice — the heart of the queue:

```csharp
public async Task<DecideModerationResult> Handle(DecideModerationCommand cmd, CancellationToken ct)
{
    // RBAC: super_admin required for to_state=deleted
    if (cmd.ToState == ReviewState.Deleted && !_actor.IsSuperAdmin)
        return Fail(ReviewReasonCode.DeleteRequiresSuperAdmin);

    var review = await _db.Reviews.FindByVersionAsync(cmd.Id, cmd.ExpectedRowVersion, ct);
    if (review is null)
        return Fail(ReviewReasonCode.VersionConflict);

    if (!ReviewStateMachine.TryTransition(review.State, cmd.ToState, _time.GetUtcNow(), ReviewTriggerKind.ModeratorAction, out var error))
        return Fail(error);

    // Validate notes
    if ((cmd.ToState == ReviewState.Hidden || cmd.ToState == ReviewState.Deleted) && string.IsNullOrWhiteSpace(cmd.ReasonNote))
        return Fail(ReviewReasonCode.ReasonRequired);
    if (cmd.ToState == ReviewState.Visible && string.IsNullOrWhiteSpace(cmd.AdminNote))
        return Fail(ReviewReasonCode.AdminNoteRequired);

    var fromState = review.State;
    review.Decide(cmd.ToState, cmd.ReasonNote, cmd.AdminNote, _actor.Id, _time.GetUtcNow());

    _db.ReviewModerationDecisions.Add(ReviewModerationDecision.Record(review, fromState, _actor, /*...*/));
    await _db.SaveChangesAsync(ct);

    // Audit + aggregate + events
    await _audit.PublishAsync(/* mapped kind */, ct);
    if (fromState != cmd.ToState && (CountedIn(fromState) || CountedIn(cmd.ToState)))
        await _aggregateRecomputer.RefreshAsync(review.ProductId, review.MarketCode, ct);
    await _bus.Publish(/* mapped event */, ct);

    return Ok(review);
}
```

---

## 8. Aggregate slices + recomputer (Phase I)

`RatingAggregateRecomputer.RefreshAsync(productId, marketCode, ct)`:

```sql
INSERT INTO reviews.product_rating_aggregates (product_id, market_code, avg_rating, review_count,
    distribution_1, distribution_2, distribution_3, distribution_4, distribution_5, last_updated_utc)
SELECT
    @productId, @marketCode,
    NULLIF(AVG(rating)::numeric(3,2), 0),
    COUNT(*),
    COUNT(*) FILTER (WHERE rating = 1),
    COUNT(*) FILTER (WHERE rating = 2),
    COUNT(*) FILTER (WHERE rating = 3),
    COUNT(*) FILTER (WHERE rating = 4),
    COUNT(*) FILTER (WHERE rating = 5),
    now()
FROM reviews.reviews
WHERE product_id = @productId AND market_code = @marketCode AND state IN ('visible', 'flagged')
ON CONFLICT (product_id, market_code) DO UPDATE SET
    avg_rating = EXCLUDED.avg_rating,
    review_count = EXCLUDED.review_count,
    distribution_1 = EXCLUDED.distribution_1,
    distribution_2 = EXCLUDED.distribution_2,
    distribution_3 = EXCLUDED.distribution_3,
    distribution_4 = EXCLUDED.distribution_4,
    distribution_5 = EXCLUDED.distribution_5,
    last_updated_utc = EXCLUDED.last_updated_utc;
```

Public read endpoint `ReadProductRating/` is unauthenticated; cache header `Cache-Control: public, max-age=60`.

Latency test:
```csharp
[Fact]
public async Task Aggregate_RefreshWithin60s_AfterTransition()
{
    var initial = await _db.Aggregates.GetAsync(productId, "SA");
    await _client.PostAsJsonAsync("/v1/customer/reviews", validRequest); // visible
    await Task.Delay(TimeSpan.FromSeconds(2));
    var updated = await _db.Aggregates.GetAsync(productId, "SA");
    updated.LastUpdatedUtc.Should().BeAfter(initial?.LastUpdatedUtc ?? DateTimeOffset.MinValue);
}
```

---

## 9. Subscribers (Phase K)

`RefundCompletedHandler` runs on every `RefundCompletedEvent`:

```csharp
public async Task HandleAsync(RefundCompletedEvent e, CancellationToken ct)
{
    var affected = await _db.Reviews
        .Where(r => r.OrderLineId == e.OrderLineId && r.State == ReviewState.Visible || r.State == ReviewState.Flagged)
        .ToListAsync(ct);

    foreach (var r in affected)
    {
        r.AutoHide(triggeredBy: ReviewTriggerKind.RefundEvent, reasonNote: "auto_hidden:order_refunded", systemActor: SystemActor.Id, _time.GetUtcNow());
        _db.ReviewModerationDecisions.Add(ReviewModerationDecision.Record(r, /* ... */));
    }

    if (affected.Count > 0)
    {
        await _db.SaveChangesAsync(ct);
        foreach (var r in affected)
        {
            await _aggregateRecomputer.RefreshAsync(r.ProductId, r.MarketCode, ct);
            await _bus.Publish(new ReviewAutoHidden(r.Id, "refund_event", e.SourceEventId), ct);
            await _audit.PublishAsync(new ReviewAutoHiddenAuditEvent(r.Id, /* ... */), ct);
        }
    }
}
```

---

## 10. Workers (Phase L)

`RatingAggregateRebuildWorker` runs daily; rebuilds every aggregate row from scratch using the same SQL as the recomputer but iterated over all `(product_id, market_code)` pairs that have at least one review. Advisory-lock guarded.

`ReviewIntegrityScanWorker` runs daily; SC-004 verification:

```sql
SELECT r.id, r.product_id, r.customer_id
FROM reviews.reviews r
WHERE r.state IN ('visible', 'flagged')
  AND EXISTS (
    SELECT 1 FROM orders.refunds rf
    WHERE rf.order_line_id = r.order_line_id AND rf.state IN ('completed','approved')
  );
```

If results are non-empty, log to `reviews.integrity` channel + emit metric `reviews_integrity_violations_total{kind, market}`.

---

## 11. `reviews-v1` dev seeder (Phase Q)

`ReviewsV1DevSeeder.cs` (`SeedGuard.RunInProduction = false`):

- 30 visible reviews across 6 products and 5 ratings.
- 5 `pending_moderation` reviews (filter-tripped on seeded EN + AR terms).
- 4 `flagged` reviews (with seeded community reports from qualified reporters).
- 3 `hidden` reviews (with audit history showing prior visible state).
- 2 `deleted` reviews (with super_admin actor).
- All bilingual editorial-grade where AR — flag in `AR_EDITORIAL_REVIEW.md`.

Smoke:
```bash
seed --dataset=reviews-v1 --mode=apply
psql -d dental -c "SELECT state, COUNT(*) FROM reviews.reviews GROUP BY state ORDER BY state;"
```

Expect ≥ 1 row in each of the 5 states.

---

## 12. Tests checklist

- [ ] Unit: state machine (every valid + invalid transition; idempotency; no terminal exit).
- [ ] Unit: `QualifiedReporterPolicy` (account-age threshold, verified-buyer flag, per-market schema variants).
- [ ] Unit: `ReviewerDisplayRenderer` (handle present, handle null, last_name empty, AR-name boundary cases).
- [ ] Unit: `ProfanityFilter` (wordlist coverage matrix per market; AR normalization corner cases) — SC-010.
- [ ] Unit: reason-code mapper (every code resolves to non-empty `en` and `ar` ICU keys).
- [ ] Integration: every customer slice (Acceptance Scenarios from spec.md US1-US3, US7).
- [ ] Integration: every admin slice (US2 moderation, US4 hide/reinstate/delete).
- [ ] Integration: refund event auto-hide (US5).
- [ ] Integration: aggregate read endpoint (US6) — `review_count`, distribution, null `avg_rating` when zero.
- [ ] Integration: aggregate refresh latency ≤ 60 s (SC-005) under soak.
- [ ] Integration: edit during pending invalidates moderator decision (R9 verification hook).
- [ ] Integration: optimistic-concurrency on concurrent moderator decisions (FR-019).
- [ ] Integration: report idempotency on same `(reporter, review)` (FR-022).
- [ ] Integration: qualified-reporter threshold escalation (FR-023).
- [ ] Integration: account-locked auto-hide (FR-031) — reuses spec 020's lifecycle subscriber harness.
- [ ] Integration: hard-delete returns 405 always (FR-005a).
- [ ] Integration: rate-limit envelopes (submission, edit, report, moderation; FR-040, FR-041).
- [ ] Integration: integrity scan finds visible reviews on refunded order lines (SC-004).
- [ ] Contract: every spec.md Acceptance Scenario exercised against live handlers.
- [ ] Audit-coverage script (SC-003): 100 % of state transitions + reports + wordlist edits + threshold edits + admin notes have matching audit rows.
- [ ] AR editorial review pass (SC-007).
- [ ] OpenAPI artifact regenerated and committed.

---

## 13. Definition of Done

Before opening the PR:
- [ ] `dotnet test services/backend_api/tests/Reviews.Tests/` is green.
- [ ] `dotnet build services/backend_api/services.sln` is warning-free.
- [ ] `seed --dataset=reviews.reference --mode=apply` is idempotent.
- [ ] `seed --dataset=reviews-v1 --mode=apply` produces ≥ 1 row in each of 5 states; AR strings flagged in `AR_EDITORIAL_REVIEW.md`.
- [ ] Aggregate refresh-latency soak passes (SC-005).
- [ ] Profanity-filter coverage matrix passes (SC-010).
- [ ] Audit-coverage script reports 100 %.
- [ ] OpenAPI artifact in `openapi.reviews.json` regenerated; PR diff reviewed.
- [ ] Constitution + ADR fingerprint computed via `scripts/compute-fingerprint.sh` and pasted into the PR body.
- [ ] DoD checklist from `docs/dod.md` (DoD version 1.0) completed.
- [ ] Manual smoke through Postman / curl for one slice from each top-level surface (customer submit, customer edit, customer report, admin queue, admin decide, public aggregate read).

The PR is ready to merge when CI is green, the design-review checklist is signed off, and the constitution + ADR fingerprint matches the locked v1.0.0 state.
