# Phase 1A Audit — Specs 001, 002, 003

**Branch**: `review/phase-1a-audit`
**Date**: 2026-04-19
**Scope**: everything on `main` after PRs #1–#10 merged
**Purpose**: surface residual issues before Phase 1B (`004-identity-and-access`) opens.

Task completion: 001 = 29/29, 002 = 64/64, 003 = 81/82 (T077 verified in CI, pending tick).

Legend: 🔴 blocker · 🟠 high · 🟡 medium · 🟢 low · ℹ️ info

---

## Closeout Status (2026-04-20)

Individual fix PRs landed on `main`:

| PR | Scope | Findings addressed |
|---|---|---|
| #11 | Spec 003 closeout + audit doc | E1, E2 |
| #12 | EF migration `RevokeAuditWriteGrants` + grant-enforcement integration tests | **C1** (blocker) |
| #13 | CI hardening: `workflow_dispatch`, empty-OpenAPI guard, PR-gate dotnet pack + npm/dart `--dry-run`, monotonic version formula, drop `preview-deploy` from required checks | A1, A4, B2, B3 |
| #14 | Polish: portable `gen-contracts.sh` (python3), migration-snapshot drift test, `infra/README.md`, `packages/shared_contracts/CHANGELOG.md` | B4, C4, D2, D3 |
| #15 | Flatten `services/backend_api/services/` up one level; exclude `Tests\**` from main csproj | **C2** |

Investigated and closed as false-positive: C3 (`AuditLogReadPolicy` is live-referenced), C5 (`Microsoft.EntityFrameworkCore.InMemory` used by tests), C6 (QuestPDF license set once), C7 (`MediatR` used in test scaffolding).

Accepted as bootstrap posture: A2, A3, A5, A6 (revisit when a second maintainer is onboarded). D1 tracked for Phase 1B/1C kickoff. B1 tracked for spec 004 (first backend endpoints generate real OpenAPI).

---

## A. Governance & CI

