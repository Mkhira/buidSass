# Architecture Document Contract

**Version**: 1.0 | **Date**: 2026-04-19

This document defines the interface contract that every architecture artifact produced by spec 002 MUST satisfy so that downstream specs can consume them reliably.

---

## Contract: Entity-Relationship Model (`docs/architecture/erd.md`)

Every version of the ERD MUST:

| Requirement | Verifiable by |
|---|---|
| Begin with a version header: `**ERD Version**: X.Y.Z \| **Date**: YYYY-MM-DD` | Text grep |
| Contain a Mermaid `erDiagram` block covering all 19 Phase-1 domains | Mermaid CI parse + manual review |
| Include a DBML supplement file at `docs/architecture/erd.dbml` | File existence check |
| Express multi-vendor readiness via nullable `vendor_id` FK on ownable entities | Manual review |
| List every entity's ownership domain in a comment | Text grep |
| Be resolvable to a unique version at the time any domain spec references it | Version header check |

### ERD amendment PR template addition

Every ERD amendment PR description MUST include:

```markdown
## ERD Amendment
- **ERD version**: X.Y before → X.Y+1 after
- **Entities changed**: [list]
- **Downstream specs affected**: [list spec numbers]
- **Migration impact**: [schema-breaking / additive / no-op]
```

---

## Contract: State Machine Documents (`docs/architecture/state-machines/*.md`)

Every state machine document MUST:

| Requirement | Verifiable by |
|---|---|
| Begin with: `**Domain**: <name> \| **Version**: X.Y.Z \| **Date**: YYYY-MM-DD` | Text grep |
| Contain exactly one Mermaid `stateDiagram-v2` block | Mermaid CI parse |
| Contain a Markdown table with columns: From State, To State, Trigger, Authorized Actor, Failure Behavior, Timeout Behavior | Markdown table parse |
| Enumerate all states from the minimum required list (see data-model.md) | Manual review |
| Define failure behavior for every transition | Manual review |
| Define timeout behavior for every time-bounded state | Manual review |

---

## Contract: Permissions Matrix (`docs/architecture/permissions-matrix.md`)

Every version of the permissions matrix MUST:

| Requirement | Verifiable by |
|---|---|
| Begin with a version header | Text grep |
| Have one section per Phase-1 domain | Heading scan |
| Have one Markdown table per domain with exactly 10 role columns (G, C, P, BB, BA, BrA, CO, AR, AW, AS) | Column count check |
| Use only ✅, ❌, or ⚠️ `[condition]` in cells | Regex check |
| Include a footnotes section resolving every ⚠️ condition to a one-sentence rule | Manual review |

---

## Contract: Testing Strategy (`docs/architecture/testing-strategy.md`)

Every version of the testing strategy MUST:

| Requirement | Verifiable by |
|---|---|
| Begin with a version header | Text grep |
| Have a section for every spec category listed in research.md | Heading scan |
| State the required test layers per category | Manual review |
| State the four universal mandatory scenario types | Text grep for "every state transition", "every error branch", "every permission boundary", "every acceptance scenario" |
| Contain no numeric coverage thresholds | Negative grep for `%` in coverage context |

---

## Contract: Architecture Artifact Index (`docs/architecture/index.md`)

The index MUST:

| Requirement | Verifiable by |
|---|---|
| Link to: ERD, all 7 state machines, permissions matrix, testing strategy | Link check |
| Include current version + last-amended date next to each link | Text grep |
| Be updated atomically with every artifact amendment (same PR) | Manual review of amendment PRs |
