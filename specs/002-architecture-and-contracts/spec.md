# Feature Specification: Architecture and Contracts

**Feature Branch**: `002-architecture-and-contracts`
**Created**: 2026-04-19
**Status**: Draft
**Constitution**: v1.0.0
**Input**: User description: "Phase 1A, spec 002 — architecture-and-contracts. Lock API design rules, ERD, permissions matrix, seven Principle-24 state models, testing strategy, CI/CD bootstrap, and finalize remaining ADRs. depends-on: 001."

## Clarifications

### Session 2026-04-19

- Q: What format must state machine diagrams use? → A: Mermaid `stateDiagram-v2` — renders natively in GitHub, text-diffable in PRs, CI-validatable.
- Q: How are multi-vendor-readiness annotations expressed in the ERD? → A: Nullable `vendor_id` foreign key on every ownable entity — its presence is the annotation; absent means the entity is non-ownable.
- Q: Which B2B roles must the Phase-1 permissions matrix cover? → A: Four B2B roles — B2B buyer, B2B approver, branch admin, company owner.
- Q: Is testing strategy coverage posture qualitative or quantitative? → A: Qualitative — mandatory scenario types per spec category (every state transition, every error branch, every permission boundary); no numeric thresholds.
- Q: What approval gate applies to ERD amendments? → A: One human code-owner approval; PR must include a downstream-impact note listing all active specs affected by the amendment.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Every domain has one authoritative data model before any domain spec begins (Priority: P1)

As the **platform owner**, I need a single ratified entity-relationship model covering every Phase-1 domain — identity, catalog, search, pricing/tax, inventory, cart, checkout, orders, invoices, returns, verification, quotes/B2B, promotions, reviews, support, CMS, notifications, shipping, payments — before any domain spec begins, so that specs cannot invent conflicting entities and the resulting database is coherent from the start.

**Why this priority**: Without a ratified data model, each later spec redefines its own entities and the combined schema becomes incoherent. Fixing schema drift post-implementation is expensive and risky. This is the highest blast-radius risk in the entire program.

**Independent Test**: Open the ratified ERD artifact. Pick any Phase-1 domain. Confirm that its primary entities, attributes, keys, and relationships to adjacent domains are already present. Confirm the ERD is versioned.

**Acceptance Scenarios**:

1. **Given** a contributor starts authoring any Phase-1 domain spec, **When** they consult the ERD, **Then** the primary entities for that domain are already named, attributed, and related to adjacent domains with no gaps.
2. **Given** a domain spec proposes a new entity not in the ERD, **When** the pull request is reviewed, **Then** it is blocked until an ERD amendment is reviewed and merged separately.
3. **Given** the ERD is amended, **When** the change merges, **Then** the version number is bumped and the date of change is recorded.
4. **Given** two entities in different domains share a foreign-key relationship, **When** a contributor reads the ERD, **Then** the ownership direction is stated explicitly and unambiguously.

---

### User Story 2 — Every Principle-24 state domain has a diagrammed, reviewed state machine (Priority: P1)

As a **contributor implementing any domain spec**, I need each of the seven Principle-24 domains (verification, cart, payment, order, shipment, return, quote) to have a complete, reviewed state machine before I implement behavior against it, so that I cannot invent ambiguous status fields or undefined transitions.

**Why this priority**: Ambiguous order/payment/refund statuses are the single largest source of commerce-platform bugs. Explicit, pre-agreed state machines are the cheapest prevention. Constitution Principle 24 mandates them; this spec delivers them.

**Independent Test**: Open the seven state-machine artifacts. For each, confirm: complete enumeration of valid states; the allowed transitions; the actor permitted to trigger each; the event that triggers it; the behavior on failure or timeout.

**Acceptance Scenarios**:

1. **Given** any of the seven state machines is ratified, **When** a domain spec proposes a new status value, **Then** the domain spec is blocked until the state machine is amended and re-ratified.
2. **Given** a state transition is attempted by an actor not listed as permitted, **When** the system evaluates the transition request, **Then** the transition is rejected with a reason — no implicit promotion.
3. **Given** a transition fails due to a downstream error, **When** the system handles the failure, **Then** the state machine dictates exactly which state the entity remains in or reverts to, with no ambiguity.
4. **Given** a timeout occurs on a time-bounded state (e.g., soft-hold reservation), **When** the TTL expires, **Then** the state machine defines the resulting state and the triggering mechanism.

