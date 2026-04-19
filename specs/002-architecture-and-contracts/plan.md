# Implementation Plan: Architecture and Contracts

**Branch**: `002-architecture-and-contracts` | **Date**: 2026-04-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/002-architecture-and-contracts/spec.md`
**Depends on**: Spec 001 at DoD

## Summary

Author and ratify the four foundational architecture artifacts — ERD, seven state machines, permissions matrix, testing strategy — that every subsequent Phase-1 domain spec depends on. Add Mermaid diagram CI validation. Finalize all ADRs to Accepted or explicit Stage-7 deferral. Produce a single index linking all artifacts so any contributor can navigate to any document in under 90 seconds.

## Technical Context

**Language/Version**: Markdown, Mermaid (erDiagram + stateDiagram-v2), DBML
**Primary Dependencies**: Mermaid CLI `mmdc` (npm), oasdiff (already in CI from spec 001)
**Storage**: N/A — document artifacts only; no database
**Testing**: Mermaid CLI parse validation in CI; manual review via PR for content correctness
**Target Platform**: GitHub (native Mermaid rendering), VS Code (Mermaid preview extension)
**Project Type**: Architecture documentation
**Performance Goals**: ERD amendment cycle must not block active domain specs for more than 24 hours; `validate-diagrams` CI job must complete in under 2 minutes
**Constraints**: All diagrams must render on GitHub without external tools; no server-side renderers
**Scale/Scope**: 19 Phase-1 domains; 10 roles; 7 state machines; ~29 downstream spec consumers

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| P6 (multi-vendor ready) | ERD includes nullable `vendor_id` FK on every ownable entity | ✅ Pass |
| P9 (B2B first-class) | Permissions matrix includes B2B buyer, approver, branch admin, company owner | ✅ Pass |
| P17 (four orthogonal order statuses) | Order state machine uses four independent status streams; merging into one is non-compliant | ✅ Pass |
| P24 (state machines) | All 7 Principle-24 domains have explicit state machine documents | ✅ Pass |
| P25 (audit) | Permissions matrix includes audit-log-entry as a non-ownable entity; audit actions included | ✅ Pass |
| P28 (AI-build standard) | Architecture documents are explicit, structured, low-ambiguity by design | ✅ Pass |
| P29 (spec output standard) | All 12 sections present in spec.md | ✅ Pass |
| P30 (phasing) | Assigned Phase 1A; no Phase-1.5 or Phase-2 items | ✅ Pass |
| P31 (constitution supremacy) | No conflicts detected | ✅ Pass |

**Post-Phase-1 re-check**: No violations introduced by the design. Mermaid-only toolchain is consistent with P22 (no new locked tech — Mermaid is a documentation tool, not a product tech).

## Project Structure

### Documentation (this feature)

```text
specs/002-architecture-and-contracts/
├── plan.md                                    # This file
├── research.md                                # Phase 0 — all unknowns resolved
├── data-model.md                              # Phase 1 — artifact schemas
├── quickstart.md                              # Phase 1 — implementation guide
├── contracts/
│   └── architecture-document-contract.md     # Phase 1 — artifact interface contract
├── checklists/
│   └── requirements.md                        # Quality checklist (all items pass)
└── tasks.md                                   # Phase 2 — created by /speckit-tasks
```

### Source Code (repository root)

```text
docs/
└── architecture/
    ├── index.md                               # Artifact index (link + version for all 11)
    ├── erd.md                                 # Mermaid erDiagram — all 19 Phase-1 domains
    ├── erd.dbml                               # DBML supplement for tooling
    ├── permissions-matrix.md                  # Domain-grouped tables; 10 roles; ✅❌⚠️ cells
    ├── testing-strategy.md                    # Per-category layers + mandatory scenario types
    └── state-machines/
        ├── verification.md                    # stateDiagram-v2 + table
        ├── cart.md
        ├── payment.md
        ├── order.md
        ├── shipment.md
        ├── return.md
        └── quote.md

.github/
└── workflows/
    └── build-and-test.yml                     # + validate-diagrams job (mmdc)
```

**Structure Decision**: All deliverables are documentation files under `docs/architecture/`. The only source-code change is adding the `validate-diagrams` job to the existing CI workflow.

## Implementation Phases

### Phase A — Skeleton
Create `docs/architecture/` directory structure and all stub files.

### Phase B — ERD
Author full `erd.md` (Mermaid `erDiagram`) and `erd.dbml` covering all 19 Phase-1 domains with nullable `vendor_id` on ownable entities.

### Phase C — Seven State Machines
Author all 7 state-machine documents in Mermaid `stateDiagram-v2` + Markdown table format. All minimum states per domain per data-model.md must be present.

### Phase D — Permissions Matrix
Author `permissions-matrix.md` with domain-grouped tables, 10 role columns, ✅/❌/⚠️ cells, and footnotes for every conditional.

### Phase E — Testing Strategy
Author `testing-strategy.md` with per-spec-category sections, required layers, and four universal mandatory scenario types.

### Phase F — CI Mermaid Validation
Add `validate-diagrams` job to `.github/workflows/build-and-test.yml`.

### Phase G — Index and ADR Finalization
Complete `docs/architecture/index.md`. Verify ADR section in `docs/implementation-plan.md` is fully up to date.

## Complexity Tracking

No constitution violations detected. Table omitted.
