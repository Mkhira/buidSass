# Launch-Readiness Document Template

> Copy this template to `evidence/launch-readiness/launch-readiness-<DATE>.md` at the start of Phase 9 (US-9 / tasks T060+). The Launch Captain walks the entire Section 13 checklist below; every line MUST end up checked + evidence-linked + timestamped + signed before launch authorization is granted.

---

```yaml
---
spec: 029-qa-and-hardening
pillar: launch-readiness
artifact_kind: signoff
date: 2026-MM-DD
commit_sha: <full-git-sha>
environment: na
owner: Launch Captain
sign_off:
  - actor: <Product Lead name>
    role: Product Lead
    timestamp: 2026-MM-DDThh:mm:ssZ
  - actor: <Engineering Lead name>
    role: Engineering Lead
    timestamp: 2026-MM-DDThh:mm:ssZ
  - actor: <Operations Lead name>
    role: Operations Lead
    timestamp: 2026-MM-DDThh:mm:ssZ
  - actor: <Security Lead name>
    role: Security Lead
    timestamp: 2026-MM-DDThh:mm:ssZ
status: pass
---
```

# Launch Readiness — `<DATE>`

**Launch Captain**: `<name>`
**Launch candidate image tag**: `<tag>`
**Launch authorization status**: `<DRAFT | AUTHORIZED>`

---

## Product

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| P-1 | All Phase 1 scope in Section 8 functional. | Product Lead | `evidence/regression/regression-run-<DATE>.summary.md` | | |
| P-2 | Market rules reviewed for EG and KSA. | Product Lead + Compliance Lead | `evidence/dod-audit/dod-audit-<DATE>.md` (Principle 5 cells) | | |
| P-3 | Restricted-product logic verified end-to-end. | QA Lead | `evidence/regression/regression-run-<DATE>.summary.md` (restricted-flow scenarios) | | |
| P-4 | Returns/refunds flow operational. | Operations Lead | `evidence/regression/regression-run-<DATE>.summary.md` (return-flow scenarios) | | |
| P-5 | B2B quote → order conversion tested. | QA Lead | `evidence/regression/regression-run-<DATE>.summary.md` (B2B scenarios) | | |

## Engineering

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| E-1 | Staging stable. | Engineering Lead | `evidence/containers/health-*.md` | | |
| E-2 | Migrations repeatable on a clean DB. | Engineering Lead | `evidence/dod-audit/dod-audit-<DATE>.md` (migration cells) | | |
| E-3 | Backup and restore verified (in-region per ADR-010). | Operations Lead | `evidence/launch-readiness/backup-restore-<DATE>.md` (linked) | | |
| E-4 | Secrets managed (no in-repo). | Security Lead | `evidence/security/gitleaks-<DATE>.json` | | |
| E-5 | Rate limits configured on public endpoints. | Security Lead | `evidence/dod-audit/dod-audit-<DATE>.md` (rate-limit cells) | | |
| E-6 | ADRs 001–006 + 010 Accepted; 007, 008, 009 resolved. | Engineering Lead | `evidence/dod-audit/dod-audit-<DATE>.md` | | |
| E-7 | Lint + format bar green on `main`. | Engineering Lead | `<CI run URL>` | | |
| E-8 | Contract tests green on `main`. | Engineering Lead | `<CI run URL>` | | |
| E-9 | CODEOWNERS enforcement verified. | Security Lead | `evidence/impeccable/promotion-<DATE>.md` (waiver flow demonstrates CODEOWNERS gate) | | |

## Integrations

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| I-1 | Payment providers live-ready per market (incl. BNPL: Tabby/Tamara KSA + Valu EG). | Engineering Lead | `evidence/reliability/chaos-payment-<DATE>.md` + `evidence/regression/...` | | |
| I-2 | Shipping providers tested per market. | Engineering Lead | `evidence/reliability/chaos-shipping-<DATE>.md` | | |
| I-3 | OTP/SMS delivered to test numbers in EG and KSA. | Operations Lead | `evidence/regression/...` (OTP scenarios) | | |
| I-4 | Email delivered with correct Arabic rendering. | QA Lead | `evidence/localization/transactional-email-signoff-<DATE>.md` | | |
| I-5 | Push verified on Android + iOS. | QA Lead | `evidence/regression/...` (mobile push scenarios) | | |
| I-6 | PDF invoices correct in Arabic and English. | QA Lead | `evidence/localization/pdf-invoice-signoff-<DATE>.md` + `evidence/localization/pdf-tax-receipt-signoff-<DATE>.md` | | |
| I-7 | (WhatsApp NOT in scope for launch — Phase 1.5.) | — | N/A | N/A | N/A |

