# Quickstart: Admin Orders

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

This module mounts inside spec 015's admin shell. Orders routes appear at `/admin/orders/*` once the shell is running.

## Prerequisites

Same as spec 015's `quickstart.md`. Plus:

- A staging-shaped orders dataset spanning all four state combinations (`dotnet run --project services/backend_api -- seed --mode=orders-bulk`) so the list, detail, and timeline render with realistic content.
- An admin account with MFA enrolled — otherwise step-up flows can't be exercised.
- A test-mode payment gateway return that captures funds (so refunds have something to refund against).

## Local dev

```bash
# Bring up backend + admin shell (spec 015)
cd <repo-root> && docker compose --profile admin up -d
cd apps/admin_web && pnpm dev

# Orders routes:
# /orders                              — list
# /orders/[orderId]                    — detail
# /orders/[orderId]/refund             — refund flow (intercepting)
# /orders/[orderId]/invoice            — invoice section drilldown
# /orders/exports                      — exports list
# /orders/exports/[jobId]              — export job detail
```

## Feature flags

```bash
NEXT_PUBLIC_FLAG_ADMIN_CUSTOMERS=0     # 1 once spec 019 ships
NEXT_PUBLIC_FLAG_ADMIN_QUOTES=0        # 1 once spec 021 ships
NEXT_PUBLIC_FLAG_FINANCE_EXPORT=1      # 0 if spec 011 export schema is missing
NEXT_PUBLIC_REFUND_STEP_UP_MINOR_THRESHOLD_KSA=10000
NEXT_PUBLIC_REFUND_STEP_UP_MINOR_THRESHOLD_EG=50000
```

## Tests

```bash
# Orders-scoped unit + component
pnpm test -- orders

# Visual regression (orders stories only)
pnpm test:visual -- --grep orders

# A11y
pnpm test:a11y -- --grep orders

# No-403-after-render contract test (SC-004 enforcement)
pnpm test -- orders.no-403-after-render

# E2E
pnpm test:e2e -- e2e/orders/story1_list_detail_transitions.spec.ts
pnpm test:e2e -- e2e/orders/story2_refund.spec.ts
pnpm test:e2e -- e2e/orders/story3_invoice.spec.ts
pnpm test:e2e -- e2e/orders/story4_export.spec.ts
```

## Story-level smoke acceptance

Before opening a PR:

1. **Story 1 (P1)**: list → filter → open detail → walk a happy-path fulfillment progression (placed → packed → handed-to-carrier → delivered). Verify each step audit-emits and the timeline grows. Verify illegal transitions are hidden, not disabled.
2. **Story 2 (P2)**: open a captured order → refund flow → partial refund below threshold → submit (no step-up). Then full-amount refund → step-up dialog → MFA → submit. Verify refund state advances and audit entry shows step-up assertion id.
3. **Story 3 (P3)**: download invoice. For an order with a failed render, click regenerate (with `orders.invoice.regenerate`). Verify the section transitions Pending → Available.
4. **Story 4 (P4)**: filter list to last quarter / market = KSA / payment captured → click Export → confirm `OrdersExportJob` is created → wait for job → download CSV → confirm contents match the snapshot's filters.

## CI

Inherits `apps/admin_web-ci.yml` from spec 015 — no new workflow.

## Known limitations

- **Customer chip / Source-quote chip placeholder**: until specs 019 / 021 ship, both chips open a "coming soon" dialog with copy-to-clipboard. Flip the corresponding feature flags to switch to real navigation.
- **Multi-select / bulk transitions**: not in v1. Hidden entirely (no checkbox column).
- **Older invoice versions**: not exposed in v1. Latest version only.
- **Refund step-up TTL**: spec 004's step-up assertion has a 5-minute default TTL. Refund retries within the TTL reuse the same assertion; expired assertions re-prompt.
