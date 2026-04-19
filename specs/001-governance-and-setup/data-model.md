# Data Model: Governance and Setup

**Branch**: `001-governance-and-setup` | **Date**: 2026-04-19

This spec produces no database entities. Its "model" is the set of governance artifacts — versioned files and their schemas — that every later spec depends on.

---

## Artifact: Constitution file

| Attribute | Value |
|---|---|
| Path | `.specify/memory/constitution.md` |
| Version | Semantic version in file header (current: 1.0.0) |
| Protected by | CODEOWNERS — two human code-owner approvals required |
| Mutability | Append/amend only via Principle-32 process |
| Consumers | `CLAUDE.md`, `.codex/system.md`, `GLM_CONTEXT.md`, fingerprint script |

---

## Artifact: ADR block

| Attribute | Value |
|---|---|
| Path | `docs/implementation-plan.md` §7 |
| Entries | ADR-001 through ADR-010 (7 Accepted, 3 Proposed) |
| Protected by | CODEOWNERS — two human code-owner approvals required |
| Mutability | Append-only after Accepted; supersede via new ADR |
| Consumers | `CLAUDE.md`, `.codex/system.md`, `GLM_CONTEXT.md`, fingerprint script |

---

## Artifact: Context fingerprint

| Attribute | Value |
|---|---|
| Algorithm | SHA-256 |
| Input | Byte content of constitution file + ADR block (in order) |
| Produced by | `scripts/gen-agent-context.sh` |
| Embedded in | PR description as `<!-- context-fingerprint: <hex> -->` |
| Verified by | GitHub Actions job `verify-context-fingerprint` |
| Staleness | Invalidated by any amendment to constitution or ADR block |

---

## Artifact: Definition of Done

| Attribute | Value |
|---|---|
| Path | `docs/dod.md` |
| Version | Semantic version in file header |
| Structure | `## Universal Core` + `## Applicability-Tagged Items` (trigger headers) |
| Consumed by | PR template, spec reviewers, CI (for structured checks where automatable) |

### Universal Core items (minimum)

| ID | Item | Automatable |
|---|---|---|
| UC-1 | Acceptance scenarios all pass | Partial (test suite) |
| UC-2 | Lint-and-format job green | Yes (CI) |
| UC-3 | Contract-diff job green | Yes (CI) |
| UC-4 | Constitution + ADR context fingerprint matches | Yes (CI) |
| UC-5 | No constitution/ADR edits outside a ratified amendment | Yes (CODEOWNERS gate) |
| UC-6 | Required human code-owner approvals present | Yes (branch protection) |
| UC-7 | All commits on `main` signed by a human | Yes (branch protection) |
| UC-8 | Constitution version recorded in the spec | Manual review |

### Applicability-Tagged items (trigger → required check)

| Trigger | Required check |
|---|---|
| `state-machine` | States, transitions, actors, triggers, failure behavior documented |
| `audit-event` | Event emitted with actor, timestamp, resource, before, after, reason |
| `storage` | Residency routing per ADR-010 verified |
| `pdf` | Arabic + English layouts rendered and visually regression-tested |
| `user-facing-strings` | Arabic editorial review recorded before launch-readiness |

---

## Artifact: Pull-request template

| Attribute | Value |
|---|---|
| Path | `.github/pull_request_template.md` |
| Structure | Spec/branch link, Universal Core checklist, active-tag declaration, summary field |
| Enforced by | GitHub — presented automatically on PR creation |

---

## Artifact: CODEOWNERS

| Attribute | Value |
|---|---|
| Path | `.github/CODEOWNERS` |
| Rules | Constitution file → 2 named code-owners; ADR block → 2 named code-owners; `main` catchall → 1 code-owner |
| Format | GitHub CODEOWNERS syntax |
| Mutability | Changes follow the same 2-approver rule as the constitution |

---

## Artifact: Agent context files

| File | Agent | Generator |
|---|---|---|
| `CLAUDE.md` | Claude | `scripts/gen-agent-context.sh claude` |
| `.codex/system.md` | Codex | `scripts/gen-agent-context.sh codex` |
| `GLM_CONTEXT.md` | GLM | `scripts/gen-agent-context.sh glm` |

All three share the same content schema: Principles 1–32 verbatim, ADR Decisions table, four-guardrail summary, current fingerprint hash, and DoD reference.

---

## Artifact: Repository layout

Root folders (ADR-001) — no schema, existence is the contract:

```
apps/customer_flutter/
apps/admin_web/
services/backend_api/
packages/shared_contracts/
packages/design_system/
infra/
scripts/
```

Any pull request introducing a new top-level folder MUST include an ADR-001 amendment with two-approver approval.
