# Tasks: Governance and Setup

**Input**: Design documents from `specs/phase-1A/001-governance-and-setup/`
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ci-pipeline-contract.md ✅

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no pending dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5 from spec.md)
- Exact file paths are included in every task description

---

## Phase 1: Setup

**Purpose**: Repository initialization and shared baseline — no feature tasks until this is complete.

- [X] T001 Initialize monorepo with Git; set default branch to `main`; add `.gitignore` at repo root covering OS, editor, env/secrets, Node.js, .NET, Flutter, Docker, logs, and test output patterns
- [X] T002 Create ADR-001 skeleton: `apps/customer_flutter/.gitkeep`, `apps/admin_web/.gitkeep`, `services/backend_api/.gitkeep`, `packages/shared_contracts/.gitkeep`, `packages/design_system/.gitkeep`, `infra/.gitkeep`, `scripts/.gitkeep`
- [X] T003 [P] Add `.editorconfig` at repo root: UTF-8, LF line endings, 4-space indent for .NET/Dart/YAML; 2-space for JS/TS/JSON/Markdown
- [X] T004 Add `scripts/extract-adr-block.sh`: extracts the ADR §7 content from `docs/implementation-plan.md` and writes it to stdout (used by fingerprint and context generation scripts)
- [X] T005 [P] Add `scripts/compute-fingerprint.sh`: computes SHA-256 of the byte-concatenation of `.specify/memory/constitution.md` and the output of `scripts/extract-adr-block.sh`; prints the hex digest

**Checkpoint**: Repo initialised with correct layout. `scripts/compute-fingerprint.sh` runs and outputs a stable hex digest.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure every user story depends on. No user-story work begins until this phase is complete.

**⚠️ CRITICAL**: Phases 3–7 all depend on this phase being complete.

- [X] T006 Create `scripts/gen-agent-context.sh`: reads `.specify/memory/constitution.md` and the ADR Decisions table (via `scripts/extract-adr-block.sh`), then writes the three per-agent context files: `CLAUDE.md`, `.codex/system.md`, `GLM_CONTEXT.md`. Each file must embed: all 32 Principles, the ADR Decisions table, the four guardrails, the `docs/dod.md` reference, and the current fingerprint comment header
- [X] T007 [P] Run `scripts/gen-agent-context.sh` and commit: `CLAUDE.md`, `.codex/system.md`, `GLM_CONTEXT.md` at repo root
- [X] T008 Create `docs/` directory; add `docs/dod.md` as an empty stub with `## Universal Core` and `## Applicability-Tagged Items` headings (content filled in Phase 5)

**Checkpoint**: `CLAUDE.md` exists and contains all 32 Principles and the ADR Decisions table. `docs/dod.md` exists.

---

## Phase 3: User Story 2 — AI Session Context Injection (Priority: P1) 🎯

> **Ordering note**: US2 (agent context) is implemented before US1 (CI guardrails) because the fingerprint CI job (US1, T015) references context files that must already exist. This ordering is intentional — it does not reflect a priority difference between the two P1 stories.

**Goal**: Every AI agent session starts with the constitution and ADR Decisions table already loaded — no manual paste required.

**Independent Test**: Open a fresh Claude/Codex/GLM session in the repo. Without manual input, confirm the session's initial context contains the 32 Principles and the ADR Decisions table. Confirm `scripts/compute-fingerprint.sh` output matches the hash that the CI fingerprint job will compute.

- [X] T009 [US2] Verify `CLAUDE.md` (from T007) contains: Principles 1–32 verbatim, ADR Decisions table (all 10 ADRs with status and Decision line), four-guardrail summary, DoD reference, and `<!-- context-fingerprint-source: ... -->` comment
- [X] T010 [P] [US2] Verify `.codex/system.md` (from T007) carries identical constitution + ADR content to `CLAUDE.md`
- [X] T011 [P] [US2] Verify `GLM_CONTEXT.md` (from T007) carries identical constitution + ADR content to `CLAUDE.md`
- [X] T012 [US2] Write a shell test `scripts/tests/test-gen-agent-context.sh` that: runs `gen-agent-context.sh`, reads `CLAUDE.md`, and asserts the phrase "Principle 32" and "ADR-010" both appear; exits non-zero if either is missing

