# Quickstart: CMS

**Phase**: 1 (alongside data-model.md and contracts/cms-contract.md)
**Date**: 2026-04-29

This is the implementer walkthrough. Read it after `plan.md` + `data-model.md` + `contracts/cms-contract.md`. It shows the first end-to-end slice (author + publish a banner), the legal-page version-transition smoke, and the preview-token round-trip — enough to validate the module's architectural skeleton before `/speckit-tasks` expands the full task list.

---

## §0. Prerequisites

- Spec 015 admin-foundation contract merged to `main` (RBAC, audit panel, idempotency middleware, rate-limit middleware, storage abstraction with signed-URL upload).
- Spec 003 `audit-and-events` table + `IAuditEventPublisher` at DoD (it is — already on `main`).
- Spec 005 catalog read contracts (`ICatalogProductReadContract`, `ICatalogCategoryReadContract`, `ICatalogBundleReadContract`) at DoD on `main`. If not, ship 024 against the `Fake*ReadContract` stubs in `Modules/Shared/Testing/` and replace the bindings when 005 lands. The fake doubles MUST be removed in a single follow-up PR (recommended schedule: a routine cleanup in ~1 week).
- Phase 1E E1 Key Vault NOT required for V1 — the preview-token signing key is sourced from `appsettings.{env}.json` per environment in dev / staging until E1 ships; production rolls over to Key Vault as a single configuration swap.

## §1. First slice: author + schedule + publish a hero banner (User Story 1)

This is the smallest end-to-end slice that exercises the editor → publisher → worker path. It's the recommended first PR.

```text
1. Migrations: dotnet ef migrations add cms_initial -p Modules/Cms -s services/backend_api
   → creates `cms` schema + 9 tables + triggers
   → creates `CmsReferenceDataSeeder` invocation in Program.cs (runs on every env)
2. Sign in as a `cms.editor` actor
3. Upload two assets via spec 015 storage abstraction:
   POST /v1/admin/storage/signed-url-upload (×2 for ar + en)
   → returns { storage_object_id, upload_url }
   → upload binary to the signed URL
   → POST /v1/admin/cms/assets to register the asset in `cms.assets` (returns asset_id_ar, asset_id_en)
4. POST /v1/admin/cms/banner-slots/drafts
   { slot_kind: hero_top, headline_ar, headline_en, asset_id_ar, asset_id_en,
     cta_kind: category, cta_target: <category_uuid>,
     scheduled_start_utc, scheduled_end_utc, market_code: KSA }
   → 201 with full BannerSlot row, state=draft
5. (Optional, for stakeholder review) POST /v1/admin/cms/banner-slot/<id>/preview-token { ttl_hours: 24 }
   → returns signed URL; share with marketing lead
6. Sign in as a `cms.publisher` actor
7. POST /v1/admin/cms/banner-slots/<id>/schedule-publish
   → gates run: locale-completeness (FR-007), banner CTA validation against ICatalogCategoryReadContract,
     banner capacity check (FR-021a), idempotency-key envelope, xmin guard
   → 200 with state=scheduled
   → audit row `cms.content.scheduled` written
   → no domain event emitted yet (event fires at worker tick)
8. Wait for worker tick (or advance FakeTimeProvider in tests):
   → CmsScheduledPublishWorker scans rows where (state=scheduled AND scheduled_start_utc <= now())
   → transitions to live; stamps published_at_utc; emits cms.banner.published + cms.cache.invalidate.banner
   → audit row `cms.content.published` written with triggered_by=worker_promote_to_live
9. Storefront read:
   GET /v1/storefront/cms/banner-slots?market=KSA&locale=ar
   → returns the live banner with its `ar` headline + asset_id_ar
   → Cache-Control headers + ETag emitted
10. At scheduled_end_utc:
    → worker transitions to archived; stamps archived_at_utc; emits cms.banner.archived
    → storefront read no longer returns the banner
```

### Code skeleton (Editor slice)

