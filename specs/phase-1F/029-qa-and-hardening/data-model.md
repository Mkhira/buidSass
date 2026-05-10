# Data Model: 029 — QA & Launch Hardening

**Phase**: 1
**Date**: 2026-05-10

## No new schema

Spec 029 introduces **zero new database tables**, **zero migrations**, **zero new domain events**, and **zero new state machines**. It exercises the entities already defined by Phase 1A–1E specs.

The verification work writes to existing tables only — primarily `audit_log_entries` (per spec 003) when system-internal actions fire during chaos / regression / impeccable rehearsal runs.

## Existing entities exercised (read-mostly)

| Module | Tables touched | Mode |
|---|---|---|
| Identity (004) | `auth.tokens`, `mfa.tokens`, `otp_codes` | Auth fuzzing (US-4) — read-heavy + force-replay attempts. |
| Catalog (005) | `catalog.products`, `catalog.skus`, `catalog.categories` | Regression (US-1) + k6 catalog scenario (US-6) — read-only at scale. |
| Search (006) | (Meilisearch indices) | k6 search scenario (US-6) — read-only. |
| Cart-checkout (010) | `cart.carts`, `cart.cart_lines` | Regression (US-1) + k6 checkout scenario (US-6) — write-path on Staging only. |
| Order (011) | `orders.orders`, `orders.order_lines` | Regression + checkout k6 — Staging only. |
| Tax-invoice (012) | `tax.invoices`, `tax.invoice_lines` | Regression — verifies VAT calculation + PDF render. |
| Payments (027) | `payments.payments`, `payments.refunds`, `payments.webhooks_received`, `payments.idempotency_keys`, `payments.reconciliation_runs`, `payments.reconciliation_exceptions` | Chaos drill (US-5) — write-path on Staging via sandbox providers. |
| Notifications (025) | `notifications.delivery_log`, `notifications.dead_letter` | Chaos drill (US-5) — write-path on Staging. |
| Shipping (026) | `shipping.shipments`, `shipping.shipment_events`, `shipping.dead_letter` | Chaos drill (US-5) — write-path on Staging. |
| Audit (003) | `audit_log_entries` | Every chaos action + waiver approval + threshold flip writes here. |

## New artifacts (filesystem-only, NOT database)

These are the only "data" 029 produces:

### Evidence Bundle directory tree (top-level `evidence/`)

See [contracts/evidence-bundle-layout.md](./contracts/evidence-bundle-layout.md) for the canonical directory shape. In summary:

- `evidence/regression/` — JUnit XML + summary markdown.
- `evidence/localization/` — per-surface signed markdown.
- `evidence/rtl/` — visual-diff HTML reports + screenshot ZIPs.
- `evidence/security/` — ASVS L1 control sheet, dep-scan JSON, gitleaks JSON, auth-fuzz markdown, IDOR coverage matrix.
- `evidence/reliability/` — chaos runbook execution logs, provider-sandbox-cooperation matrix.
- `evidence/performance/` — k6 summary JSON per scenario + Grafana snapshot PNGs.
- `evidence/production-smoke/` — captured `/health`, `/version`, seed dry-run output.
- `evidence/containers/` — health-sample logs + rollback rehearsal logs.
- `evidence/dod-audit/` — 22 spec × 18 checkbox audit matrix.
- `evidence/impeccable/` — promotion runbook + rehearsal-PR URL + threshold-flip-PR URL.
- `evidence/launch-readiness/launch-readiness-<DATE>.md` — the **signed launch authorization document** (multi-actor signatures captured inline).

### Bundle entry format (markdown convention)

Every Evidence Bundle markdown file MUST start with the frontmatter:

```markdown
---
spec: 029-qa-and-hardening
pillar: <regression|localization|rtl|security|reliability|performance|production-smoke|containers|dod-audit|impeccable|launch-readiness>
artifact_kind: <run-report|signoff|matrix|runbook|smoke-capture>
date: 2026-MM-DD
commit_sha: <full-sha>
environment: <staging-aca|production-aca|local|na>
owner: <role-name>
sign_off:
  - actor: <name>
    role: <role>
    timestamp: 2026-MM-DDThh:mm:ssZ
status: <pass|fail|in-progress>
---
```

### Test seed fixtures

This spec ships **no production seeders**. Chaos drill harnesses (`tests/chaos/**`) are bash + curl scripts; they do not write to the seeder framework.

## Multi-vendor readiness

N/A for this spec — no schema introduced. The DoD audit (BR-1) re-verifies multi-vendor readiness on every Phase 1A–1E module's existing tables.

## PII handling

Localization sign-off artifacts MAY contain customer-facing string samples; these are public-by-design (they ship in product). RTL screenshot artifacts MUST NOT include real customer PII — Staging seed data uses fake names + emails per spec 003's seed framework. Auth-fuzz artifacts MAY reference test-account JWT payloads; these are sandbox-only and do not leak production credentials.
