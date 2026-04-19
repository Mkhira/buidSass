# Implementation Plan: Governance and Setup

**Branch**: `001-governance-and-setup` | **Date**: 2026-04-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/001-governance-and-setup/spec.md`

## Summary

Establish the foundational governance layer that every subsequent Phase-1 spec inherits: monorepo layout (ADR-001), four CI guardrails, branch protection rules, CODEOWNERS with tiered approver counts, agent-context injection pattern, layered Definition of Done, and a squash-merge policy that keeps AI-authored commits off `main`. No domain features are built here — this spec makes the build safe for everything that follows.

## Technical Context

**Language/Version**: YAML (GitHub Actions workflows), Bash (utility scripts), Markdown (governance docs)
**Primary Dependencies**: GitHub Actions, GPG (commit signing), oasdiff (contract diff), dotnet format, dart format, eslint, prettier
**Storage**: N/A — no database; governance artifacts are files in the repository
**Testing**: Shell script unit tests (bats or shunit2), GitHub Actions dry-run on PRs
**Target Platform**: GitHub (hosted runners, branch protection, CODEOWNERS, PR templates)
**Project Type**: DevOps / governance configuration
**Performance Goals**: CI pipeline completes in under 10 minutes per PR on a cold runner
**Constraints**: Must run on GitHub-hosted runners (no self-hosted required for this spec); no secrets beyond GPG keys and GitHub tokens
**Scale/Scope**: ~200 PRs expected across Phase 1; ~29 specs; 3 AI agents; 1 human reviewer cadence

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| P22 (locked tech) | No new tech beyond what ADRs ratify; CI tooling is infrastructure not product tech | ✅ Pass |
| P23 (modular monolith) | This spec sets up the repo, not the product architecture | ✅ Pass |
| P25 (audit) | CODEOWNERS + signed commits + PR records provide an audit trail for every governance action | ✅ Pass |
| P28 (AI-build standard) | This spec is itself the AI guardrail setup — no conflict | ✅ Pass |
| P29 (spec output standard) | All 12 required sections present in spec.md | ✅ Pass |
| P30 (phasing) | Assigned Phase 1A; no Phase-1.5 or Phase-2 items included | ✅ Pass |
| P31 (constitution supremacy) | No conflicts detected | ✅ Pass |
| P32 (amendment procedure) | CODEOWNERS enforces the two-approver gate required by P32 | ✅ Pass |

**Post-Phase-1 re-check**: No new violations introduced. Contract (ci-pipeline-contract.md) is consistent with P25 audit and P28 AI-build requirements.

## Project Structure

### Documentation (this feature)

```text
specs/001-governance-and-setup/
├── plan.md                          # This file
├── research.md                      # Phase 0 — all unknowns resolved
├── data-model.md                    # Phase 1 — governance artifact schemas
├── quickstart.md                    # Phase 1 — implementation guide
├── contracts/
│   └── ci-pipeline-contract.md      # Phase 1 — CI interface contract
├── checklists/
│   └── requirements.md              # Quality checklist (all items pass)
└── tasks.md                         # Phase 2 — created by /speckit-tasks
```

### Source Code (repository root)

```text
.github/
├── CODEOWNERS                        # Path-based approver rules (tiered)
├── pull_request_template.md          # UC checklist + active-tag declaration
└── workflows/
    ├── lint-format.yml               # Guardrail #1
    ├── contract-diff.yml             # Guardrail #2
    ├── verify-context-fingerprint.yml # Guardrail #3 (automated verification)
    └── build-and-test.yml            # Standard build gate

apps/
├── customer_flutter/                 # ADR-001 skeleton
└── admin_web/                        # ADR-001 skeleton

services/
└── backend_api/                      # ADR-001 skeleton

packages/
├── shared_contracts/                 # ADR-001 skeleton
└── design_system/                    # ADR-001 skeleton

infra/                                # ADR-001 skeleton
scripts/
├── gen-agent-context.sh              # Generates CLAUDE.md, .codex/system.md, GLM_CONTEXT.md
├── extract-adr-block.sh              # Extracts §7 ADR block for fingerprinting
└── compute-fingerprint.sh            # SHA-256 of constitution + ADR block

CLAUDE.md                             # Claude session context (generated)
.codex/
└── system.md                         # Codex session context (generated)
GLM_CONTEXT.md                        # GLM session context (generated)
.editorconfig                         # Repo-wide formatting baseline
docs/
└── dod.md                            # Layered Definition of Done (Universal Core + Tagged)
```

**Structure Decision**: Single-repo configuration layout. All deliverables are configuration files, scripts, and governance documents — no application source code is written in this spec.

## Implementation Phases

### Phase A — Repository skeleton
Scaffold ADR-001 folder layout. Add `.editorconfig`. Write `scripts/gen-agent-context.sh` and generate initial `CLAUDE.md`, `.codex/system.md`, `GLM_CONTEXT.md`.

### Phase B — Definition of Done
Author `docs/dod.md` with layered structure (Universal Core 8 items + 5 Applicability-Tagged triggers per data-model.md).

### Phase C — CI pipeline
Write four GitHub Actions workflows: `lint-format`, `contract-diff`, `verify-context-fingerprint`, `build-and-test`. Add utility scripts: `extract-adr-block.sh`, `compute-fingerprint.sh`.

### Phase D — Branch protection and CODEOWNERS
Configure `.github/CODEOWNERS` with tiered rules. Enable branch protection on `main` per ci-pipeline-contract.md: signed commits, squash-only, required checks, 1-reviewer catchall, 2-reviewer override for constitution/ADR paths.

### Phase E — PR template and final verification
Author `.github/pull_request_template.md`. Run the quickstart.md smoke tests. Verify all Universal Core DoD items pass for this spec itself.

## Complexity Tracking

No constitution violations detected. Table omitted per template instructions.
