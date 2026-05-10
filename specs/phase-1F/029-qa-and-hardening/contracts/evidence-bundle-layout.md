# Contract: Launch-Readiness Evidence Bundle Layout

**Spec**: 029-qa-and-hardening
**Phase**: 1F
**Status**: Authoritative for the structure of `evidence/`

This contract defines the canonical directory shape, naming convention, and frontmatter format for every artifact in the Launch-Readiness Evidence Bundle. The Bundle IS the launch-authorization document. Every Section 13 checklist line MUST map to ≥ 1 artifact under `evidence/` per this layout.

---

## §1 — Top-level directory shape

```
evidence/
├── README.md                       (one-page bundle overview)
├── regression/
├── localization/
├── rtl/
├── security/
├── reliability/
├── performance/
├── production-smoke/
├── containers/
├── dod-audit/
├── impeccable/
└── launch-readiness/
```

## §2 — Required frontmatter (every markdown artifact)

```yaml
---
spec: 029-qa-and-hardening
pillar: <regression|localization|rtl|security|reliability|performance|production-smoke|containers|dod-audit|impeccable|launch-readiness>
artifact_kind: <run-report|signoff|matrix|runbook|smoke-capture>
date: 2026-MM-DD
commit_sha: <full-git-sha>
environment: <staging-aca|production-aca|local|na>
owner: <role-or-name>
sign_off:
  - actor: <name>
    role: <Product Lead|Engineering Lead|Operations Lead|Security Lead|Compliance Lead|QA Lead|Arabic Editorial Reviewer|Launch Captain>
    timestamp: 2026-MM-DDThh:mm:ssZ
status: <pass|fail|in-progress>
---
```

## §3 — Per-pillar contents

### §3.1 — `regression/`

| File | Format | Required |
|---|---|---|
| `regression-run-<DATE>.junit.xml` | JUnit XML | Yes |
| `regression-run-<DATE>.summary.md` | Markdown + frontmatter | Yes |

Summary MUST include: pass count, fail count, P0/P1/P2 breakdown, links to remediation issues for any non-pass.

### §3.2 — `localization/`

| File | Format | Required |
|---|---|---|
| `inventory-<DATE>.md` | Markdown (string surface inventory handed to reviewer) | Yes |
| `<surface>-signoff-<DATE>.md` | Markdown + frontmatter — one per surface | Yes — for each surface in the surface set below |
| `consolidated-signoff-<DATE>.md` | Markdown + frontmatter — final consolidated sign-off | Yes |

**Surface set** (per BR-2 + BR-15): `mobile-app`, `web-storefront`, `admin-dashboard`, `transactional-email`, `push-notification`, `sms-template`, `pdf-invoice`, `pdf-tax-receipt`, `cms-legal-pages`, `cms-faq`, `cms-blog`. (Eleven surfaces.)

### §3.3 — `rtl/`

| File | Format | Required |
|---|---|---|
| `rtl-sweep-mobile-<DATE>.html` | Visual-diff HTML report (Percy or equivalent) | Yes |
| `rtl-sweep-web-<DATE>.html` | Visual-diff HTML report | Yes |
| `rtl-sweep-admin-<DATE>.html` | Visual-diff HTML report | Yes |
| `screenshots-<platform>-<DATE>.zip` | Screenshot archive — large, may be linked vs. committed | Yes (link allowed) |

Each report MUST cover both `ar-SA` and `ar-EG` locales.

### §3.4 — `security/`

| File | Format | Required |
|---|---|---|
| `asvs-l1-<DATE>.md` | Markdown — annotated ASVS L1 control sheet | Yes |
| `depscan-dotnet-<DATE>.txt` | `dotnet list package --vulnerable` output | Yes |
| `depscan-admin-<DATE>.json` | `pnpm audit --json` output | Yes |
| `depscan-flutter-<DATE>.txt` | `flutter pub outdated` output | Yes |
| `gitleaks-<DATE>.json` | gitleaks report JSON | Yes |
| `auth-fuzz-<DATE>.md` | Auth-fuzzing test plan + results + findings | Yes |
| `idor-sweep-<DATE>.md` | IDOR endpoint coverage matrix + per-endpoint result | Yes |

ASVS L1 sheet MUST have every control row marked `implemented` or `N/A with documented rationale` — no `gap` rows at exit.

### §3.5 — `reliability/`

