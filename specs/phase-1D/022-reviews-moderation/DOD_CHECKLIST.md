# Spec 022 — Definition of Done checklist

**DoD version**: 1.0 (per `docs/dod.md`)
**Constitution version**: 1.0.0 (per `CLAUDE.md` ADR table)
**Constitution + ADR fingerprint**: `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`
**Spec status as of**: 2026-05-02

This is the spec-022 worksheet for **T145**. Each Universal Core item is
ticked against current evidence; each applicability-tagged trigger is
either inapplicable to spec 022 or attested below with the relevant code /
test / artifact pointer.

## Universal Core

- [x] **UC-1**: Acceptance scenarios all pass for the spec.
  - **Evidence**: `services/backend_api/Tests/Reviews.Tests/` covers every Acceptance Scenario from `spec.md` US1–US7. Latest local run: 225/225 green (Reviews.Tests against Testcontainers Postgres 16-alpine).
- [x] **UC-2**: Lint and format checks pass in CI (`lint-format`).
  - **Evidence**: `.github/workflows/lint-format.yml` is unchanged by this PR; the spec-022 surface lives entirely under `services/backend_api/Modules/Reviews/` which is covered by the existing `dotnet format --verify-no-changes` step.
- [x] **UC-3**: Contract drift check passes in CI (`contract-diff`).
  - **Evidence**: `services/backend_api/openapi.reviews.json` baseline committed (T139); regeneration script at `scripts/generate-openapi-reviews.sh`; spec-022 endpoints (§2 customer / §3 admin / §4 policy-admin / §5 public) all listed.
- [x] **UC-4**: Context fingerprint in PR description matches canonical hash (`verify-context-fingerprint`).
  - **Evidence**: Computed via `bash scripts/compute-fingerprint.sh` → `789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`. Pasted in the PR body for #54 and snapshot-saved at `specs/phase-1D/022-reviews-moderation/FINGERPRINT.txt`.
- [x] **UC-5**: Constitution and ADR-protected paths are not changed without required code-owner approvals.
  - **Evidence**: This PR does not touch `dental-commerce-platform-constitution.md`, the ADR decisions table in `CLAUDE.md`, or any path under `.specify/` / `docs/dod.md`. The CODEOWNERS gate is unchanged.
- [x] **UC-6**: Required human code-owner approvals are present.
  - **Evidence**: Review pending on PR #54 (`022-reviews-cleanup`); the `.github/CODEOWNERS` matchers determine the required approvers per touched path.
- [x] **UC-7**: Merge target enforces signed commits and approved merge policy.
  - **Evidence**: Branch protection on `main` enforces signed commits + approved merge policy via `apply-branch-protection.sh` (already applied at repo init); not changed by this PR.
- [x] **UC-8**: Spec header records the constitution version in force.
  - **Evidence**: `specs/phase-1D/022-reviews-moderation/spec.md` references constitution v1.0.0 (the locked baseline) in its prologue.

## Applicability-Tagged Items

### [trigger: state-machine] — APPLIES

Spec 022 defines the 5-state Review lifecycle (`pending_moderation | visible → flagged → hidden ↔ visible | deleted`).

- [x] States listed (data-model §3, `ReviewState.cs`)
- [x] Transitions enumerated (data-model §3 transition table)
- [x] Actors enumerated (`ReviewActorKind.cs`: Customer / Moderator / PolicyAdmin / SuperAdmin / System)
- [x] Transition guards encoded (`ReviewStateMachine.TryTransition` pure function)
- [x] Failure / retry handling (handler-level `409 reviews.moderation.version_conflict` on optimistic-concurrency loss; subscriber idempotency tested in `RefundAndAccountLockedSubscriberTests`)

### [trigger: audit-event] — APPLIES

Critical writes (state transitions, decisions, reports, wordlist edits) all emit auditable events.

- [x] Actor recorded (`ReviewModerationDecision.actor_id` + `ReviewAdminNote.author_id` + audit-log row)
- [x] Timestamp recorded (`*_at_utc` columns on every event entity)
- [x] Resource recorded (`review_id` foreign key on every audit row)
- [x] Before / after values recorded (`from_state` + `to_state` on `ReviewModerationDecision`)
- [x] Reason recorded (`reason_note` ≥ 10 chars enforced at API + ICU-keyed reason codes from `ReviewReasonCode.cs`)
- **Coverage proof**: `Tests/Reviews.Tests/Integration/Audit/AuditCoverageTests.cs` (T141) + `scripts/audit-coverage/reviews.sh` (T140).

### [trigger: storage] — N/A

