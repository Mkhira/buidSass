# Research: Architecture and Contracts

**Branch**: `002-architecture-and-contracts` | **Date**: 2026-04-19
**Phase**: 0 — unknowns resolved before design

---

## Decision 1 — ERD format

**Decision**: Mermaid `erDiagram` (primary, rendered in GitHub) + DBML source file (`docs/architecture/erd.dbml`) as a machine-readable supplement.

**Rationale**: Mermaid `erDiagram` renders natively on GitHub, is text-diffable line-by-line in pull requests, and pairs naturally with the chosen Mermaid `stateDiagram-v2` format — keeping the toolchain uniform (one Mermaid CLI validation job covers both). DBML is added as a supplementary format because it can generate SQL migration stubs and is consumed by dbdiagram.io for richer visualization without blocking the GitHub-native workflow.

**Alternatives considered**:
- dbdiagram.io DBML only: excellent tooling but does not render on GitHub — contributors need a separate tool to read the diagram.
- PlantUML entity diagrams: server-dependent; harder to integrate with CI.
- Plain Markdown tables: readable for small schemas, breaks down at 20+ tables.

---

## Decision 2 — State machine diagram format (confirmed from clarification)

**Decision**: Mermaid `stateDiagram-v2` for visual form + Markdown table for structured form. CI validates every `.md` file containing a ` ```mermaid ` block using `mmdc --input <file> --output /dev/null`.

**Rationale**: Native GitHub rendering, text-diffable, single toolchain with the ERD format. The Markdown table companion makes the state machine programmatically parseable (e.g., for future code-generation from spec) without requiring Mermaid parsing.

**Alternatives considered**: Confirmed in clarification session — no alternatives kept.

---

## Decision 3 — Permissions matrix format

**Decision**: Domain-grouped Markdown tables, one table per domain section, rows = roles, columns = actions. Each cell: `✅ allowed`, `❌ denied`, or `⚠️ [condition]` with the condition spelled out in a footnote.

**Rationale**: Permissions matrices in YAML are more structured but harder for humans to scan during PR review. A Markdown table is instantly readable by reviewers and renders correctly on GitHub. Domain grouping keeps any single table manageable (a single flat matrix of 10 roles × all resources × all actions across 20 domains would be unreadable). Emoji status codes allow rapid visual scanning.

**Roles in scope** (confirmed from clarification):
1. Guest
2. Customer
3. Professional (verified customer)
4. B2B Buyer
5. B2B Approver
6. B2B Branch Admin
7. B2B Company Owner
8. Admin (read-only)
9. Admin (write)
10. Admin (super)

**Alternatives considered**:
- Single flat YAML file: machine-readable but review-hostile.
- Per-role permission files: good for code generation, terrible for "does role X have access to resource Y" queries.

---

## Decision 4 — Testing strategy: qualitative posture format (confirmed from clarification)

**Decision**: Per-spec-category sections, each listing required test layers and mandatory scenario types expressed as prose rules ("every X must have a test"), not numeric thresholds.

**Spec categories in scope**:
| Category | Required layers |
|---|---|
| Backend domain spec | Unit (handler), Integration (DB + full request), Contract (OpenAPI diff) |
| Flutter customer-app spec | Widget test, Integration test (flutter_test), RTL golden test |
| Next.js admin spec | Jest unit, Playwright E2E (critical paths) |
| Integration adapter spec | Unit (mock adapter), Integration (provider sandbox), Contract (schema) |
| Shared-contract spec | Contract diff (oasdiff on every PR) |

**Mandatory scenario types** (all spec categories):
- Every state transition must have at least one test.
- Every error branch must have at least one test.
- Every permission boundary (allowed → denied role transition) must have at least one test.
- Every acceptance scenario in the spec must map to at least one test.

**Alternatives considered**: Numeric thresholds — rejected in clarification session.

---

## Decision 5 — Architecture artifact index

**Decision**: `docs/architecture/index.md` — a single Markdown document that links to every architecture artifact with current version, last-amended date, and a one-sentence description. It is the canonical entry point referenced from `CLAUDE.md`, `docs/dod.md`, and every domain spec's header.

**Rationale**: Without an index, contributors would need to know folder structure to find artifacts. An index reduces onboarding friction to a single file read and satisfies SC-006 (90-second discoverability target).

---

## Decision 6 — Mermaid CI validation approach

**Decision**: GitHub Actions job `validate-diagrams` runs `mmdc` (Mermaid CLI) on every pull request that touches a `.md` file. The job parses each ` ```mermaid ` block, renders to `/dev/null`, and fails on any parse error. Job added to the existing `build-and-test.yml` workflow.

**Rationale**: Keeps the diagram format honest without requiring a separate workflow file. `mmdc` is the official CLI, installable via npm, and available on GitHub-hosted runners.

---

## Resolved unknowns summary

| Unknown | Resolved |
|---|---|
| ERD format | Mermaid erDiagram + DBML supplement |
| State machine format | Mermaid stateDiagram-v2 + Markdown table (confirmed in clarification) |
| Permissions matrix format | Domain-grouped Markdown tables with emoji status codes |
| Testing strategy posture | Qualitative mandatory scenario types per spec category (confirmed in clarification) |
| B2B roles in scope | 4 B2B roles: buyer, approver, branch admin, company owner (confirmed) |
| ERD amendment gate | 1 code-owner + downstream-impact note (confirmed) |
| Architecture artifact index | docs/architecture/index.md |
| Mermaid CI validation | mmdc in build-and-test.yml |