---

### User Story 3 — Role-based access is answerable from one authoritative matrix (Priority: P1)

As a **reviewer or compliance auditor**, I need a single permissions matrix that maps every role to every resource and every action with a stated outcome (allowed, denied, or conditional), so that I can instantly verify whether any behavior is authorized without reading scattered spec text.

**Why this priority**: Permissions inconsistencies across 29 domain specs are a major audit and security risk. One matrix eliminates this class of defect by making the answer to any (role, resource, action) query a lookup, not an interpretation.

**Independent Test**: Pick any (role, resource, action) triple from any Phase-1 domain spec. Open the permissions matrix. Find the answer in at most two steps. Confirm the answer is unambiguous and matches the spec.

**Acceptance Scenarios**:

1. **Given** any domain spec needs to gate an action by role, **When** the author consults the permissions matrix, **Then** the answer already exists without per-spec invention.
2. **Given** a role is added or removed, **When** the matrix is updated, **Then** every cell intersecting that role is explicitly reviewed and set.
3. **Given** two specs address the same (role, resource, action) triple with different answers, **When** the conflict is detected, **Then** the matrix wins and the spec is revised.

---

### User Story 4 — Testing expectations are defined once and applied consistently (Priority: P2)

As a **contributor completing any Phase-1 spec**, I need a written testing strategy that states, per spec category, which test layers are required and at what expected depth, so that completion means the same thing to every author and reviewer across all 29 specs.

**Why this priority**: Inconsistent test coverage across 29 specs produces fragile launch readiness. A single strategy makes the bar uniform, mechanical to apply, and easy to verify.

**Independent Test**: Read the testing strategy document. For any spec category (backend domain, Flutter screen, admin module, integration adapter), determine the required test layers and expected coverage posture in under 30 seconds without interpretation.

**Acceptance Scenarios**:

1. **Given** any completed spec, **When** a reviewer applies the testing strategy, **Then** they reach the same pass/fail verdict as the author.
2. **Given** the strategy is amended, **When** the amendment merges, **Then** every active spec adopts it at next review.
3. **Given** a novel spec category not in the strategy, **When** it is identified, **Then** the strategy must be amended before that spec can pass DoD.

---

### User Story 5 — The CI/CD pipeline is ready for the first domain spec (Priority: P2)

As the **first contributor on a domain spec**, I need the CI/CD pipeline to already build, test, emit API artifacts, diff contracts, and produce preview environments for pull requests before I write a line of domain code, so that I inherit a working pipeline rather than having to build it inside a domain spec.

**Why this priority**: Pipeline setup pushed into domain specs causes inconsistency. One bootstrap keeps the pipeline coherent across all 29 specs.

**Independent Test**: Open a pull request touching each of backend, Flutter, admin, and shared contracts. Confirm the pipeline runs build, test, contract-diff, and lint/format for each, and delivers a preview deployment for the admin app.

**Acceptance Scenarios**:

1. **Given** any pull request touching the backend, **When** CI runs, **Then** an API description artifact is emitted and contract-diff is checked.
2. **Given** a pull request touches the admin app, **When** CI completes successfully, **Then** a short-lived preview URL is available for the reviewer.
3. **Given** a pull request breaks any test, **When** CI runs, **Then** the failure is reported and merge is blocked.

---

### User Story 6 — No ADR remains Proposed outside the Stage-7 deferral list (Priority: P2)

As the **platform owner**, I need every ADR in scope to be either Accepted or explicitly Stage-7-deferred by the close of this spec, so that no domain spec begins under ambiguous architectural ground.

**Why this priority**: A Proposed ADR leaks ambiguity into every spec that touches its domain. Closing or explicitly deferring every ADR now bounds the rework surface for all 29 downstream specs.

**Independent Test**: Open the ADR section of the implementation plan. Confirm that ADRs 001–006 and 010 show `Accepted` with a Decision line. Confirm that ADRs 007, 008, 009 show `Proposed` with an explicit Stage-7 deferral note. Confirm no other ADR is in the `Proposed` state without a deferral note.