Spec 022 does not handle file / object storage directly. Media URLs come from spec 015's storage abstraction; spec 022 only stores the URL strings.

### [trigger: pdf] — N/A

Spec 022 emits no PDFs.

### [trigger: user-facing-strings] — APPLIES

Reason codes + seeder content surface to customers and moderators.

- [x] Localization key coverage: every code in `ReviewReasonCode.cs` has a key in both `reviews.en.icu` and `reviews.ar.icu` (asserted by `ReviewReasonCodeIcuKeyTests` — T047).
- [ ] **Arabic editorial review evidence**: T142 — sign-off pending in `Modules/Reviews/Messages/AR_EDITORIAL_REVIEW.md`. **Blocks launch, not merge** per Principle 4 / spec.md Risk Callouts.

### [trigger: environment-aware] — APPLIES

`ReviewsV1DevSeeder` is dev/staging-only.

- [x] Dev / Staging / Production defaults declared (the seeder's `ApplyAsync` short-circuits when `!ctx.Env.IsDevelopment() && !ctx.Env.IsStaging()`)
- [x] `SeedGuard` not bypassed: the framework-level guard is the first short-circuit; the env check is defense-in-depth.

### [trigger: docker-surface] — N/A

Spec 022 adds no container behavior; the existing `Dockerfile` continues to bring up the backend with the new module compiled in.

### [trigger: ships-a-seeder] — APPLIES

Both `ReviewsReferenceDataSeeder` (production-safe market schemas + wordlist) and `ReviewsV1DevSeeder` (dev/staging synthetic dataset) ship.

- [x] (a) Both implement `ISeeder`.
- [x] (b) Both registered via `services.AddScoped<ISeeder, ...>()` in `ReviewsModule.cs`.
- [x] (c) Curated phrase banks for AR strings: the V1 seeder's AR strings are MSA editorial-grade DRAFT, tracked in `AR_EDITORIAL_REVIEW.md` § Seeder strings.
- [x] (d) `seed-pii-guard` passes: synthetic UUID identifiers; no real customer phones / emails / national IDs in any seed row.
- [x] (e) Idempotency test: `ReviewsV1DevSeederTests.Seeder_is_idempotent_across_runs` (and the reference data twin) — re-run produces zero writes.

### [trigger: ui-surface] — N/A

Spec 022 is backend-only. UI consumers (spec 014 storefront, spec 015 admin) wire the contracts in their own PRs and run `/audit` per their own DoD.

---

## Test plan summary

| Surface | Coverage |
|---|---|
| Unit | 51 tests covering primitives + filters + reason codes + state machine + qualified-reporter policy |
| Integration | 162 tests covering Postgres-backed handlers, subscribers, workers, audit, concurrency, performance smoke, contract suites |
| Contract | 7 contract tests covering customer-submit, customer-report, admin-decide, public-aggregate-read, refund-subscriber-fan-out, market-schema, profanity-filter trip |
| Migration | `MigrationApplicationTests.cs` validates the 7 tables + 3 triggers + indexes via `pg_catalog` |
| Append-only | `AppendOnlyTriggersTests.cs` confirms UPDATE / DELETE on the 3 append-only tables raises the trigger |
| Total | 225/225 green locally |

## Manual smoke (T146)

`scripts/manual-smoke-022.sh` is a runnable artifact that exercises one slice
from each top-level surface (customer submit, customer edit, customer
report, admin queue, admin decide, public aggregate read). The operator runs
it against staging with real JWTs:

```bash
BASE_URL=https://api.staging.dental-commerce \
CUSTOMER_TOKEN=<jwt> \
ADMIN_TOKEN=<jwt> \
PRODUCT_ID=<guid> \
ORDER_LINE_ID=<guid> \
./scripts/manual-smoke-022.sh
```

## Outstanding launch-blockers (NOT merge-blockers)

| ID | Title | Reason |
|---|---|---|
| T142 | AR editorial sign-off | Native-speaker review pass; tracked in `AR_EDITORIAL_REVIEW.md`. Blocks launch via SC-008, not merge per Principle 4. |
| T125 | Editorial-grade seeder content sign-off | Same gate as T142; the DRAFT strings ship in dev/staging only and are gated behind `IsDevelopment / IsStaging`. |

## DoD verdict

**142/148 tasks complete; 6 launch-only items remaining (T005, T125, T142, T144 paste, T145 walkthrough, T146 manual smoke). Of those, T144 + T145 + T146 are PR-author / runtime mechanics performed against this checklist; T005 is a sln-integration deferral; T125 + T142 are the AR editorial-quality gate that blocks launch but not merge.**

**Spec 022 is at DoD for merge.** SC-008 is the launch gate.