```csharp
// Modules/Cms/Editor/SaveBannerDraft/Command.cs
public sealed record SaveBannerDraftCommand(
    Guid? Id,                                 // null for create
    string SlotKind,
    string? HeadlineAr, string? HeadlineEn,
    string? SubheadAr, string? SubheadEn,
    Guid? AssetIdAr, Guid? AssetIdEn,
    string CtaKind,
    string? CtaTarget,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    string MarketCode,
    int PriorityWithinSlot,
    string? IdempotencyKey,
    uint? Xmin                               // required on update
) : IRequest<SaveBannerDraftResult>;

// Modules/Cms/Editor/SaveBannerDraft/Handler.cs
public sealed class SaveBannerDraftHandler : IRequestHandler<SaveBannerDraftCommand, SaveBannerDraftResult>
{
    public SaveBannerDraftHandler(
        CmsDbContext db,
        ICurrentActor actor,
        IAuditEventPublisher audit,
        TimeProvider clock) { ... }

    public async Task<SaveBannerDraftResult> Handle(SaveBannerDraftCommand cmd, CancellationToken ct)
    {
        // 1. Validate basic shape (FluentValidation pipeline)
        // 2. Resolve or create row
        var row = cmd.Id is null ? new BannerSlot() : await _db.BannerSlots.FirstOrFailAsync(cmd.Id.Value, ct);
        if (cmd.Id is not null && row.State != ContentLifecycleState.Draft)
            throw new CmsApiException("cms.draft.not_editable", 400);
        if (cmd.Id is not null && row.Xmin != cmd.Xmin)
            throw new CmsApiException("cms.draft.version_conflict", 409);

        // 3. Validate banner-specific gates (CTA-kind/target match, schedule window)
        BannerValidator.AssertScheduleWindow(cmd.ScheduledStartUtc, cmd.ScheduledEndUtc);
        BannerValidator.AssertCtaShape(cmd.CtaKind, cmd.CtaTarget);
        BannerValidator.AssertExternalUrlHttps(cmd.CtaKind, cmd.CtaTarget);

        // 4. Apply changes
        row.Apply(cmd, _actor.Id, _clock.GetUtcNow());

        // 5. Persist + audit
        if (cmd.Id is null) _db.BannerSlots.Add(row);
        await _db.SaveChangesAsync(ct);
        await _audit.PublishAsync(row.Id == default
            ? CmsAuditEvent.DraftCreated(row, _actor)
            : CmsAuditEvent.DraftUpdated(row, _actor),
            ct);

        return new SaveBannerDraftResult(row);
    }
}
```

### Code skeleton (Publisher slice — publish-now path)

```csharp
// Modules/Cms/Publisher/PublishNow/Handler.cs
public async Task<PublishNowResult> Handle(PublishNowCommand cmd, CancellationToken ct)
{
    // 1. Load row + xmin
    var row = await _db.LoadByEntityKindAsync(cmd.EntityKind, cmd.Id, ct);
    if (row.State != ContentLifecycleState.Draft && row.State != ContentLifecycleState.Scheduled)
        throw new CmsApiException("cms.{kind}.archive_forbidden_in_state", 405);

    // 2. Locale-completeness gate
    var gate = _localeGate.CheckPublishable(row);
    if (gate is BlockedReason r) throw new CmsApiException(r.Code, 400);

    // 3. Banner-specific gates (capacity + CTA validation)
    if (row is BannerSlot banner)
    {
        await _ctaValidator.ValidateAsync(banner, ct);  // throws cms.banner.cta_target_unresolvable on hard failure
        await _capacityCalc.AssertCapacityAvailableAsync(banner, ct);  // throws cms.banner.slot_capacity_exceeded
    }

    // 4. Featured-section non-empty refs
    if (row is FeaturedSection fs && (fs.References is null || fs.References.Count == 0))
        throw new CmsApiException("cms.featured_section.empty_references", 400);

    // 5. Apply transition
    row.TransitionTo(ContentLifecycleState.Live, _actor, _clock, triggeredBy: TriggerKind.PublisherPublishNow);
    await _db.SaveChangesAsync(ct);

    // 6. Audit + domain events
    await _audit.PublishAsync(CmsAuditEvent.ContentPublished(row, _actor), ct);
    await _bus.PublishAsync(CmsEvents.PublishedFor(row), ct);
    await _bus.PublishAsync(CmsEvents.CacheInvalidateFor(row), ct);

    return new PublishNowResult(row);
}
```

### Code skeleton (Worker — scheduled-publish promotion)