## Operations

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| O-1 | Catalog loaded. | Operations Lead | `evidence/launch-readiness/catalog-load-<DATE>.md` (linked) | | |
| O-2 | Support team trained. | Operations Lead | `evidence/launch-readiness/support-training-<DATE>.md` (linked) | | |
| O-3 | Admin roles assigned per permissions matrix. | Security Lead | `evidence/dod-audit/dod-audit-<DATE>.md` (RBAC cells) | | |
| O-4 | Verification SOP ready. | Operations Lead | `docs/sop/verification-sop.md` | | |
| O-5 | Refund SOP ready. | Operations Lead | `docs/sop/refund-sop.md` | | |
| O-6 | Order-ops SOP ready. | Operations Lead | `docs/sop/order-ops-sop.md` | | |

## QA

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| Q-1 | Full regression passed. | QA Lead | `evidence/regression/regression-run-<DATE>.junit.xml` | | |
| Q-2 | Arabic editorial QA passed by a named human reviewer. | Arabic Editorial Reviewer | `evidence/localization/consolidated-signoff-<DATE>.md` | | |
| Q-3 | Web + mobile (Android + iOS) smoke tests passed. | QA Lead | `evidence/rtl/rtl-sweep-{mobile,web,admin}-<DATE>.html` + regression summary | | |
| Q-4 | Admin permissions matrix tested. | Security Lead | `evidence/security/idor-sweep-<DATE>.md` + regression admin scenarios | | |

## Compliance

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| C-1 | KSA PDPL checks passed (residency + privacy notices). | Compliance Lead | `evidence/launch-readiness/ksa-pdpl-<DATE>.md` (linked) | | |
| C-2 | Egypt Law 151/2020 checks passed. | Compliance Lead | `evidence/launch-readiness/eg-law-151-<DATE>.md` (linked) | | |
| C-3 | Egypt VAT invoice format verified with an accountant. | Compliance Lead | `evidence/launch-readiness/eg-vat-accountant-signoff-<DATE>.md` (linked) | | |
| C-4 | Legal pages in Arabic + English reviewed. | Compliance Lead | `evidence/localization/cms-legal-pages-signoff-<DATE>.md` | | |
| C-5 | Azure Saudi Arabia Central confirmed for all tenants; no out-of-region data paths. | Engineering Lead + Compliance Lead | `evidence/production-smoke/smoke-<DATE>.md` (region check) + Terraform/Bicep CI region-guard run | | |

## Monitoring

| # | Line | Owner | Evidence | Timestamp | Sign-off |
|---|---|---|---|---|---|
| M-1 | Uptime monitor live. | Operations Lead | `<monitor URL>` | | |
| M-2 | Error tracking active. | Engineering Lead | `<error tracker URL>` | | |
| M-3 | Structured logs accessible. | Engineering Lead | `<log search URL>` | | |
| M-4 | Payment-failure alerts firing. | Operations Lead | `evidence/reliability/chaos-payment-<DATE>.md` (alert observed during drill) | | |

---

## Blocker tally (must be 0 to authorize)

```bash
gh issue list --label phase-1f-blocker,loc-ar-blocker,rtl-blocker,security-blocker,launch-blocker --state open
```

| Label | Open count |
|---|---|
| `phase-1f-blocker` | |
| `loc-ar-blocker` | |
| `rtl-blocker` | |
| `security-blocker` | |
| `launch-blocker` | |

## Final authorization

When every line above is checked, every cell has owner + evidence + timestamp + sign-off, and the blocker tally is 0:

- [ ] Product Lead — `<name>` — `<ISO timestamp>`
- [ ] Engineering Lead — `<name>` — `<ISO timestamp>`
- [ ] Operations Lead — `<name>` — `<ISO timestamp>`
- [ ] Security Lead — `<name>` — `<ISO timestamp>`

When all four boxes are checked, **launch is authorized.** This document's git SHA goes into the launch deploy's release notes.