| # | Sev | Finding | Evidence | Recommended action |
|---|---|---|---|---|
| A1 | 🟠 | `AZURE_STATIC_WEB_APPS_API_TOKEN` secret is **not set**; `preview-deploy` is a required status check. Any PR that touches `apps/admin_web/**` will fail the required check once admin_web has content. | `gh secret list` → empty | Create the secret in Phase 1B before the first `admin_web` PR, OR relax the required-checks list to make `preview-deploy` optional until admin_web is scaffolded. |
| A2 | 🟠 | Solo-maintainer bootstrap: `CODEOWNERS` has only `@Mkhira`; required reviews = 1 with `require_code_owner_reviews: true`. Every PR requires `--admin` override. | CODEOWNERS, `gh api …/protection` | Accept as bootstrap; document in `CONTRIBUTING.md` that admin-merge is the expected path until a second maintainer is onboarded. Log each admin-merge in the PR description. |
| A3 | 🟡 | `enforce_admins: false` — admin can push/merge around protection. | protection JSON | Acceptable now; flip to `true` when co-maintainer exists. |
| A4 | 🟡 | Recent Phase-1A close-out used **4 CI hotfix PRs** (#7 → #10). Pattern suggests CI wasn't exercised pre-merge. | git log | Add a `workflow_dispatch` trigger + a `ci/dryrun-contracts-publish.yml` that runs dotnet pack + npm publish `--dry-run` on every PR so publish-only regressions surface before main. |
| A5 | 🟢 | `validate-diagrams` job is defined but not in the required-checks list. | `gh api …/protection` | Add it once Mermaid coverage matters; right now it's a nice-to-have. |
| A6 | ℹ️ | Branch protection = `required_linear_history: true`, `signatures: true`, `conversation: true`, force-push/delete off. All good. | protection JSON | — |

---

## B. Shared Contracts Pipeline

| # | Sev | Finding | Recommended action |
|---|---|---|---|
| B1 | 🟠 | `packages/shared_contracts/openapi.json` has **zero paths and zero schemas** — contracts packages currently publish empty type surfaces (`paths = Record<string, never>`, dotnet `DentalCommerceApiClient.cs` is 6 lines, dart lib is 3 lines). T077 verified the *pipeline* works, but the contracts are functionally empty. | Phase 1B spec 004 must emit a real OpenAPI from the backend (via Microsoft.AspNetCore.OpenApi + `dotnet run --no-launch-profile -- --openapi-export`) and overwrite `services/backend_api/openapi.json` so subsequent publishes carry schemas. Add a CI gate: fail if `paths` is empty after the build step. |
| B2 | 🟠 | Version bump formula `0.$(date +%y%m).${GITHUB_RUN_NUMBER}` produces **non-monotonic** sequences across month boundaries (e.g., `0.2604.999` then `0.2605.1`). NuGet is OK; npm semver is lenient but consumers using `^0.2604.0` will not receive `0.2605.x`. | Switch to strictly increasing: `0.$(date +%s)` or `0.0.${GITHUB_RUN_NUMBER}`. |
| B3 | 🟡 | Dart publish is stubbed (`echo`); there's no sanity check that the Dart package even `flutter pub get`-parses. | Add a CI step `flutter pub publish --dry-run` on every PR that touches `packages/shared_contracts/dart/**`. |
| B4 | 🟡 | `gen-contracts.sh` uses **BSD-incompatible** `sed -i.bak -E` on the runner (GNU sed) — works in CI but will misbehave on macOS dev laptops (same trap that bit `apply-branch-protection.sh`). | Either commit to "CI-only" with a fail-fast guard, or add a `_sed_inplace` helper that branches on `uname`. |

---

## C. Backend Modules (Spec 003)

| # | Sev | Finding | Recommended action |
|---|---|---|---|
| C1 | 🔴 | `RevokeAuditWriteGrants.sql` lives under `Modules/AuditLog/Migrations/` but is **not wired into EF's migration pipeline**. `dotnet ef database update` will NOT apply it — the INSERT-only guarantee that the audit-log architecture depends on is currently enforced only by developer discipline. | Convert to an idempotent EF migration: `migrationBuilder.Sql(File.ReadAllText("RevokeAuditWriteGrants.sql"))` wrapped in `IF EXISTS` checks, OR add a bootstrap role-setup SQL under `infra/postgres/` + document a mandatory step in `services/backend_api/README.md` AND gate a CI job that asserts the grants on the Testcontainers Postgres after migration. |
| C2 | 🟠 | Nested layout `services/backend_api/services/Program.cs` + sln referencing `services\backend_api.csproj`. Works but every `cd services/backend_api` commit lands in the wrong depth; future ADR-003 vertical slices under `Features/` will collide. | Flatten in Phase 1B, before feature slices land: move `services/backend_api/services/*` up one level, update sln + tests csproj ProjectReference. One focused PR, ~20 file renames. |
| C3 | 🟠 | `AuditLogReadPolicy.cs` still exists but `/audit-log` endpoint was removed. Policy is orphaned; first real auth PR will inherit a dead-code symbol with a confusing name. | Keep the class with a clear `// TODO: spec 005` comment (already done?) — verify the comment exists; if not, add it. |
| C4 | 🟡 | `AppDbContextModelSnapshot.cs` was generated against InMemory or the dev connection — verify the snapshot matches `20260419_01/02` migrations by running `dotnet ef migrations script` against main and diffing against the Testcontainers applied schema. | Add an integration test that runs `dbContext.Database.MigrateAsync()` then `dbContext.Database.GetPendingMigrationsAsync()` and asserts the result is empty. |
| C5 | 🟡 | `Tests/backend_api.Tests.csproj` still carries **`Microsoft.EntityFrameworkCore.InMemory`** despite Testcontainers being the strategy. InMemory silently ignores constraints and transactions — any test that uses it will hide bugs that Postgres would catch. | Remove the package unless there's a test that genuinely needs it; audit `Tests/**/*.cs` for `UseInMemoryDatabase` and delete. |
| C6 | 🟡 | QuestPDF license is set in `Program.cs` (good) AND previously was duplicated in `StubPdfService.cs` / `QuestPdfService.cs` — confirm the duplicates are removed. | Grep pass: `grep -rn "QuestPDF.Settings.License" services/` should return one hit only. |
| C7 | 🟢 | `MediatR` is in the tests csproj but nowhere in the production project. Dead dependency. | Remove from test csproj; add to production csproj when the first MediatR handler lands in spec 004. |

---

## D. Scaffolding Coverage

| # | Sev | Finding | Recommended action |
|---|---|---|---|
| D1 | 🟠 | `apps/customer_flutter/` and `apps/admin_web/` are **empty directories**. Lint-format workflow and build-and-test workflow both have conditional paths guarded by `if [ -f .../pubspec.yaml ]` and `if [ -f .../package.json ]` so nothing fails, but there is zero coverage of the actual app stacks. | Open a tracked issue: "Spec 1C kickoff prerequisite — scaffold flutter create + next.js create before 1B spec merges". Document in `docs/implementation-plan.md`. |
| D2 | 🟡 | `infra/` directory is empty (Terraform/K8s/Docker were scoped for later but ADR-010 Azure is already locked). | Add a stub `infra/README.md` stating "Azure Saudi Arabia Central IaC lands in Phase 1E (spec 016+)" so the empty dir isn't ambiguous. |
| D3 | 🟢 | No `CHANGELOG.md` or release-notes workflow for shared contracts. First npm consumer will have no version diff signal. | Add `packages/shared_contracts/CHANGELOG.md` + adopt `conventional-changelog` in Phase 1B. |

---

## E. Open Task Closure

| # | Sev | Finding | Recommended action |
|---|---|---|---|
| E1 | 🟡 | `specs/phase-1A/003-shared-foundations/tasks.md` T077 is still `[ ]` but CI run `24636917073` at `2026-04-19T19:13` on main shows `Your package was pushed` (NuGet) + `+ @mkhira/shared-contracts@0.2604.23` (npm). | Tick T077 `[X]` on a Spec 003 closeout PR with a one-line note referencing run id. |
| E2 | 🟡 | Spec 003 DoD UC-1..UC-8 checklist not verified on main yet (T080 was marked complete locally). | In the same closeout PR, confirm `specs/phase-1A/003-shared-foundations/checklists/requirements.md` matches the state on main. |

---

## Priority Recommendations Before Phase 1B Opens

1. **C1 — Wire the audit-log grant revocation into EF migrations.** This is the only item with correctness implications for the constitution's audit principle. Do not let spec 004 build on top of it unresolved.
2. **B1 — Gate empty OpenAPI.** Spec 004 will emit the first real contracts; a CI fail-fast on `paths == []` prevents silent empty-publish regressions.
3. **A1 — Decide on `preview-deploy` token**: create the secret, or drop it from required checks, before admin_web scaffolding lands.
4. **C2 — Flatten `services/backend_api/services/`** in a dedicated PR before any `Features/` slices appear. Rename cost grows linearly with feature count.
5. **E1/E2 — Close T077 + tick DoD** on a tiny closeout PR so Spec 003 is officially shippable.

Everything else can be tracked as issues and deferred.