**Checkpoint**: Running `scripts/tests/test-gen-agent-context.sh` exits 0. All three agent context files are present and contain governing content.

---

## Phase 4: User Story 1 — CI Guardrails and Branch Protection (Priority: P1) 🎯

**Goal**: Every pull request to `main` is blocked unless all automated guardrails pass and a human code-owner approves. Constitution/ADR PRs require two approvers. AI commits cannot reach `main`.

**Independent Test**: Open a draft PR that (a) breaks formatting, (b) omits the context fingerprint, (c) modifies the constitution file. Confirm all three merge-blockers fire independently and that the PR cannot merge until every blocker is resolved and a human code-owner approves.

- [X] T013 [US1] Create `.github/workflows/lint-format.yml`: path-filtered job that runs `dotnet format --verify-no-changes` for `services/backend_api/**`, `dart format --set-exit-if-changed` for `apps/customer_flutter/**`, and `eslint + prettier --check` for `apps/admin_web/**`, `packages/**`. Any non-zero exit → job fails. Job name must be `lint-format` (required status check name used in branch protection).
- [X] T014 [P] [US1] Create `.github/workflows/contract-diff.yml`: builds `services/backend_api` and emits `openapi.json` as an artifact; runs `oasdiff` comparing the emitted spec against `packages/shared_contracts/openapi.json` on `main`; fails on any diff. Job name: `contract-diff`.
- [X] T015 [P] [US1] Create `.github/workflows/verify-context-fingerprint.yml`: extracts `<!-- context-fingerprint: <hex> -->` from the PR description body; runs `scripts/compute-fingerprint.sh` against the current `main` HEAD; fails when the two hashes differ or the marker is missing. Job name: `verify-context-fingerprint`.
- [X] T016 [P] [US1] Create `.github/workflows/build-and-test.yml`: runs build and test for all packages on every PR. Job name: `build`.
- [X] T017 [US1] Create `.github/CODEOWNERS` with rules per `contracts/ci-pipeline-contract.md`: `.specify/memory/constitution.md` → two named human code-owners; `docs/implementation-plan.md` → two named human code-owners; `.github/CODEOWNERS` → two named human code-owners; `docs/dod.md` → one human code-owner; `*` (catchall) → one human code-owner. Use placeholder names (`@org/platform-leads`) until real GitHub usernames are confirmed.
- [X] T018 [US1] Enable branch protection on `main` via GitHub repository settings (document the exact settings in `docs/branch-protection.md`): required status checks: `lint-format`, `contract-diff`, `verify-context-fingerprint`, `build`, `preview-deploy`; require signed commits; only squash-merge allowed; dismiss stale reviews; require branches to be up-to-date before merge; prevent force push; prevent branch deletion; required reviewers: 1 (CODEOWNERS handles per-path overrides to 2).
- [X] T018a [US1] Add `preview-deploy` job to `.github/workflows/build-and-test.yml`: on every PR that touches `apps/admin_web/**`, build the Next.js admin app and deploy to Azure Static Web Apps (using the `Azure/static-web-apps-deploy` action with `action: upload`); job name must be `preview-deploy`; post the preview URL as a PR comment; on PR close, run `action: close` to tear down. Document the required Azure Static Web Apps resource and the `AZURE_STATIC_WEB_APPS_API_TOKEN` secret in `docs/branch-protection.md`.

**Checkpoint**: Open a PR → confirm all four CI jobs appear as required checks. Attempt to merge without approval → blocked. Attempt direct push to `main` → rejected. Attempt PR editing constitution file → two-reviewer approval required.

---

## Phase 5: User Story 3 — Definition of Done (Priority: P2)

**Goal**: A single, versioned, layered Definition of Done document governs every spec in the program.

