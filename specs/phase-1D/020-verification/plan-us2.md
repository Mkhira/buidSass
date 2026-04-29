# Implementation Sub-Plan: US2 — Admin reviewer queue and decisioning

**Spec**: [spec.md](./spec.md) · **Phase**: 4 (User Story 2; P1 / MVP)
**Branch**: `feat/020-verification` · **Date**: 2026-04-29
**Companion to**: [plan.md](./plan.md) (module-level), [tasks.md](./tasks.md) §Phase 4
**Status**: Ready to implement (Phase 3 batch 1 + Phase 2 complete on `feat/020-verification`)

> This document is a **focused implementation sub-plan** for the reviewer surface only. It does NOT replace `plan.md` (module-level architecture, Constitution Check, Project Structure — all already authored and merged via PR #43). It exists to break the 18 US2 tasks into discrete, reviewable batches before any reviewer-side code is written.

---

## §1. Why this phase next, and what it unlocks

### 1.1 Goal

Land the **`verification.review`-permission reviewer surface** so a customer's submission can be approved / rejected / request-info'd / revoked. This completes the customer↔reviewer round trip and unblocks two previously deferred items:

| Deferred item | Why it was deferred | What US2 unlocks |
|---|---|---|
| T058 ResubmitWithInfo | Needs `info_requested` state to be reachable | T074 RequestInfo creates that state |
| T059 RequestRenewal | Needs `approved` state to be reachable | T072 Approve creates that state |
| Eligibility-cache writes | Currently a Phase 2 stub (`EligibilityCacheInvalidator.RebuildAsync` no-op) | The cache becomes load-bearing once approval / revoke fire |

### 1.2 spec.md US1 Independent Test becomes runnable

Per `tasks.md:144` checkpoint: *"a customer can submit ... Independent test (per spec.md US1) becomes possible once US2 lands the admin approval."*

After US2 ships:
1. Customer POSTs `/api/customer/verifications` → 201 `{state: submitted}` (already works ✅)
2. Reviewer GETs `/api/admin/verifications` → sees the row ⏳
3. Reviewer POSTs `/api/admin/verifications/{id}/approve` → row state flips to `approved` ⏳
4. Customer GETs `/api/customer/verifications/active` → sees the new approval (B-deferred slice covers this; happens AFTER US2) ⏳

Steps 2–3 are this phase. Step 4 is Phase 3 batch 2 (cheap follow-up).

### 1.3 What stays deferred after US2 ships

- **T054 AttachDocument** — IStorageService + IVirusScanService DI plumbing; orthogonal to state-machine work
- **T055–T057** customer read endpoints — same Submit pattern, copy-paste ready after US2 lands
- **T058 ResubmitWithInfo / T059 RequestRenewal** — final pass after US2 (now testable)
- **Phase 5 (US3) eligibility query** — Catalog/Cart/Checkout consumer; needs spec 005 read contract

---

## §2. Task scope (T063–T078, plus T069a / T069b)

18 tasks across 7 reviewer slices + RBAC + ICU + OpenAPI regen.

### 2.1 Tests (8)

| Task | File | Coverage |
|---|---|---|
| T063 [P] | `Tests/Verification.Tests/Contract/AdminQueueContractTests.cs` | RBAC 403; market scope; oldest-first sort; SLA signal `ok\|warning\|breach` |
| T064 [P] | `Tests/Verification.Tests/Contract/AdminDetailContractTests.cs` | Schema-as-submitted (FR-026); transition history; document metadata only |
| T065 [P] | `Tests/Verification.Tests/Contract/AdminApproveContractTests.cs` | Reason required; `expires_at` set; audit event; `VerificationApproved` event |
| T066 [P] | `Tests/Verification.Tests/Contract/AdminRejectContractTests.cs` | Cool-down written; rejected event |
| T067 [P] | `Tests/Verification.Tests/Contract/AdminRequestInfoContractTests.cs` | SLA-timer-pause via `paused_at` metadata |
| T068 [P] | `Tests/Verification.Tests/Contract/OpenHistoricalDocumentContractTests.cs` | Signed URL + dual PII audit on terminal-state read; 410 on purged |
| T069 | `Tests/Verification.Tests/Integration/AdminDecisionConcurrencyTests.cs` | 100-parallel approve/reject; xmin guard; one winner; loser sees `already_decided` (SC-007) |
| T069a [P] | `Tests/Verification.Tests/Integration/ReviewerReasonLocaleTests.cs` | FR-033 — empty `{}` → 400; both locales preserved in audit; rendering preference |
| T069b [P] | `Tests/Verification.Tests/Integration/AdminQueueSlaBreachTests.cs` | FR-039 — 3-business-day breach; 1.5-day warning; info-requested pause |

### 2.2 Implementation slices (7)

| Task | Path | Notes |
|---|---|---|
| T070 | `Modules/Verification/Admin/ListVerificationQueue/{Request,Handler,Endpoint}.cs` | Computes SLA signal per row via `BusinessDayCalculator` against snapshotted schema; market scope from reviewer claims |
| T071 | `Modules/Verification/Admin/GetVerificationDetail/{Request,Handler,Endpoint}.cs` | Resolves schema by `schema_version` for FR-026; includes `customer_locale`; calls `IRegulatorAssistLookup` (V1 returns null → field absent) |
| T072 | `Modules/Verification/Admin/DecideApprove/{Request,Validator,Handler,Endpoint}.cs` | xmin-guarded; `expires_at = now + market.expiry_days`; supersession in same Tx if `supersedes_id != null`; eligibility-cache rebuild; `VerificationApproved` published |
| T073 [P] | `Modules/Verification/Admin/DecideReject/{Request,Validator,Handler,Endpoint}.cs` | Same body shape; writes `cooldown_until` to response |
| T074 [P] | `Modules/Verification/Admin/DecideRequestInfo/{Request,Validator,Handler,Endpoint}.cs` | SLA-timer-pause via `paused_at` in transition metadata |
| T075 | `Modules/Verification/Admin/OpenHistoricalDocument/{Request,Handler,Endpoint}.cs` | `IPiiAccessRecorder` on every read; 410 when `purged_at IS NOT NULL` |
| T076 | `[RequirePermission("verification.review")]` / `[RequirePermission("verification.revoke")]` on every admin endpoint |

### 2.3 Closing tasks (2)

- **T077** — ICU keys + AR translations for every reviewer-facing string + every customer-visible decision reason summary; append AR keys to `AR_EDITORIAL_REVIEW.md`.
- **T078** — Re-emit `openapi.verification.json` to include admin endpoints; verify Guardrail #2 contract diff.

---

## §3. Implementation batches

Three batches. Each batch is a single coherent commit + green test gate.

### 3.1 Batch 1 — Read surface + concurrency-safe approve (the critical path)

**Why first**: this is the smallest end-to-end demonstrable slice. Reviewer can list the queue, open a detail, and approve a submission. The hard part — xmin concurrency guard, eligibility-cache rebuild on approval, supersession in same Tx — lands here.

**Scope (8 tasks)**:
- T070 ListVerificationQueue (slice)
- T071 GetVerificationDetail (slice)
- T072 DecideApprove (slice + xmin + eligibility-cache + supersession)
- T076 RBAC `[RequirePermission]` attributes on the three endpoints above
- T063 AdminQueueContractTests
- T064 AdminDetailContractTests
- T065 AdminApproveContractTests
- T069 AdminDecisionConcurrencyTests (asserts SC-007 — exactly one winner under 100-parallel approves)

**Files (~12 new)**:
```
Modules/Verification/Admin/
├── AdminAuthorizationDefaults.cs                          # NEW — auth scheme constant
├── AdminVerificationResponseFactory.cs                    # NEW — Problem Details for admin surface
├── ListVerificationQueue/{Request,Handler,Endpoint}.cs    # NEW
├── GetVerificationDetail/{Request,Handler,Endpoint}.cs    # NEW
└── DecideApprove/{Request,Validator,Handler,Endpoint}.cs  # NEW
Tests/Verification.Tests/Contract/
├── AdminQueueContractTests.cs                             # NEW
├── AdminDetailContractTests.cs                            # NEW
└── AdminApproveContractTests.cs                           # NEW
Tests/Verification.Tests/Integration/
└── AdminDecisionConcurrencyTests.cs                       # NEW
```

**VerificationModule changes**:
```csharp
// New scoped registrations:
services.AddScoped<ListVerificationQueueHandler>();
services.AddScoped<GetVerificationDetailHandler>();
services.AddScoped<DecideApproveHandler>();

// MapVerificationEndpoints additions:
var admin = endpoints.MapGroup("/api/admin/verifications");
admin.MapListVerificationQueueEndpoint();
admin.MapGetVerificationDetailEndpoint();
admin.MapDecideApproveEndpoint();
```

**Build + test gate**: build green; new contract + integration tests pass; total green test count rises from 54 → ~65.

### 3.2 Batch 2 — Reject + RequestInfo + locale-aware reason

**Why second**: reuses the DecideApprove pattern; most of the test+slice cost is already paid. Adds the two non-approval decisions and the FR-033 locale reason validator.

**Scope (6 tasks)**:
- T073 DecideReject (slice)
- T074 DecideRequestInfo (slice with SLA-timer-pause metadata)
- T066 AdminRejectContractTests
- T067 AdminRequestInfoContractTests
- T069a ReviewerReasonLocaleTests (FR-033 — both locales preserved in audit)
- T069b AdminQueueSlaBreachTests (FR-039 — depends on the queue from batch 1)

**New files**: ~6 (4 slice files for the two decisions + 2 test files; reason-locale validator extracted as a shared helper).

**Build + test gate**: total green test count rises ~65 → ~74.

### 3.3 Batch 3 — Revoke, OpenHistoricalDocument, RBAC alpha, ICU/OpenAPI close-out

**Why last**: revoke uses a distinct permission (`verification.revoke`) and modifies an `approved` row, so it needs an approved-state row to test against (which only exists once batch 1 ships). OpenHistoricalDocument depends on the PII recorder and storage-abstraction signed-URL helper. ICU + OpenAPI regen are the natural close-out.

**Scope (4 tasks)**:
- T075 OpenHistoricalDocument (slice)
- T068 OpenHistoricalDocumentContractTests
- T077 ICU keys + AR translations + AR_EDITORIAL_REVIEW.md tracker entries
- T078 OpenAPI regen + Guardrail #2 contract diff

**Plus** Admin/DecideRevoke slice (separate `verification.revoke` permission per contracts §3.6) — was implicit but not listed in the original task numbering; needed for completeness of the reviewer surface and unblocks customer-side `RequirePermission(verification.revoke)` tests.

**Build + test gate**: total green test count rises ~74 → ~80; OpenAPI artifact diff committed.

---

## §4. Risks and dependencies

### 4.1 Authentication scheme

Customer slice in Phase 3 used `"CustomerJwt"`. Admin slice needs the **admin** scheme (likely `"AdminJwt"` or similar). Need to confirm by inspecting `Modules/Identity/Authorization/` — if the scheme name differs from the assumption, `AdminAuthorizationDefaults.cs` (new) wraps the constant so all admin slices reference one place.

**Mitigation**: First file in batch 1 is `AdminAuthorizationDefaults.cs`. Single point of change if the actual scheme name surprises us.

### 4.2 RBAC permission binding

`verification.review` and `verification.revoke` are NEW permissions declared in this spec (`Modules/Verification/Authorization/VerificationPermissions.cs` — already shipped in Phase 2 batch 1). However, they are NOT yet **bound to roles** — that binding is owned by spec 015 (admin-foundation) which is on `main` but doesn't know about Verification's permissions yet.

**Mitigation**:
- Tests that need a real reviewer JWT mint a token with the `verification.review` claim directly (existing pattern from Returns admin tests via `ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[] { "returns.read", ... })`).
- Spec 015's role-binding update is a follow-up PR after 020 ships — it doesn't block this work.
- Production deploy gates on spec 015 binding; a follow-up `/schedule` agent verifies ~1 week post-merge.

### 4.3 SLA timer + business-day math

`BusinessDayCalculator` (Phase 2 batch 1, unit-tested) provides the arithmetic. Queue handler needs to:
1. Compute `now - submitted_at` in business days using the schema's `holidays_list`.
2. Map to `sla_signal` based on `sla_warning_business_days` and `sla_decision_business_days`.
3. Pause the timer when state is `info_requested` (use `paused_at` metadata or `decided_at` as the cap).

**Risk**: timer-pause logic is fiddly. T067 + T069b are designed to catch off-by-ones; budget extra test iterations.

### 4.4 Eligibility cache rebuild on approval

`EligibilityCacheInvalidator.RebuildAsync` is a Phase 2 stub. **It MUST become non-stub by end of US2** because approval is the first transition that produces a meaningful eligibility-class change.

**Mitigation**: T072 (DecideApprove) flesh-out is the natural place to write the real cache logic. The implementation is ~30 lines:
1. Look up customer's most-recent approved verification (the just-approved one).
2. Read `IProductRestrictionPolicy` (Phase 2 declared the contract — but spec 005 hasn't shipped yet, so use a stub `IProductRestrictionPolicy` test double).
3. UPSERT the `verification_eligibility_cache` row with `EligibilityClass=Eligible`, `ExpiresAt=approval.expires_at`, `Professions=[approval.profession]`.

Note: Phase 5 (US3) re-tests the cache invalidator across the full state matrix — batch 1 only needs the cache write to work for the `approved` case.

### 4.5 Domain event publish — `VerificationApproved`

The event record is shipped (Phase 2 batch 1, `Modules/Shared/VerificationDomainEvents.cs`). What's missing is the **publisher** — there's no `IDomainEventPublisher` injected into the handler.

**Mitigation**: existing modules use `IPublisher` from MediatR (Returns publishes via `_mediator.Publish(...)`). Use the same pattern. Add `IPublisher` to handler constructors. Spec 025 (notifications) eventually subscribes; until then the event is fire-and-forget and the in-process bus has no subscribers (which is fine — the event is still dispatched, just no-op consumed).

### 4.6 Audit event payload shape

The `verification.state_changed` event already has a settled shape from Phase 3 batch 1. Approve / reject / request-info / revoke all use the same shape with different `prior_state` / `new_state` / `reason` values. Reason is now bilingual (FR-033) — audit `event_data.reason_en` and `event_data.reason_ar` rather than the single `reason` field used in the original Phase 3 audit event.

**Mitigation**: extract a small `VerificationAuditEvents` helper in batch 1 (one method per transition kind) so the shape is centralized and the four reviewer-side handlers reference one helper.

### 4.7 Test fixture reuse

The `SubmitVerificationHappyPathTests` fixture (Phase 3 batch 1) spins up its own Postgres container per test class. The 4 new admin test classes will each spin up their own → 4× Docker overhead.

**Mitigation**: extract a shared `VerificationTestcontainerFixture` under `Tests/Verification.Tests/Infrastructure/` that batches admin test classes via `IClassFixture<>` collection. Estimated savings: ~30 s per CI run. NOT critical for correctness; defer if it grows the batch over budget.

---

## §5. Acceptance criteria

US2 is "done" when ALL of these hold:

1. **All 18 tasks marked complete** in `tasks.md` (T063–T078 plus T069a / T069b).
2. **`dotnet build services/backend_api/backend_api.sln`** → 0 errors.
3. **`dotnet test Tests/Verification.Tests`** → green; total ≈ 80 tests including all batch 1 + 2 + 3 additions.
4. **Independent test from spec.md US1** runnable end-to-end: customer submit → reviewer approve → audit log shows both events → eligibility cache row says `Eligible`.
5. **`openapi.verification.json`** regenerated and committed; CI Guardrail #2 contract-diff check passes.
6. **`AR_EDITORIAL_REVIEW.md`** updated with the new AR keys staged for editorial sign-off.
7. **No regressions** in pre-US2 tests (Phase 1 + Phase 2 + Phase 3 batch 1 still green).
8. **Concurrency stress passes**: T069 — 100 parallel approve/reject calls produce exactly one decision; SC-007 verified.

---

## §6. Estimated batch-by-batch deliverable

| Batch | Tasks | New files | LOC est. | Test count delta |
|---|---|---|---|---|
| 1 | T070, T071, T072, T076 (partial), T063, T064, T065, T069 | ~12 | +800 | 54 → 65 |
| 2 | T073, T074, T076 (rest), T066, T067, T069a, T069b | ~6 | +500 | 65 → 74 |
| 3 | T075, DecideRevoke, T068, T077, T078 | ~6 | +400 | 74 → 80 |
| **Total** | **18 tasks** | **~24 files** | **+1,700 LOC** | **+26 tests** |

Per batch ≈ one /speckit-implement turn. Three turns to complete US2.

---

## §7. After US2 — what's next

In dependency-justified order:

1. **Phase 3 batch 2** (T054 AttachDocument + T055–T057 customer reads) — cheap polish; 4 tasks, ~400 LOC, 1 turn.
2. **Phase 3 batch 3** (T058 ResubmitWithInfo + T059 RequestRenewal) — now testable since US2 created the prerequisite states; 2 tasks + tests, ~300 LOC, 1 turn.
3. **Phase 5 (US3)** — eligibility query consumer surface for catalog/cart/checkout. ~8 tasks. Unlocks SC-004 latency benchmark.
4. **Phase 6 (US4)** — expiry + reminder + document-purge background workers. ~14 tasks.
5. **Phase 7 (US5)** — market-aware schema versioning. ~6 tasks.
6. **Phase 8 (US6)** — reviewer revoke (already partially landed in batch 3). ~5 tasks.
7. **Phase 9 — Polish** — final pass on AR translations, OpenAPI consolidation, documentation.

After all of the above, spec 020 hits DoD and unblocks spec 022 / 023 / 024 / 005 / 009 / 010 / 019 consumers of `ICustomerVerificationEligibilityQuery`.
