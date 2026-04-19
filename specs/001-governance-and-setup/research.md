# Research: Governance and Setup

**Branch**: `001-governance-and-setup` | **Date**: 2026-04-19
**Phase**: 0 — unknowns resolved before design

---

## Decision 1 — Version-control platform

**Decision**: GitHub (GitHub Actions for CI, native CODEOWNERS, branch protection, required checks, PR templates).

**Rationale**: Standard for AI-agent-assisted development; GitHub Actions workflows are YAML-first, auditable, and well-supported by Claude/Codex/GLM tooling. CODEOWNERS is a first-class file that supports two-approver rules per path. Native branch protection supports squash-only merge strategy, signed-commit enforcement, required status checks, and reviewer count gates — all required by FR-002 through FR-017.

**Alternatives considered**:
- GitLab: equivalent feature set but tooling ecosystem for AI agents leans GitHub.
- Bitbucket: lacks native CODEOWNERS with per-path approver count.

---

## Decision 2 — Commit signing mechanism

**Decision**: GPG commit signing enforced via GitHub branch protection ("Require signed commits" toggle) plus GPG key published per human code-owner. Squash-only merge ensures AI-authored branch commits never reach `main` as authored commits (FR-016).

**Rationale**: GPG is the most widely supported signing mechanism on GitHub and is verifiable without additional tooling. SSH signing is an emerging alternative but GPG has broader CI verification support. Squash-only strategy collapses all branch history under the merger's identity, cleanly satisfying FR-015/FR-016.

**Alternatives considered**:
- SSH signing (newer, simpler key management): viable but less CI tooling support today.
- Gitsign (Sigstore): excellent for keyless, but adds external dependency.

---

## Decision 3 — Constitution + ADR context fingerprint mechanism

**Decision**: SHA-256 hash of the byte content of `.specify/memory/constitution.md` concatenated with the ADR block of `docs/implementation-plan.md`. Hash is computed by a script at session start and embedded in the PR description in a fenced block (`<!-- context-fingerprint: <hash> -->`). A GitHub Actions job recomputes the hash from current `main` HEAD and compares. Mismatch → job fails.

**Rationale**: SHA-256 is stable, cheap, and deterministic. Embedding in PR description avoids committing a transient file. The job is deterministic and reproducible — no shared secret needed. False positives only occur when the constitution or ADRs are legitimately amended (in which case the PR is an amendment PR itself, requiring the two-approver rule anyway).

**Alternatives considered**:
- Hash of entire implementation plan: too broad — changes to non-ADR sections would fail fingerprint incorrectly.
- Semantic hash (AST): overkill; byte hash suffices for immutable governance artifacts.

---

## Decision 4 — Lint and format toolchain (per ADRs)

| App / Package | Formatter | Linter | Config file |
|---|---|---|---|
| `services/backend_api` (.NET) | `dotnet format` | Roslyn analysers | `.editorconfig` |
| `apps/customer_flutter` | `dart format` | `dart analyze` | `analysis_options.yaml` |
| `apps/admin_web` (Next.js) | `prettier` | `eslint` | `.prettierrc`, `eslint.config.js` |
| `packages/shared_contracts` | `prettier` | `eslint` | shared config |
| `packages/design_system` | `prettier` | `eslint` | shared config |
| All files | `.editorconfig` | (enforced by formatters) | `.editorconfig` |

CI job `lint-format` runs all tools per changed package (path-filtered). Any non-zero exit → job fails → merge blocked (Guardrail #1).

---

## Decision 5 — Contract diff mechanism (Guardrail #2)

**Decision**: Backend emits an OpenAPI 3.1 spec as a CI artifact on every build. A diff step runs `oasdiff` (or equivalent) comparing the emitted spec against the last merged spec stored in `packages/shared_contracts/openapi.json`. Any breaking or additive change without a regenerated client → diff job fails.

**Rationale**: `oasdiff` understands semantic OpenAPI diff and can distinguish breaking vs. non-breaking changes. Non-breaking additions (new endpoints, new optional fields) can be flagged as warnings; breaking changes always fail. This gives nuance without removing the gate.

**Alternatives considered**:
- Spectral: excellent for linting spec style, not diffing against a previous version.
- Manual review only: too slow and too error-prone for an AI-agent-led build.

---

## Decision 6 — Layered DoD delivery format

**Decision**: The DoD lives in `docs/dod.md` as a Markdown file with two clearly delimited sections: `## Universal Core` and `## Applicability-Tagged Items`. Each tagged item carries a YAML-like trigger header, e.g., `### [trigger: state-machine]`. The PR template references `docs/dod.md` and includes a checkbox block listing the universal core items. Authors tick the applicable-tag checkboxes that are relevant to their PR.

**Rationale**: A single file is easy to version and reference. The trigger-header convention lets tooling (and humans) identify which tags apply without parsing prose. Universal core checkboxes in the PR template make the barrier to missing them very high.

---

## Decision 7 — Agent context injection format

**Decision**: Root `CLAUDE.md` (for Claude), `.codex/system.md` (for Codex), and a `GLM_CONTEXT.md` (for GLM) each contain:
1. A verbatim embed of constitution Principles 1–32.
2. The ADR Decisions table (ADR number, title, Decision line only — one row per ADR).
3. A short "How to work in this repo" guide covering the four guardrails and the DoD.

These files are generated by a script (`scripts/gen-agent-context.sh`) from the constitution and ADR source, so they stay in sync. The fingerprint hash is also produced by this script.

**Rationale**: Separate files per agent prevents one agent's context format from polluting another's. Generated (not hand-authored) context ensures the content tracks the source documents.

---

## Resolved unknowns summary

| Unknown | Resolved |
|---|---|
| VCS platform | GitHub |
| Commit signing | GPG via branch protection + squash-only merge |
| Fingerprint mechanism | SHA-256 of constitution + ADR block, embedded in PR description |
| Lint toolchain | dotnet format / dart format / prettier+eslint per package |
| Contract diff | oasdiff on OpenAPI 3.1 artifacts |
| DoD format | `docs/dod.md` with Universal Core + Applicability-Tagged sections |
| Agent context | Generated per-agent files from constitution + ADR source |
