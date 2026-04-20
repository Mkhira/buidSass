# Branch Protection Configuration (`main`)

This document records the exact GitHub settings required for `main` per spec 001.

## Required Status Checks

- `lint-format`
- `contract-diff`
- `verify-context-fingerprint`
- `build`

`preview-deploy` is intentionally **not required** until `apps/admin_web` is scaffolded and `AZURE_STATIC_WEB_APPS_API_TOKEN` is populated. Re-add it to the required list when both conditions are met (spec 004 / Phase 1C kickoff).

## Pull Request Review Rules

- Require pull request reviews before merging: enabled
- Required approving reviews: `1` (path-specific elevation handled by CODEOWNERS)
- Dismiss stale pull request approvals when new commits are pushed: enabled
- Require review from Code Owners: enabled
- Require approval of the most recent reviewable push: enabled

## Commit and Merge Policy

- Require signed commits: enabled
- Allow squash merging: enabled
- Allow merge commits: disabled
- Allow rebase merging: disabled
- Require branches to be up to date before merging: enabled

## Branch Safety

- Allow force pushes: disabled
- Allow deletions: disabled
- Lock branch: disabled

## CODEOWNERS Approval Model

- `.specify/memory/constitution.md`: two human code-owners
- `docs/implementation-plan.md`: two human code-owners
- `.github/CODEOWNERS`: two human code-owners
- `docs/dod.md`: one human code-owner
- `*`: one human code-owner

## Preview Deploy Requirements

`preview-deploy` in `.github/workflows/build-and-test.yml` expects an Azure Static Web Apps environment for `apps/admin_web`.

Required repository secret:

- `AZURE_STATIC_WEB_APPS_API_TOKEN`: deployment token from Azure Static Web Apps

Preview behavior:

- On PR open/sync/reopen: deploy with `action: upload`
- On PR close: tear down with `action: close`
- Post preview URL as a PR comment

**Deferred prerequisite**: the Azure SWA resource and `AZURE_STATIC_WEB_APPS_API_TOKEN` secret may be deferred until `apps/admin_web` has real code. The `preview-deploy` job already reports success as a no-op when `apps/admin_web` is unchanged, so branch protection can be enabled against this check name today.

## Applying Protection

Run `scripts/apply-branch-protection.sh` after authenticating `gh` (`gh auth login`). The script is idempotent and applies every setting above via the GitHub REST API.

**Rollout order** (important):

1. Open the first PR on this branch with all CI wiring in place.
2. Let every required check run green once on a real PR.
3. Merge.
4. Then run `scripts/apply-branch-protection.sh` so protection locks against known-good check names.

Enabling protection before a clean CI baseline will block the very PR that introduces CI.