```csharp
// Modules/Cms/Workers/CmsScheduledPublishWorker.cs
public sealed class CmsScheduledPublishWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            // Postgres advisory lock per worker class (project pattern from spec 020)
            await using var lockHandle = await db.TryAcquireAdvisoryLockAsync("cms-scheduled-publish", ct);
            if (lockHandle is null) { await Task.Delay(_period, ct); continue; }

            await PromoteScheduledToLiveAsync(db, ct);
            await PromoteLiveToArchivedAsync(db, ct);    // banner only, on scheduled_end_utc
            await SupersedeLegalVersionsAsync(db, ct);    // legal_page_version supersession in single txn
            await Task.Delay(_period, ct);
        }
    }
}
```

---

## §2. Legal page version transition smoke (User Story 3)

```text
1. Sign in as `cms.legal_owner`
2. POST /v1/admin/cms/legal-pages/privacy/versions/drafts
   { version_label: "2.3.0", body_ar: <markdown>, body_en: <markdown>,
     effective_at_utc: "<future-effective-date>", market_code: "KSA" }
   → 201 with state=draft
3. POST /v1/admin/cms/legal-pages/privacy/versions/<id>/preview-token
   → review with legal counsel via the storefront preview URL
4. POST /v1/admin/cms/legal-pages/privacy/versions/<id>/schedule-publish
   { scheduled_publish_at_utc: <effective_at_utc> }   // schedule equals effective_at_utc
   → 200 with state=scheduled
5. Wait for worker tick (or advance FakeTimeProvider):
   → in a single transaction:
     - new version: state scheduled → live; published_at_utc stamped
     - prior live version (same legal_page_kind + market_code): state live → superseded
       with superseded_at_utc + superseded_by_version_id stamped
   → emits cms.legal_page.version.published + cms.legal_page.version.superseded
6. Storefront read:
   GET /v1/storefront/cms/legal-pages/privacy?market=KSA&locale=ar
   → returns the new version's body_ar
7. Admin read:
   GET /v1/admin/cms/legal-pages/privacy/versions?market_code=KSA
   → returns BOTH versions (live + superseded) ordered by effective_at_utc DESC
8. DELETE attempt against the prior version's id:
   DELETE /v1/admin/cms/legal-pages/privacy/versions/<prior_id>
   → 405 cms.legal_page.version.delete_forbidden
```

---

## §3. Preview-token round-trip (User Story 4)

```text
1. POST /v1/admin/cms/blog-articles/<id>/preview-token { ttl_hours: 24 }
   → 201 returns { token: "<opaque>", url: "https://.../preview/...?token=...", expires_at_utc }
   → the token's sha256 hash + claims persist in cms.preview_tokens
2. Open the URL in an incognito browser (no auth):
   GET /v1/storefront/cms/preview/blog-article/<id>?token=...
   → server: PreviewTokenSigner.Verify(token) — HMAC-SHA256 check (constant-time)
   → server: load row from cms.preview_tokens by token_hash; check expires_at_utc > now() AND revoked_at_utc IS NULL
   → server: load draft entity; render with X-Robots-Tag: noindex, nofollow
   → 200 with `preview_banner_marker=true`
3. POST /v1/admin/cms/preview-token/<token_hash>/revoke
   → 200 with revoked_at_utc stamped
   → audit row `cms.preview_token.revoked`
4. Hit the preview URL again:
   → 403 cms.preview.token_expired_or_revoked
5. After expires_at_utc + 30 days, the daily CmsPreviewTokenCleanupWorker hard-deletes the row
   (the only hard-delete path for preview tokens; FR-016)
```

---

## §4. Tests checklist

Each user story in spec.md has at least one Acceptance Scenario; the test suite mirrors them 1:1.

### Unit tests (`tests/Cms.Tests/Unit/`)

- `CmsContentLifecycleTests` — every allowed transition + every forbidden transition with the correct reason code.
- `LocaleCompletenessGateTests` — per-kind happy + missing-locale + missing-asset + missing-effective-at.
- `BannerCapacityCalculatorTests` — at-cap, under-cap, with `*` cross-market accounting in both directions.
- `PreviewTokenSignerTests` — sign + verify round-trip; tampered token rejected; clock-skew tolerance; constant-time compare.
- `CmsMarketPolicyTests` — fallback to `*` when per-market row missing (should never happen post-seed but the resolver is defensive).
- `BannerCtaValidatorTests` — every `cta_kind` × every catalog availability state including `transient_unverified` fail-open.
- `TwoTierStorefrontSortTests` — specific market first, then `*`, with stable secondary sort.