**Acceptance Scenarios**:

1. **Given** Phase 1A is closing, **When** the ADR section is reviewed, **Then** ADRs 007, 008, 009 are the only ones in `Proposed` and all others are `Accepted`.
2. **Given** a domain spec references an Accepted ADR, **When** a change contradicts it, **Then** a new superseding ADR must be authored and accepted before the domain spec merges.
3. **Given** an ADR is accepted, **When** it is amended, **Then** a new ADR number is issued that supersedes it; the original ADR's status is set to `Superseded by ADR-NNN`.

---

### Edge Cases

- **Two domains share an entity with conflicting ownership definitions**: The ERD MUST resolve ownership to exactly one domain. Circular ownership is rejected at review.
- **A state machine defines a transition that violates an ERD invariant**: The pull request is rejected; the invariant must be renegotiated explicitly before either artifact merges.
- **A permissions matrix cell conflicts with a domain spec's stated behavior**: The matrix wins; the spec is revised before merge.
- **A new spec category emerges that the testing strategy does not cover**: The strategy is amended before that spec passes DoD — no silent exceptions.
- **CI preview environment fails to provision**: The pull request is held by the same CI gate as any failed check — no bypass path.
- **An ERD amendment is needed mid-Phase-1B**: The amendment PR includes a downstream-impact note listing every active spec affected. One code-owner approves. After merge, every listed spec records the new ERD version at its next review checkpoint.
- **An ADR is reopened after being accepted**: A new superseding ADR must be authored; the original is not edited in place.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A single, versioned entity-relationship document MUST exist covering every Phase-1 domain with: entities, attributes, primary keys, foreign keys, relationships, and ownership boundaries. Multi-vendor readiness MUST be expressed via a nullable `vendor_id` foreign key on every ownable entity — its presence in the schema is the annotation; entities without it are non-ownable by design. This pattern MUST be applied consistently across all Phase-1 domain data models.
- **FR-002**: The ERD MUST be versioned; every amendment MUST increment the version and record the amendment date and author. ERD amendment pull requests MUST require one human code-owner approval and MUST include a "downstream impact" note in the PR description listing every active Phase-1 spec that references entities touched by the amendment.
- **FR-003**: Seven state machines MUST exist — one per Principle-24 domain (verification, cart, payment, order, shipment, return, quote) — each specifying: enumerated states, allowed transitions, triggering event per transition, authorized actor per transition, behavior on failure, and behavior on timeout where applicable.
- **FR-004**: Each state machine MUST be expressed in Mermaid `stateDiagram-v2` format (visual, GitHub-native, text-diffable) AND in a structured Markdown table (states, transitions, authorized actor, trigger, failure behavior, timeout behavior). A CI step MUST validate that every Mermaid diagram in the state-machine documents is parseable.
- **FR-005**: A single, versioned permissions matrix MUST exist mapping every defined role to every resource to every action with an outcome: allowed, denied, or conditional (condition stated).
- **FR-006**: The permissions matrix MUST cover at minimum the following roles across every Phase-1 domain: guest, customer, professional (verified customer), B2B buyer, B2B approver, B2B branch admin, B2B company owner, admin (read-only), admin (write), admin (super). B2B branch admin and B2B company owner are first-class roles in the Phase-1 matrix — not aliases of any admin role.
- **FR-007**: A single, versioned testing strategy document MUST exist specifying, per spec category, the required test layers and a qualitative coverage posture expressed as mandatory scenario types — not numeric percentages. Example posture for a backend domain spec: "every state transition must have at least one test; every error branch must have at least one test; every permission boundary must have at least one test." No numeric coverage threshold is required.
- **FR-008**: The testing strategy MUST cover at minimum: backend domain specs, Flutter customer-app specs, Next.js admin specs, integration-adapter specs, and shared-contract specs.
- **FR-009**: The CI/CD pipeline MUST build, test, lint/format, and run contract-diff on every pull request for every changed package.
- **FR-010**: The CI/CD pipeline MUST emit the backend's API description as a build artifact on every pull request and every merge to `main`.
- **FR-011**: The CI/CD pipeline MUST produce a short-lived preview deployment of the admin app on every pull request that changes the admin app.
- **FR-012**: Every ADR in scope MUST reach `Accepted` status (with a concrete, single-line Decision) or be explicitly marked `Proposed – deferred to Stage 7` before Phase 1A closes.
- **FR-013**: All ratified architecture artifacts (ERD, state machines, permissions matrix, testing strategy) MUST be discoverable from a single index document so that contributors never have to search.
- **FR-014**: Any domain spec MUST reference the ERD section, state machine(s), permissions-matrix rows, and testing-strategy slice that apply to it; missing references block the spec from passing DoD.
- **FR-015**: When any architecture artifact is amended, every active domain spec that references it MUST record the new version at its next review checkpoint.

