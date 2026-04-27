# Quickstart: Admin Inventory

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

This module mounts inside spec 015's admin shell. Inventory routes appear at `/admin/inventory/*` once the shell is running.

## Prerequisites

Same as spec 015's `quickstart.md` — Node 20, pnpm 9, Docker Desktop, Playwright browsers. Plus:

- A staging-shaped inventory dataset (`dotnet run --project services/backend_api -- seed --mode=inventory-bulk`) so the low-stock queue / expiry calendar / reservations table render with realistic content.
- For barcode-scan testing on the adjustment form: a Chromium-based browser (Chrome / Edge) with a working camera, OR a USB barcode scanner that types into the focused input.

## Local dev

```bash
# Bring up backend + admin shell (spec 015)
cd <repo-root> && docker compose --profile admin up -d
cd apps/admin_web && pnpm dev

# Inventory routes:
# /inventory                       — overview
# /inventory/stock                 — stock-by-SKU list
# /inventory/adjust                — adjustment form
# /inventory/low-stock             — queue + threshold editor
# /inventory/batches               — batch list / new / detail
# /inventory/expiry                — tri-lane calendar
# /inventory/reservations          — reservation table + manual release
# /inventory/ledger                — append-only movements + export
```

## Tests

```bash
# Inventory-scoped unit + component
pnpm test -- inventory

# Visual regression (inventory stories only)
pnpm test:visual -- --grep inventory

# A11y
pnpm test:a11y -- --grep inventory

# E2E (Story 1 positive + negative + below-zero adjustment)
pnpm test:e2e -- e2e/inventory/story1_adjust.spec.ts

# E2E (Story 4 reservation release)
pnpm test:e2e -- e2e/inventory/story4_reservation_release.spec.ts
```

## Story-level smoke acceptance

Before opening a PR:

1. **Story 1 (P1)**: positive adjustment (e.g., +5 supplier_receipt) on a non-batched SKU — confirm ledger row + audit entry + recomputed available-to-sell. Negative adjustment (-1 breakage) requiring a mandatory note. Below-zero attempt as a non-`writeoff_below_zero` admin — blocked with localized error.
2. **Story 2 (P2)**: open low-stock; confirm severity sort; edit a threshold inline; confirm row drops out / stays based on the new threshold.
3. **Story 3 (P3)**: create a batch with a near-future `expiresOn`; confirm the batch shows in the **Near expiry** lane. Try to delete a batch with non-zero on-hand; confirm the write-off-first guard.
4. **Story 4 (P4)**: open reservations; release a stale reservation; confirm audit entry + recomputed available-to-sell + that the cart owner sees drift on next interaction (cross-app verification with spec 014).

## CI

Inherits `apps/admin_web-ci.yml` from spec 015 — no new workflow.

## Known limitations

- **Barcode scan**: Chromium-based browsers only via `BarcodeDetector`. Firefox / Safari falls back to manual SKU entry.
- **Calendar Hijri date display**: deferred — v1 uses Gregorian formatting; spec 012 owns Hijri tax-invoice rendering.
- **Multi-warehouse transfer wizard**: out of scope; admins do paired `transfer_out` / `transfer_in` adjustments.
- **Cycle-count waves**: out of scope; admins do per-SKU `cycle_count_correction` adjustments.