### Integration tests (`tests/Cms.Tests/Integration/`)

- `EditorBannerSlice` — User Story 1 happy path + locale-completeness rejection.
- `EditorFaqReorderRaceTest` — concurrent reorder; one wins, one sees `409 cms.faq.reorder_conflict`.
- `PublisherCapacityRaceTest` — concurrent publishes hitting the cap; exactly one wins.
- `LegalPageSupersessionInSingleTxn` — User Story 3.
- `PreviewTokenLifecycleTest` — User Story 4 mint → read → revoke → 403.
- `StorefrontLeakDetectionTest` — seed every non-`live` state across all 5 kinds; assert zero leakage on every storefront endpoint.
- `WorkerIdempotencyStressTest` — 100-iteration repeat-worker against a backdated row; exactly 1 transition + 1 audit + 1 event.
- `BannerCampaignBindingAutoReleaseTest` — emit `pricing.campaign.deactivated`; assert the banner-campaign binding row is stamped released.
- `EditorRoleRevokedOrphanFlagTest` — emit `customer.role_revoked`; assert affected drafts flagged `ownership_orphaned=true`.
- `AssetGarbageCollectorTest` — dereference an asset; advance time past grace; verify storage object swept and metadata row preserved.

### Contract tests (`tests/Cms.Tests/Contract/`)

- 1:1 with every Acceptance Scenario from spec.md (User Stories 1–7 + Edge Cases).

### Performance tests (`tests/Cms.Tests/Performance/`)

- `BannerListPerfTest` — 1 000 live banners; 50-row page in p95 ≤ 200 ms (SC-006).
- `FeaturedSectionResolutionPerfTest` — 24-ref section, 10 000 catalog products; resolution p95 ≤ 300 ms (SC-007).

---

## §5. Definition of Done (DoD)

The PR cannot merge until every item below is true.

- [ ] Migration applies cleanly on a fresh DB and is idempotent (re-running yields zero changes).
- [ ] All 9 tables exist in the `cms` schema with the documented constraints + indexes + triggers.
- [ ] `CmsReferenceDataSeeder` runs in Program.cs and creates `cms.market_schemas` rows for `EG`, `KSA`, `*` with V1 defaults.
- [ ] Every Acceptance Scenario from spec.md passes as a contract test.
- [ ] State-machine forbidden transitions are compile-time guarded via the `TransitionTo` API; runtime attempts return `400 cms.{kind}.illegal_transition`.
- [ ] Idempotency-Key envelope enforced on every state-transitioning POST; replay window 24 h.
- [ ] xmin guards on every PATCH and on the worker's `→ live` / `→ archived` / `→ superseded` transitions.
- [ ] Storefront leak-detection test passes with 0 leakage.
- [ ] Worker idempotency stress test passes (100 iterations).
- [ ] Banner capacity stress test passes (100 concurrent publishes; exactly 1 winner per slot).
- [ ] Banner CTA validation invokes catalog read contracts at publish-time AND read-time.
- [ ] Featured-section reference resolution invokes catalog read contracts in parallel; broken refs filtered; broken-section events emitted with rate-limit.
- [ ] Audit-coverage script reports 100 % audit-row presence on every state transition + every authoring action + every preview-token mint/revoke + every reorder + every banner-campaign binding/unbinding + every asset sweep (SC-002).
- [ ] AR/EN ICU keys present for every system-generated string; `AR_EDITORIAL_REVIEW.md` reviewed; SC-008 30-screen checklist 100 %.
- [ ] OpenAPI document `services/backend_api/openapi.cms.json` regenerated and committed.
- [ ] No EF `ManyServiceProvidersCreatedWarning` in test logs (CmsModule suppresses per project-memory rule).
- [ ] No customer PII (real phones / emails / national IDs) in any seeded row (`seed-pii-guard` CI green).
- [ ] No secrets in `appsettings.json`; preview-token signing key sourced from layered config + env override.
- [ ] Constitution + ADR fingerprint present + verified on the PR (Guardrail #3).
- [ ] Lint + format + contract diff CI checks pass (Guardrails #1 + #2).
- [ ] CODEOWNERS approval received from human reviewer (Guardrail #4 covers the constitution / ADR paths; this is a content PR, but CODEOWNERS is required for spec changes if any).
- [ ] No existing test from specs 020 / 021 / 022 / 023 / 007-b regresses.