**Independent Test**: Open `docs/dod.md`. Apply the Universal Core checklist to this spec (001). Confirm every UC item is verifiable against delivered artifacts without interpretation. Confirm the five applicability tags exist and have clear trigger conditions.

- [X] T019 [US3] Fill `docs/dod.md` with full content per `data-model.md` §Artifact: Definition of Done:
  - Add version header: `**Version**: 1.0 | **Date**: 2026-04-19`
  - `## Universal Core` section with all 8 UC items (UC-1 through UC-8) as a checkbox list, each with: item text, how it is verified (automated/manual/partial), and the spec reference (FR number)
  - `## Applicability-Tagged Items` section with all 5 triggers (`state-machine`, `audit-event`, `storage`, `pdf`, `user-facing-strings`), each as `### [trigger: <name>]` with the required check described
- [X] T020 [US3] Add DoD version reference to `CLAUDE.md`, `.codex/system.md`, and `GLM_CONTEXT.md` under the "How to work in this repo" section (re-run `scripts/gen-agent-context.sh` after updating the template to include the DoD path)

**Checkpoint**: Open `docs/dod.md` and confirm the Universal Core section has exactly 8 items, each with a verification method. Confirm the five applicability-tagged sections exist with trigger headers in the required format.

---

## Phase 6: User Story 4 — Predictable Repository Layout (Priority: P2)

**Goal**: A new contributor can locate any of the seven canonical top-level folders within 60 seconds of cloning, without reading documentation.

**Independent Test**: Remove local clone. Re-clone. Run `ls -1` at repo root. Confirm presence of exactly: `apps/`, `services/`, `packages/`, `infra/`, `scripts/`, plus root config files. Elapsed time < 60 seconds.

- [X] T021 [US4] Write `CONTRIBUTING.md` at repo root covering: monorepo layout (map of seven folders with one-line purpose each), four guardrails summary, commit signing setup steps (GPG key generation + GitHub upload + git config), squash-merge policy, DoD reference, and agent context injection instructions (how to embed the fingerprint in a PR description)
- [X] T022 [P] [US4] Write `docs/repo-layout.md` as a deeper reference: ADR-001 layout rationale, where each deliverable type lives (backend endpoints, Flutter screens, admin pages, shared types, tokens, infra definitions, utility scripts), and how to add a new package

**Checkpoint**: A fresh clone with `ls -1` shows the seven folders. `CONTRIBUTING.md` and `docs/repo-layout.md` are present and navigable.

---

## Phase 7: User Story 5 — Constitution Version Recording (Priority: P2)

**Goal**: Every spec records the constitution version in force at authoring time. An auditor can cross-reference any spec to the governing principles that applied without git archaeology.

**Independent Test**: Open `specs/phase-1A/001-governance-and-setup/spec.md`. Confirm the constitution version is recorded in the spec header. Open `.github/pull_request_template.md`. Confirm it includes a field for constitution version.

- [X] T023 [US5] Create `.github/pull_request_template.md` with these sections: spec link (`Spec: specs/<NNN>-<name>/spec.md`), constitution version field (`Constitution: v<semver>`), Universal Core checklist (8 UC items as checkboxes), active applicability tags declaration (`Active tags: none / state-machine / audit-event / ...`), summary field, and the squash-merge commit format reminder
- [X] T024 [US5] Update `specs/phase-1A/001-governance-and-setup/spec.md` header to include the explicit constitution version line: `**Constitution**: v1.0.0` (verify `spec.md` already satisfies UC-8 after this change)

**Checkpoint**: Create a test PR using the template. Confirm the constitution version field appears pre-populated in the description. Confirm `specs/phase-1A/001-governance-and-setup/spec.md` header explicitly records `v1.0.0`.

---

## Phase 8: Polish and Cross-Cutting Concerns

**Purpose**: Final verification, documentation consistency, and DoD sign-off for this spec.

