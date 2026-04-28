# Quickstart: Admin Customers

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

This module mounts inside spec 015's admin shell. Customers routes appear at `/admin/customers/*` once the shell is running.

## Prerequisites

Same as spec 015's `quickstart.md`. Plus:

- A staging-shaped customers dataset spanning B2C + B2B + suspended + closed accounts (`dotnet run --project services/backend_api -- seed --mode=customers-bulk`).
- An admin account with MFA enrolled — required for all account actions (suspend / unlock / password-reset).
- A test account whose email + phone are known (so PII redaction can be visually verified).

## Local dev

```bash
# Bring up backend + admin shell (spec 015)
cd <repo-root> && docker compose --profile admin up -d
cd apps/admin_web && pnpm dev

# Customers routes:
# /customers                              — list
# /customers/[customerId]                 — profile detail
# /customers/[customerId]/addresses       — address book expanded
# /customers/[customerId]/company         — B2B drill (B2B + permission only)
```

## Feature flags

```bash
NEXT_PUBLIC_FLAG_ADMIN_VERIFICATIONS=0     # 1 once spec 020 ships
NEXT_PUBLIC_FLAG_ADMIN_QUOTES=0            # 1 once spec 021 ships
NEXT_PUBLIC_FLAG_ADMIN_SUPPORT=0           # 1 once spec 023 ships
NEXT_PUBLIC_FLAG_ADMIN_ORDERS=1            # already on if spec 018 shipped
```

## Tests

```bash
# Customers-scoped unit + component
pnpm test -- customers

# PII-leak sweep (critical)
pnpm test -- customers/pii-leak

# Visual regression
pnpm test:visual -- --grep customers

# A11y
pnpm test:a11y -- --grep customers

# No-403-after-render contract test
pnpm test -- customers.no-403-after-render

# E2E
pnpm test:e2e -- e2e/customers/story1_find_open_profile.spec.ts
pnpm test:e2e -- e2e/customers/story2_account_actions.spec.ts
pnpm test:e2e -- e2e/customers/story3_b2b_hierarchy.spec.ts
```

## Story-level smoke acceptance

Before opening a PR:

1. **Story 1 (P1)**: list → filter by market + B2B + verification state → free-text search → open a profile. Verify identity card / role chips / address book preview / orders summary chip resolve. Verify PII redaction by signing in as an admin without `customers.pii.read` and confirming masked values everywhere.
2. **Story 2 (P2)**: open a B2C profile → suspend (with reason note ≥ 10 chars + step-up) → confirm: (a) audit entry in spec 015's reader carries the assertion id, (b) the customer's customer-app sign-in fails with a generic auth-failure (cross-app verification), (c) the customer's existing in-flight orders proceed unaffected. Then unlock; password-reset trigger.
3. **Story 3 (P3)**: open a B2B `customer.company_owner` profile → confirm Company card renders parent + branches → click a branch → confirm it routes to that branch's profile. Then sign in as an admin without `customers.b2b.read` → confirm the Company card is hidden.
4. **Story 4 (P4)**: confirm each history panel (verification / quote / support) renders the placeholder until its flag flips. Flip a flag in `.env.local`, restart dev, confirm the panel switches to the populated render.

## CI

Inherits `apps/admin_web-ci.yml` from spec 015 — no new workflow.

## Known limitations

- **Address-book editing**: read-only in v1 (FR-019). The customer owns their address book through spec 014.
- **Customer impersonation**: explicitly out of scope; no placeholder, no menu entry.
- **History panels**: placeholder until specs 020 / 021 / 023 ship. Flag flips are deployment-config changes, not code changes.
- **Suspended-customer reason display**: surfaces only when spec 004 publishes the lockout-state record's reason field. Until then, support agents see the reason in the audit-log reader (spec 015).
