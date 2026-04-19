# Quickstart: Architecture and Contracts

**Branch**: `002-architecture-and-contracts` | **Date**: 2026-04-19
**Depends on**: spec 001 at DoD (repo layout, CI guardrails, CODEOWNERS, DoD all live)

Implementation phases in order. Each section lists deliverables and the verification step.

---

## Phase A — Architecture directory skeleton

**Deliverables**:
- `docs/architecture/` directory created.
- `docs/architecture/index.md` stub (empty link list with placeholder rows).
- `docs/architecture/state-machines/` directory with 7 empty `.md` stubs (one per domain).

**Verify**: `ls docs/architecture/` shows `index.md`, `erd.md` placeholder, `erd.dbml` placeholder, `permissions-matrix.md` placeholder, `testing-strategy.md` placeholder, and `state-machines/` directory.

---

## Phase B — Entity-Relationship Model

**Deliverables**:
- `docs/architecture/erd.md`: Mermaid `erDiagram` covering all 19 Phase-1 domains. Version header present. Nullable `vendor_id` FK on every ownable entity.
- `docs/architecture/erd.dbml`: DBML source matching the Mermaid diagram.

**Verify**:
1. Run `mmdc --input docs/architecture/erd.md --output /dev/null` — exits 0.
2. Open `docs/architecture/erd.md` on GitHub — diagram renders.
3. Every entity for: identity, catalog, search, pricing/tax, inventory, cart, checkout, orders, invoices, returns, verification, quotes/B2B, promotions, reviews, support, CMS, notifications, shipping, payments — is present in the diagram.
4. Every entity that can be owned by a future vendor has a `vendor_id UUID NULL FK` shown.

---

## Phase C — Seven State Machine Documents

**Deliverables**:
- `docs/architecture/state-machines/verification.md`
- `docs/architecture/state-machines/cart.md`
- `docs/architecture/state-machines/payment.md`
- `docs/architecture/state-machines/order.md`
- `docs/architecture/state-machines/shipment.md`
- `docs/architecture/state-machines/return.md`
- `docs/architecture/state-machines/quote.md`

Each file: version header, Mermaid `stateDiagram-v2` block, Markdown table (From, To, Trigger, Authorized Actor, Failure Behavior, Timeout Behavior).

**Verify**:
1. `mmdc` parses all 7 files without error.
2. Each file's table covers all minimum states from data-model.md.
3. Every transition row has a non-empty Failure Behavior.
4. Every time-bounded state row has a non-empty Timeout Behavior.

---

## Phase D — Permissions Matrix

**Deliverables**:
- `docs/architecture/permissions-matrix.md`: version header, one section per Phase-1 domain, each with a Markdown table of 10 role columns and domain-specific resource rows.

**Verify**:
1. Every domain has a section heading.
2. Every table has exactly 10 role columns: G, C, P, BB, BA, BrA, CO, AR, AW, AS.
3. Every cell contains only ✅, ❌, or ⚠️ `[condition]`.
4. Every ⚠️ condition has a footnote resolving it to a one-sentence rule.
5. Pick any 10 (role, resource, action) triples from Phase-1 domain specs — every answer is present in the matrix without ambiguity.

---

## Phase E — Testing Strategy

**Deliverables**:
- `docs/architecture/testing-strategy.md`: version header, per-spec-category sections, required layers, four universal mandatory scenario types.

**Verify**:
1. Sections exist for: backend domain, Flutter customer-app, Next.js admin, integration adapter, shared-contract.
2. Each section lists required test layers.
3. The four universal mandatory scenario types are present verbatim.
4. No numeric coverage percentages appear.

---

## Phase F — CI Mermaid Validation

**Deliverables**:
- `validate-diagrams` job added to `.github/workflows/build-and-test.yml`. Runs `mmdc` on every `.md` file touched in a PR.

**Verify**: Open a PR with a deliberately malformed Mermaid block (e.g., `stateDiagram-v2 [invalid`). Confirm `validate-diagrams` fails and blocks merge.

---

## Phase G — Architecture Artifact Index and ADR finalization

**Deliverables**:
- `docs/architecture/index.md` fully populated with links to: ERD (with version), all 7 state machines (each with version), permissions matrix (with version), testing strategy (with version).
- `docs/implementation-plan.md` §7 ADR section: ADRs 001–006 and 010 all show `Accepted` with Decision lines. ADRs 007, 008, 009 show `Proposed — deferred to Stage 7`.

**Verify**:
1. Open `docs/architecture/index.md` — all 11 artifacts linked with current version shown.
2. A new contributor finds all 11 artifacts within 90 seconds from the index.
3. ADR section has no `Proposed` entry outside the Stage-7 deferral list.

---

## Acceptance gate (DoD universal core applied)

| UC item | Verification |
|---|---|
| UC-1: acceptance scenarios pass | Phases A–G verification steps above |
| UC-2: lint-format green | No code in this spec; passes trivially |
| UC-3: contract-diff green | No backend interface changed |
| UC-4: context fingerprint matches | Embedded in PR description |
| UC-5: no constitution edits outside amendment | This spec does not amend the constitution |
| UC-6: required approvals | 1 code-owner (standard PR) |
| UC-7: commits signed | Enforced by branch protection |
| UC-8: constitution version recorded in spec | `v1.0.0` in spec.md header |

**Active applicability tags**: none (no state machine implementation, no audit events, no storage, no PDF, no user-facing strings — this spec *authors* the state machine documents but does not implement the state machine runtime).
