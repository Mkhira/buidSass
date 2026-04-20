# Feature Specification: Governance and Setup

**Feature Branch**: `001-governance-and-setup`
**Created**: 2026-04-19
**Status**: Draft
**Constitution**: v1.0.0
**Input**: User description: "Phase 1A, spec 001 — governance-and-setup. Establish the Definition of Done, repository layout (ADR-001), CI skeleton, branch protection, CODEOWNERS, agent-context injection pattern, and the working cadence that every subsequent spec for the dental commerce platform inherits."

## Clarifications

### Session 2026-04-19

- Q: How many human code-owner approvals are required for changes to the constitution or ADR block? → A: Two code-owners required on constitution/ADR edits; one on ordinary PRs.
- Q: How is "constitution + ADR context was present during authoring" verified? → A: Automated — CI verifies a context fingerprint recorded in the PR description against the current canonical hash.
- Q: Are signed commits required on `main`? → A: Yes — signed commits required on every merge to `main`.
- Q: Are AI-authored commits allowed on `main`? → A: No — AI may author on feature branches; `main` accepts only human-signed commits. Squash-merge collapses AI commits under the merging human's signature.
- Q: Is the Definition of Done a flat list, layered by applicability, or per-stage? → A: Layered — a universal core applies to every spec, plus applicability-tagged items that activate when their trigger is present (state machine, audit events, storage, PDF, etc.).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Merges are blocked unless guardrails pass and a human approves (Priority: P1)

As the **platform owner**, I need every pull request to `main` to pass automated guardrails **and** receive at least one approving review from a human code-owner before it lands, so that AI coding agents (Claude, Codex, GLM) cannot silently drift from the constitution, ADRs, or quality bar.

**Why this priority**: This single outcome is the program's primary brake on AI drift. Without an enforced merge gate, every downstream spec inherits uncontrolled risk. With it, every later spec inherits a safe default.

**Independent Test**: Open a pull request that violates formatting, fails the contract-diff job, and edits the constitution file. Confirm the merge button stays disabled, the failures are listed, and no one can merge until all are resolved and a human code-owner review is recorded.

**Acceptance Scenarios**:

1. **Given** a pull request that breaks the lint/format bar, **When** CI runs, **Then** the merge is blocked until the job turns green.
2. **Given** a pull request that changes the backend's interface without regenerating the shared contracts, **When** CI runs, **Then** the contract-diff job fails and the merge is blocked.
3. **Given** a pull request that modifies the constitution file or the ADR block of the implementation plan, **When** it is opened, **Then** an explicit human code-owner approval is required before merge, regardless of other reviewers.
4. **Given** any pull request to `main`, **When** zero approving human reviews are recorded, **Then** the merge is blocked.
5. **Given** a pull request that succeeds all checks and gains human approval, **When** the merger clicks merge, **Then** the commit includes a traceable record of which human(s) approved it.

---

### User Story 2 — Every AI session starts with the governing context already loaded (Priority: P1)

As an **AI coding agent** (Claude, Codex, or GLM) starting a fresh session to work on a spec, I need the constitution's enforced principles and the current ADR Decisions table to be present in my working context automatically, so that I cannot recommend a choice that silently contradicts a ratified principle or an accepted architectural decision.

**Why this priority**: Cold-start sessions are the single biggest source of AI drift. Pre-loading the governing context is the only way to make every later spec session safe by default.

**Independent Test**: Open a fresh session for any downstream spec (e.g., `004-identity-and-access`). Without pasting anything manually, confirm the session's initial context contains the constitution's principles and the ADR Decisions table.

**Acceptance Scenarios**:

1. **Given** a fresh agent session is opened in the repository, **When** it initializes, **Then** the constitution's enforced principles and the ADR Decisions table are present in the session context with no manual step required.
2. **Given** a spec is about to be authored, **When** the session begins work, **Then** the initial context explicitly references the active constitution version and the ADRs in scope.
3. **Given** an agent proposes a change that conflicts with a ratified principle or an accepted ADR, **When** it drafts the output, **Then** the conflict is surfaced and flagged rather than executed silently.

---

### User Story 3 — Contributors know exactly what "Done" means (Priority: P2)

As a **contributor** (human or AI) finishing any spec, I need a single, versioned Definition of Done that applies to every spec in the program, so that completion is verified the same way every time and reviewers reach the same verdict as authors.

**Why this priority**: Consistent completion criteria across all Phase-1 specs is what makes the program auditable. Without a shared DoD, each spec ships under a different implicit bar.

**Independent Test**: Read the DoD checklist. Pick any later spec at random. Confirm that every applicable item is verifiable against the delivered artifact without interpretation.

**Acceptance Scenarios**:

1. **Given** a contributor finishes a spec, **When** they apply the DoD, **Then** every item yields a clear pass or fail with no ambiguity.
2. **Given** a reviewer applies the DoD to the same spec, **When** they complete review, **Then** they reach the same verdict as the contributor.
3. **Given** the DoD is amended, **When** the amendment merges, **Then** every active spec adopts it at the next review.

