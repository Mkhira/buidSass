# Quickstart: Admin Catalog

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

This module mounts inside spec 015's admin shell. There is no separate dev server; the catalog routes appear under `/admin/catalog/*` once the foundation is running.

## Prerequisites

Same as spec 015's `quickstart.md` — Node 20, pnpm 9, Docker Desktop, Playwright browsers. Plus:

- A staging-shaped catalog dataset (run `dotnet run --project services/backend_api -- seed --mode=catalog-bulk`) so the products list / categories tree have realistic content for the visual tests.

## Local dev

```bash
# Bring up the backend stack (spec 015 docs)
cd <repo-root> && docker compose --profile admin up -d

# Run admin web app (spec 015 prod-equivalent dev server)
cd apps/admin_web && pnpm dev

# Catalog routes are at:
# http://localhost:3001/catalog
# http://localhost:3001/catalog/products
# http://localhost:3001/catalog/products/new
# http://localhost:3001/catalog/categories
# http://localhost:3001/catalog/brands
# http://localhost:3001/catalog/manufacturers
# http://localhost:3001/catalog/bulk-import
```

## Tests

```bash
# Catalog-scoped unit + component
pnpm test -- catalog

# Visual regression (catalog stories only)
pnpm test:visual -- --grep catalog

# A11y
pnpm test:a11y -- --grep catalog

# E2E (Story 1 product create + publish)
pnpm test:e2e -- e2e/catalog/story1_product_create.spec.ts

# E2E (Story 4 bulk import dry-run + commit)
pnpm test:e2e -- e2e/catalog/story4_bulk_import.spec.ts
```

## Story-level smoke acceptance

Before opening a PR:

1. **Story 1 (P1)**: Create a new product → upload media → save → publish. Confirm it appears in the customer app's listing within a refresh.
2. **Story 2 (P2)**: Open categories → drag a sub-category to a new parent → confirm it persists. Deactivate a branch with active products → confirm the warning surfaces.
3. **Story 3 (P3)**: Brand CRUD — create / edit / deactivate. Confirm the deactivated brand stays rendered on existing products.
4. **Story 4 (P4)**: Export the catalog as CSV → modify a row → re-upload → review the validation report → commit. Confirm row-level audit entries surface in spec 015's audit-log reader.

## CI

Inherits `apps/admin_web-ci.yml` — no new workflow needed. The advisory `impeccable-scan` workflow already runs against this app's PRs.

## Known limitations

- **Schema bump banner**: when spec 005 bumps the bulk-import CSV schema, in-flight admin sessions see a banner directing them to re-export. Fix-forward only — no migration of partially-validated sessions.
- **Pricing + inventory**: read-only summaries only — full CRUD lives in later specs (007 admin / 017).