| File | Format | Required |
|---|---|---|
| `provider-sandbox-matrix.md` | Per-provider chaos-cooperation capability matrix | Yes (one-time) |
| `chaos-payment-<DATE>.md` | Runbook execution log | Yes |
| `chaos-shipping-<DATE>.md` | Runbook execution log | Yes |
| `chaos-notification-<DATE>.md` | Runbook execution log | Yes |
| `reconciliation-rerun-<DATE>.md` | Post-chaos reconciliation rerun verification | Yes |

### §3.6 — `performance/`

| File | Format | Required |
|---|---|---|
| `rps-baseline-<DATE>.md` | Pre-test RPS baseline assumptions, validated by Operations Lead | Yes |
| `k6-catalog-<DATE>.json` | k6 summary JSON | Yes |
| `k6-search-<DATE>.json` | k6 summary JSON | Yes |
| `k6-checkout-<DATE>.json` | k6 summary JSON | Yes |
| `grafana-snapshot-<scenario>-<DATE>.png` | Dashboard snapshot — large, may be linked | Optional but recommended |

Each k6 JSON MUST report p95 within budget at every ramp step + the 5× hold.

### §3.7 — `production-smoke/`

| File | Format | Required |
|---|---|---|
| `seed-dryrun-<DATE>.log` | Captured `seed --mode=dry-run` output | Yes |
| `health-<DATE>.log` | Captured `/health` response | Yes |
| `version-<DATE>.log` | Captured `/version` response | Yes |
| `smoke-<DATE>.md` | Summary markdown + frontmatter | Yes |

Summary MUST assert: exit 0, zero `seed_applied` rows added, HTTP 200 from `/health`, matching SHA from `/version`.

### §3.8 — `containers/`

| File | Format | Required |
|---|---|---|
| `health-backend_api-<DATE>.md` | Liveness/readiness sample log + rollback rehearsal log | Yes |
| `health-admin_web-<DATE>.md` | Same | Yes |
| `health-flutter_web-<DATE>.md` | Same | Yes |

Each MUST report: 60-second sample window result, rollback target tag, rollback duration, post-rollback health check duration.

### §3.9 — `dod-audit/`

| File | Format | Required |
|---|---|---|
| `dod-audit-<DATE>.md` | 22 spec × 18 checkbox matrix — every cell marked pass/fail/N/A | Yes |

### §3.10 — `impeccable/`

| File | Format | Required |
|---|---|---|
| `promotion-<DATE>.md` | Runbook execution log + linked rehearsal PR + linked threshold-flip PR | Yes |

Promotion log MUST show the rehearsal PR's check history transition: red → labeled → green → label-removed → red.

### §3.11 — `launch-readiness/`

| File | Format | Required |
|---|---|---|
| `launch-readiness-<DATE>.md` | Section 13 checklist + per-line owner / evidence-link / timestamp / sign-off + multi-actor signature block | Yes |

This file IS the launch authorization document. Without four signed roles (Product Lead + Engineering Lead + Operations Lead + Security Lead), launch is not authorized.

## §4 — Sign-off rules

- Every artifact's frontmatter `sign_off` array MUST contain ≥ 1 entry.
- The `actor` field MUST be a real human name (not `automated`, not a role-only string), unless `artifact_kind: run-report` AND the run was machine-driven (in which case `actor: ci-runner` is acceptable provided the bundle index links to a human-signed validation note).
- The `launch-readiness/launch-readiness-<DATE>.md` document MUST contain ≥ 4 sign-off entries: Product Lead, Engineering Lead, Operations Lead, Security Lead.
- Once signed, an artifact MUST NOT be modified except via a new `-vN` suffixed file (preserves audit trail).

## §5 — Storage location

Default: top-level `evidence/` directory in this repository (`buidSass`).

Operations Lead may relocate to a dedicated docs repository if total Bundle size exceeds 500 MB or asset count exceeds 1000. Relocation MUST preserve the directory shape defined in §1 and the frontmatter format defined in §2. The launch-readiness document MUST remain in this repository regardless of overall Bundle location, with linked references to the relocated artifacts.

## §6 — Bundle exit criterion

The Bundle is considered complete when:

1. Every directory in §1 contains the artifacts required by §3.
2. Every Section 13 checklist line in `launch-readiness-<DATE>.md` is checked + evidence-linked.
3. Zero open blocker labels (`phase-1f-blocker`, `loc-ar-blocker`, `rtl-blocker`, `security-blocker`, `launch-blocker`).
4. Four-role sign-off captured in `launch-readiness-<DATE>.md`.
