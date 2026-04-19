# Repository Layout Reference

## ADR-001 Rationale

The project uses a single monorepo with explicit top-level boundaries to keep contracts, governance, and implementation synchronized while preserving clear ownership.

Canonical top-level folders:

- `apps/`
- `services/`
- `packages/`
- `infra/`
- `scripts/`

## Deliverable Placement

- Backend endpoints, handlers, and domain services: `services/backend_api/`
- Flutter customer screens and flows: `apps/customer_flutter/`
- Admin pages and operational tools: `apps/admin_web/`
- Shared API types/contracts: `packages/shared_contracts/`
- Shared design tokens and UI primitives: `packages/design_system/`
- Infrastructure definitions, environment wiring, deployment config: `infra/`
- Utility scripts for generation, verification, and project automation: `scripts/`

## Adding a New Package

1. Confirm package need and boundaries in the relevant feature spec.
2. Create directory under `packages/<new_package_name>/`.
3. Add minimal package metadata (`package.json`, language-specific manifest, or equivalent).
4. Document package purpose in this file and update `CONTRIBUTING.md` if contributor-facing.
5. Wire CI checks in `.github/workflows/` if build/test/lint behavior differs.
6. If adding a new top-level folder (not package), raise ADR amendment first.
