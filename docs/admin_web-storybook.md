# `apps/admin_web/` — Storybook publishing

Per spec 015 T098 — documentation of where the visual-regression
Storybook gets published, so downstream specs (016 / 017 / 018 / 019)
can link to it from PR descriptions.

## v1 publishing target — static host on staging

Storybook builds to `apps/admin_web/storybook-static/` via
`pnpm build-storybook`. The CI workflow `admin_web-storybook-nightly.yml`
(T032h) builds and runs Playwright snapshot tests against this output;
on `main` we additionally publish to a static host so reviewers can
browse the live primitives.

### Hosting

Two acceptable targets — pick one in Phase 1E E1 infrastructure:

1. **Azure Static Web Apps** (matches ADR-010 KSA residency for the
   admin app's runtime; Storybook is dev-tooling so residency is less
   strict, but co-locating reduces moving parts).
2. **GitHub Pages** for the repo, served at
   `https://<org>.github.io/<repo>/storybook/`. Simplest; no extra
   infra cost.

The CI workflow uploads `storybook-static/` as an artefact today so
the choice doesn't gate any 1C work — flipping the host is a one-PR
change to the publish step.

## What Storybook hosts

- Every shell primitive (`components/shell/*.stories.tsx`) — one story
  per state × locale × theme.
- Every shared composite (`components/data-table/*.stories.tsx`,
  `components/form-builder/*.stories.tsx`).
- Audit reader composites (`components/audit/*.stories.tsx`).
- Per-feature stories from 016 / 017 / 018 / 019 (each module appends
  to the same `apps/admin_web/components/<module>/*.stories.tsx`).

Per spec 015 FR-028h, the **PR-time** runs use `--grep <module>` to
keep wall-time under 10 min. The full suite runs nightly across
3 shards.

## Visual-regression snapshot policy

- Diff threshold: 0.2% per snapshot (FR-028h).
- Snapshots are committed under
  `apps/admin_web/tests/visual/__snapshots__/` (gitignored output:
  `apps/admin_web/test-results/` for failure diffs).
- Updating a snapshot intentionally:
  ```bash
  pnpm test:visual --update-snapshots
  ```
  Each update needs a reviewer comment confirming the visual change is
  intentional (e.g., "primary colour shade adjustment per design").

## Chromatic alternative (deferred)

Chromatic is a commercial-but-free-for-OSS visual-regression service
that hosts Storybook + diff UI. Adopting it would let designers
review snapshot diffs without engineering involvement. **Deferred to
Phase 2** — the in-repo Playwright snapshot path is sufficient for v1
launch and avoids a third-party dependency.