### Key Entities

- **Entity-relationship model**: Versioned document listing all Phase-1 entities and their relationships. Attributes: version, amendment date, entities (name, attributes, keys, relationships, ownership domain). Multi-vendor readiness expressed via nullable `vendor_id` FK on ownable entities — not a separate flag field.
- **State machine**: Versioned diagram + table for one Principle-24 domain. Attributes: domain, version, states (enumerated), transitions (from-state, to-state, trigger, authorized actor, failure behavior, timeout behavior).
- **Permissions matrix**: Versioned table mapping roles × resources × actions to outcomes. Attributes: version, roles, resources, actions, outcome per cell, condition text where conditional.
- **Testing strategy**: Versioned document mapping spec categories to required test layers and coverage posture. Attributes: version, spec categories, required layers per category, expected posture.
- **ADR**: Architecture Decision Record. Attributes: number, title, status (Proposed / Accepted / Superseded), context, decision, consequences, superseded-by reference.
- **Architecture artifact index**: A single document linking to all four architecture artifacts with their current versions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Phase-1 domain specs find their entities, state transitions, permissions rows, and testing expectations in the ratified artifacts without needing to invent new ones at spec time.
- **SC-002**: Zero Phase-1 domain specs reach Draft status (defined as: `spec.md` exists in `specs/NNN-name/` directory) before the ERD covers their primary entities.
- **SC-003**: A reviewer applying the permissions matrix to any (role, resource, action) query from any Phase-1 spec produces the same answer as the author in at least 95% of sampled reviews over the first 100 reviews.
- **SC-004**: A reviewer applying the testing strategy to any completed Phase-1 spec produces the same pass/fail verdict as the author in at least 95% of sampled reviews over the first 100 reviews.
- **SC-005**: Every pull request touching the backend or shared contracts passes the contract-diff check with zero unintended client regressions, measured over the first 100 such pull requests.
- **SC-006**: A new contributor can locate the ERD, all seven state machines, the permissions matrix, and the testing strategy within 90 seconds of opening the repository.
- **SC-007**: At Phase-1A close, zero ADRs remain `Proposed` outside the explicit Stage-7 deferral list (ADRs 007, 008, 009).
- **SC-008**: Every pull request that changes the admin app produces a usable preview deployment in at least 95% of cases across the first 100 such pull requests.

## Assumptions

- Spec 001 is at DoD: the repository guardrails, CODEOWNERS, CI guardrails, and DoD are live before this spec begins.
- Constitution version 1.0.0 (ratified 2026-04-19) governs this spec.
- ADRs 001 (monorepo), 002 (Bloc), 003 (vertical slice + MediatR), 004 (EF Core 9), 005 (Meilisearch), 006 (Next.js + shadcn/ui), and 010 (Azure Saudi Arabia Central) are already Accepted at the start of this spec. ADRs 007, 008, 009 are Proposed and explicitly deferred to Stage 7.
- All seven Principle-24 state-machine domains are in scope; no Phase-1 domain escapes the state-machine requirement.
- "Testing strategy" defines coverage expectations and required layers — not raw numeric thresholds. Precise per-spec thresholds are set at plan time.
- The CI preview environment for the admin app is a convenience for reviewers; it is not a merge-gate requirement.
- ERD, state machines, permissions matrix, and testing strategy are living documents: they are amended via pull request, version-bumped, and referenced by downstream specs — they are never finalized and frozen.
- Data residency for all artifacts follows ADR-010; no additional residency work is required in this spec.
- The architecture artifact index is a single Markdown file linking to the four documents; it does not duplicate content.