- [X] T025 Run `quickstart.md` verification for all five phases (A–E) and confirm every "Verify" step passes; document any failures as issues before merge
- [X] T026 [P] Verify `specs/phase-1A/001-governance-and-setup/spec.md` satisfies all Universal Core DoD items (UC-1 through UC-8); tick them in `checklists/requirements.md`; confirm no applicability tags are active (no state machine, no audit events, no storage, no PDF, no user-facing strings in this spec)
- [X] T027 [P] Run a full end-to-end smoke test: open a PR from a feature branch, confirm all four CI jobs appear, confirm the fingerprint check fires, confirm squash-merge is the only available merge method, confirm a direct push to `main` is rejected
- [X] T028 Commit all deliverables on the `001-governance-and-setup` branch and open a PR. Ensure the PR description includes the context fingerprint. Mark all UC checklist items in the PR template.

**Checkpoint**: All eight UC items in `docs/dod.md` pass for this spec. PR is ready for human code-owner review.

---

## Dependencies and Execution Order

### Phase dependencies

| Phase | Depends on | Blocks |
|---|---|---|
| Phase 1 (Setup) | — | Phase 2 |
| Phase 2 (Foundational) | Phase 1 | All user story phases |
| Phase 3 (US2 — Agent context) | Phase 2 | Phase 4 (fingerprint used by CI) |
| Phase 4 (US1 — CI + branch protection) | Phase 2, Phase 3 | Phase 8 |
| Phase 5 (US3 — DoD) | Phase 2 | Phase 8 |
| Phase 6 (US4 — Repo layout) | Phase 1 | Phase 8 |
| Phase 7 (US5 — Version recording) | Phase 2 | Phase 8 |
| Phase 8 (Polish) | Phases 3–7 | — |

### Parallel opportunities

- **Within Phase 1**: T003 and T005 can run in parallel after T001 and T002.
- **Within Phase 3**: T010 and T011 can run in parallel with T009 once T007 is done.
- **Within Phase 4**: T014, T015, T016 can all run in parallel with T013.
- **Within Phase 6**: T022 can run in parallel with T021.
- **Within Phase 8**: T026 and T027 can run in parallel after T025.

### Minimum path to US1 (P1) being independently testable

T001 → T002 → T003 (parallel) → T004 → T005 → T006 → T007 → T013 → T014 (parallel) → T015 (parallel) → T016 (parallel) → T017 → T018

---

## Parallel Execution Example: Phase 4 (US1 — CI Guardrails)

```
Once T012 (Foundational: agent context scripts done) is complete:

Parallel block A:
  Task T013: .github/workflows/lint-format.yml
  Task T014: .github/workflows/contract-diff.yml
  Task T015: .github/workflows/verify-context-fingerprint.yml
  Task T016: .github/workflows/build-and-test.yml

Sequential after all four workflows exist:
  Task T017: .github/CODEOWNERS
  Task T018: branch protection settings + docs/branch-protection.md
```

---

## Implementation Strategy

### MVP (User Stories 1 and 2 — both P1)

1. Complete Phase 1: Setup (T001–T005)
2. Complete Phase 2: Foundational (T006–T008)
3. Complete Phase 3: US2 — Agent context (T009–T012)
4. Complete Phase 4: US1 — CI + branch protection (T013–T018)
5. **STOP and VALIDATE**: confirm merge gate fires, fingerprint check passes, agent context is present in a fresh session

### Full delivery (all five user stories)

1. MVP above
2. Phase 5: US3 — DoD (T019–T020)
3. Phase 6: US4 — Repo layout docs (T021–T022) — can run in parallel with Phase 5
4. Phase 7: US5 — Version recording (T023–T024) — can run in parallel with Phases 5–6
5. Phase 8: Polish (T025–T028)

---

## Notes

- `[P]` tasks can run in parallel — they write to different files and have no dependencies on incomplete sibling tasks
- `[Story]` label maps each task to the user story it satisfies for traceability and independent testing
- No test tasks are generated (spec does not request TDD; CI validation serves as the test layer for this governance spec)
- Every task description includes an exact file path — no interpretation required
- This spec has no active applicability tags (no state machine, no audit events, no storage abstraction, no PDF rendering, no user-facing strings)
- Commit after each phase checkpoint — do not batch all phases into one commit