---

### User Story 4 — Repository layout is predictable on first clone (Priority: P2)

As any **contributor**, when I open the repository for the first time, I need the folder layout to match ADR-001 exactly, so that I never waste time hunting for the Flutter app, backend, admin app, shared contracts, design system, or infrastructure.

**Why this priority**: A predictable layout reduces onboarding and review time. Drift here causes confusion in every later spec.

**Independent Test**: Clone the repo fresh. Without reading documentation, locate each of the seven canonical top-level folders within 60 seconds.

**Acceptance Scenarios**:

1. **Given** a fresh clone, **When** a contributor lists the top-level folders, **Then** `apps/`, `services/`, `packages/`, `infra/`, and `scripts/` are present exactly as ADR-001 prescribes.
2. **Given** a pull request introduces a new top-level folder, **When** review runs, **Then** it requires an ADR-001 amendment and a human code-owner approval.

---

### User Story 5 — Every spec records the constitution version that governed it (Priority: P2)

As an **auditor** reviewing the program months after launch, I need every spec to state the constitution version that was in force when it was authored, so that I can reconstruct the governance context for any decision without relying on memory or git archaeology.

**Why this priority**: Auditability of governance over time is a Principle-25 outcome. Recording the version per spec turns every future audit into a lookup instead of an investigation.

**Independent Test**: Pick any spec. Confirm it references the constitution version that governed it and that the referenced version exists in the repository's history.

**Acceptance Scenarios**:

1. **Given** a spec is authored, **When** it is written, **Then** the governing constitution version is recorded in the spec itself.
2. **Given** the constitution is amended, **When** a later spec is authored, **Then** it records the new active version.
3. **Given** an audit request, **When** the auditor inspects any spec, **Then** the governing version can be cross-referenced to the repository's history.

---

### Edge Cases

- **Constitution or ADR edit lands with all CI green but without human code-owner approval**: The merge MUST be rejected regardless of CI state — human code-owner approval is mandatory and non-bypassable.
- **Agent session starts outside the repository** (e.g., a hosted runner without the context files): Output produced in that session MUST be treated as invalid and reauthored inside a compliant session.
- **Contract-diff fails because the backend interface has legitimately advanced**: The fix is to regenerate the shared contract package in the same pull request — there is no bypass path.
- **Emergency hotfix is needed outside business hours**: The guardrails and review still apply. There is no off-hours bypass.
- **Two pull requests race to merge and both depended on the same state**: The second one MUST re-run CI against the updated `main` before its merge button re-enables.
- **A DoD item is later proven wrong or impossible**: It MUST be corrected via an explicit amendment, not via silent per-spec exceptions.
- **A reviewer is also the author**: Self-approval is rejected; a different human code-owner must approve.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST use the folder layout specified in ADR-001 — `apps/customer_flutter`, `apps/admin_web`, `services/backend_api`, `packages/shared_contracts`, `packages/design_system`, `infra/`, `scripts/`.
- **FR-002**: The repository MUST enforce a code-owners configuration requiring at least one human approving review on every pull request to `main`.
- **FR-003**: The code-owners configuration MUST require **two** distinct human code-owner approving reviews on any change to the platform constitution file or the ADR block of the implementation plan. Ordinary pull requests remain subject to the single-approver rule of FR-002.
- **FR-004**: Branch protection on `main` MUST block merges whenever any required status check fails or when zero approving reviews are recorded.
- **FR-005**: CI MUST run a lint-and-format check on every pull request and report a failing status when any configured formatter or linter flags an issue.
- **FR-006**: CI MUST run a contract-diff check on every pull request and fail when the generated shared-contracts output has diverged from the backend's published interface.
- **FR-007**: Every fresh agent session opened in the repository MUST begin with the constitution's enforced principles and the current ADR Decisions table already present in its working context. Mechanism: `CLAUDE.md` for Claude Code sessions, `.codex/system.md` for Codex sessions, `GLM_CONTEXT.md` for GLM sessions. Coverage is limited to agents that support project-level context files loaded at session start.
- **FR-007a**: Every pull request authored with AI-agent assistance MUST carry, in its description, a fingerprint of the constitution + ADR Decisions context that was present in the authoring session. CI MUST fail the pull request when the recorded fingerprint does not match the canonical fingerprint of the current constitution + ADR Decisions at the head of `main`.
- **FR-008**: A single, versioned Definition of Done MUST exist and apply to every spec. The DoD MUST be layered into two tiers: a **universal core** that every spec must satisfy, and a set of **applicability-tagged** items that activate only when a stated trigger is present in the spec.
- **FR-009**: The Definition of Done universal core MUST include at minimum: acceptance scenarios pass; lint-and-format green; contract-diff green; constitution + ADR context fingerprint verified (FR-007a); no edits to the constitution or ADRs occurred outside a ratified amendment; pull request has the required human code-owner approvals (FR-002 / FR-003); all `main` commits signed by a human code-owner (FR-015); constitution version recorded (FR-013).
- **FR-009a**: The Definition of Done applicability-tagged items MUST include at minimum: **if the spec introduces a state machine**, its states, transitions, actors, triggers, and failure behavior are documented; **if the spec introduces a Principle-25 auditable action**, an audit event is emitted with actor, timestamp, resource, before/after, reason; **if the spec uses the shared storage abstraction**, residency routing (ADR-010) is respected; **if the spec renders PDFs**, Arabic + English layouts are rendered and visually regression-tested; **if the spec introduces user-facing strings**, Arabic editorial review is recorded before launch-readiness.
- **FR-009b**: Every pull request MUST declare which applicability tags are active for the spec it advances, and reviewers MUST verify each active tag.
- **FR-010**: Every pull request MUST use a template that references the Definition of Done and asks the author to confirm applicable items.
- **FR-011**: The working cadence MUST require human review on every pull request — no batched reviews at milestone boundaries.
- **FR-012**: The repository MUST reject any change to the constitution or ADR block that has not been approved by a human code-owner, even when all CI checks pass.
- **FR-013**: Every spec MUST record the constitution version in force at the time of authoring.
- **FR-014**: Self-approval on pull requests MUST be rejected — the approving reviewer must be a human code-owner other than the author.
- **FR-015**: Every commit merged to `main` MUST be cryptographically signed by a verified human code-owner identity. The merge MUST be blocked when one or more commits in the pull request carry an unsigned or unverified signature.
- **FR-016**: AI coding agents MAY author commits on feature branches but MUST NOT appear as the commit author on `main`. Merges to `main` MUST use a squash strategy so that all branch-side commits (including AI-authored ones) collapse into a single commit authored and signed by a human code-owner.
- **FR-017**: Branch protection MUST block any fast-forward or rebase-preserving merge strategy that would carry an AI-authored commit onto `main`.

