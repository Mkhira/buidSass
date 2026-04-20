# CI Pipeline Contract: Governance and Setup

**Version**: 1.0 | **Date**: 2026-04-19

This document defines the interface contract that the CI pipeline exposes to all pull requests and to downstream specs. Every spec produced by the program MUST be able to satisfy this contract.

---

## Required status checks (must be green to merge)

| Job name | Trigger | Pass condition | Fail behavior |
|---|---|---|---|
| `lint-format` | Every PR | All formatters and linters exit 0 for changed packages | PR merge blocked |
| `contract-diff` | Every PR touching backend or `packages/shared_contracts` | oasdiff reports no unresolved diff between emitted OpenAPI and stored client | PR merge blocked |
| `verify-context-fingerprint` | Every PR | SHA-256 of current constitution + ADR block matches `<!-- context-fingerprint: ... -->` in PR description | PR merge blocked |
| `build` | Every PR | All packages build without error | PR merge blocked |
| `test` | Every PR | All test suites pass | PR merge blocked |

---

## Branch protection rules (enforced by platform)

| Rule | Value |
|---|---|
| Required reviews — ordinary PR | 1 human code-owner |
| Required reviews — constitution/ADR PR | 2 human code-owners |
| Dismiss stale reviews on new push | Yes |
| Require signed commits | Yes (GPG) |
| Allowed merge strategies | Squash only |
| Allow force push | No |
| Allow branch deletion | No (for `main`) |
| Require branches to be up to date | Yes |

---

## CODEOWNERS path rules

| Path pattern | Required code-owners |
|---|---|
| `.specify/memory/constitution.md` | 2 named human code-owners |
| `docs/implementation-plan.md` (ADR block §7) | 2 named human code-owners |
| `.github/CODEOWNERS` | 2 named human code-owners |
| `docs/dod.md` | 1 human code-owner |
| `*` (catchall) | 1 human code-owner |

---

## Context fingerprint protocol

```
Input:  cat .specify/memory/constitution.md docs/implementation-plan.md-adr-block | sha256sum
Output: hex string
Embed:  <!-- context-fingerprint: <hex> --> (in PR description, not committed to repo)
Verify: GitHub Actions job recomputes and diffs
```

The ADR block is extracted by `scripts/extract-adr-block.sh` which outputs §7 content only.

---

## Squash-merge commit format

Every merge commit to `main` MUST use this format:

```
<type>(<scope>): <short description>

<body — optional, wraps at 72 chars>

Spec: specs/<NNN>-<name>/spec.md
Constitution: v<semver>
Co-Authored-By: <human name> <email>
```

`type` values: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`.

---

## Artifacts produced by CI on merge to `main`

| Artifact | Produced by | Consumed by |
|---|---|---|
| `openapi.json` | Backend build | `contract-diff` job; `packages/shared_contracts` regeneration |
| `packages/shared_contracts` (regenerated client) | Contract regeneration job | Flutter app; Next.js admin |
| Context fingerprint (canonical) | `scripts/gen-agent-context.sh` | `verify-context-fingerprint` job on subsequent PRs |
