# Contributing Guide

This repository follows the governance rules defined in spec `001-governance-and-setup`.

## Monorepo Layout

- `apps/customer_flutter`: Flutter mobile + web storefront
- `apps/admin_web`: Next.js admin dashboard
- `services/backend_api`: .NET backend API
- `packages/shared_contracts`: generated shared API contracts
- `packages/design_system`: shared design tokens/components
- `infra`: infrastructure configuration and deployment assets
- `scripts`: repository automation and helper scripts

## Four Guardrails

1. `lint-format` must pass on every PR.
2. `contract-diff` must pass on every PR.
3. PR description must include a valid context fingerprint.
4. Constitution/ADR edits require protected human CODEOWNERS approvals.

## Commit Signing Setup (GPG)

1. Generate key:
   - `gpg --full-generate-key`
2. List key IDs:
   - `gpg --list-secret-keys --keyid-format=long`
3. Configure git:
   - `git config --global user.signingkey <KEY_ID>`
   - `git config --global commit.gpgsign true`
4. Export and upload public key to GitHub:
   - `gpg --armor --export <KEY_ID>`
   - Add output at GitHub: Settings → SSH and GPG keys → New GPG key.

## Merge Policy

- `main` is squash-merge only.
- Merge commits and rebase merges are disabled.
- Force-push and branch deletion are disabled for `main`.

## Definition of Done

- Source of truth: `docs/dod.md`
- Every PR should satisfy Universal Core items and any active applicability tags.

## Agent Context Injection and Fingerprint

1. Ensure context files are generated:
   - `scripts/gen-agent-context.sh`
2. Compute canonical fingerprint:
   - `scripts/compute-fingerprint.sh`
3. Add fingerprint in PR description:
   - `<!-- context-fingerprint: <hex> -->`