### Key Entities

- **Governance artifact**: A file requiring the strongest protection — the constitution file and the ADR block. Attributes: current version, last-amended date, authorized human code-owners.
- **Definition of Done checklist**: The single authoritative completion list. Attributes: version, items, applicability notes per item.
- **Agent session context**: The set of governing documents that MUST be present when an AI agent begins work. Attributes: constitution version, ADR snapshot, timestamp, agent identifier.
- **Guardrail check**: An automated verification attached to every pull request. Attributes: name, required-status flag, owning pipeline, expected outcome description.
- **Pull request**: A proposed change to the repository. Attributes: author, files changed, required-check statuses, approving-review count, code-owner-required flag, mergeability state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of merges to `main` occur only after at least one approving human code-owner review and all required automated checks pass.
- **SC-002**: 100% of changes to the constitution or ADR block receive two explicit human code-owner approvals before merge.
- **SC-003**: 100% of pull requests authored with AI-agent assistance carry a matching constitution + ADR context fingerprint, enforced by CI on every PR.
- **SC-004**: A new contributor locates each of the five top-level directories (`apps/`, `services/`, `packages/`, `infra/`, `scripts/`) and all seven canonical package paths within 60 seconds of cloning, without reading documentation.
- **SC-005**: When a spec reaches review, author and reviewer independently applying the Definition of Done reach the same pass/fail verdict on every item in at least 9 of 10 reviews.
- **SC-006**: Zero changes to the constitution file land without a recorded human code-owner approval over the life of the program.
- **SC-007**: The lint-and-format guardrail catches at least 95% of style and formatting regressions before reviewer attention is required, measured over the first 50 pull requests.
- **SC-008**: The contract-diff guardrail catches 100% of backend-client interface mismatches before merge, measured over the first 50 pull requests touching the backend interface.
- **SC-009**: 100% of specs record the governing constitution version at authoring time.
- **SC-010**: 100% of commits on `main` carry a verified cryptographic signature from a human code-owner identity.
- **SC-011**: Zero commits on `main` are authored by an AI-agent identity, measured continuously over the life of the program.

## Assumptions

- The repository is a single monorepo per ADR-001; no separate repositories for frontend, backend, admin, contracts, or design system.
- Constitution version 1.0.0 (ratified 2026-04-19) is the governing document until a Principle-32 amendment ratifies a later version.
- The hosted version-control platform supports required status checks, branch protection, code-owner files, and pull-request templates natively.
- AI agents (Claude, Codex, GLM) can be configured to load repository-level context files at session start.
- Human code-owners are available for every pull request; there is no off-hours bypass path.
- "Human code-owner" excludes any AI account, service account, or bot account.
- Per-domain tooling (ERD editor, state-machine generator, storage/PDF/audit libraries, etc.) is out of scope here and belongs to spec 002 or later.
- Emergency hotfixes follow the same governance path as any other change.
