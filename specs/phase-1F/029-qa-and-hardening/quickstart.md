# Quickstart: 029 — QA & Launch Hardening

**Phase**: 1
**Date**: 2026-05-10
**Audience**: Launch Captain + per-pillar leads (QA / Engineering / Security / Operations / Compliance / Arabic Editorial Reviewer).

## Prerequisites

- All Phase 1A–1E specs at DoD. Verify via `evidence/dod-audit/dod-audit-<DATE>.md` (Phase 1 of this spec produces it).
- Risk 11 (Arabic editorial reviewer) resolved — reviewer named in the Risk Register.
- Staging ACA stack at the launch-candidate image tag.
- Production ACA stack provisioned in `Saudi Arabia Central` at the launch-candidate image tag.
- Provider sandbox accounts active (HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu, Aramex, SMSA, Mylerz, SES, Unifonic, FCM).
- k6 + gitleaks CLIs installed on QA workstation.

## Step 0 — Advance the SpecKit feature pointer (Phase 0 setup task T003)

```bash
cat > .specify/feature.json <<'JSON'
{
  "feature_directory": "specs/phase-1F/029-qa-and-hardening"
}
JSON
git add .specify/feature.json
git commit -m "chore(specify): advance feature pointer to phase-1F/029"
```

## Step 1 — Bootstrap the Evidence Bundle

```bash
mkdir -p evidence/{regression,localization,rtl,security,reliability,performance,production-smoke,containers,dod-audit,impeccable,launch-readiness}
cp specs/phase-1F/029-qa-and-hardening/contracts/evidence-bundle-layout.md evidence/README.md
git add evidence/
git commit -m "chore: bootstrap launch-readiness evidence bundle"
```

## Step 2 — Verify Risk 11 resolution

```bash
grep -A 2 'Risk 11' docs/risks/risk-register.md
# Expected: a "resolved on YYYY-MM-DD; reviewer: <name>" line.
# If not resolved, STOP. Escalate to Product Lead before continuing.
```

## Step 3 — Run the DoD audit (Phase 1 of this spec)

```bash
./scripts/qa/run-dod-audit.sh > evidence/dod-audit/dod-audit-$(date +%F).md
# The script walks each Phase 1A–1E spec and prints a 18-checkbox matrix.
# Open the file and mark each cell pass/fail/N/A. Failures = phase-1f-blocker.
```

## Step 4 — Run functional regression (US-1)

```bash
./scripts/qa/run-regression.sh staging
# Outputs JUnit XML to evidence/regression/regression-run-$(date +%F).junit.xml
# Outputs summary.md alongside.
```

If P0 / P1 failures surface: open `phase-1f-blocker` issues, remediate, re-run.

## Step 5 — Kick off Arabic localization audit (US-2 — long-pole; runs in parallel)

```bash
./scripts/qa/build-localization-inventory.sh > evidence/localization/inventory-$(date +%F).md
# Hand to the named Arabic editorial reviewer.
# Expect rolling per-surface sign-offs landing over ~5 working days.
```

## Step 6 — Run RTL visual regression sweep (US-3)

```bash
./scripts/qa/rtl-visual-sweep.sh staging
# Produces evidence/rtl/rtl-sweep-{mobile,web,admin}-$(date +%F).html
# Each report shows ar-SA + ar-EG diffs vs. baseline.
```

If P0 / P1 visual regressions: open `rtl-blocker` issues, remediate, re-run.

## Step 7 — Run security pass (US-4)

```bash
# Dependency scan
dotnet list services/backend_api package --vulnerable --include-transitive > evidence/security/depscan-dotnet-$(date +%F).txt
pnpm --filter ./apps/admin_web audit --audit-level=moderate --json > evidence/security/depscan-admin-$(date +%F).json
flutter pub outdated --mode=null-safety -C apps/customer_flutter > evidence/security/depscan-flutter-$(date +%F).txt

# Secret scan
gitleaks detect --redact --no-git -s . --report-path evidence/security/gitleaks-$(date +%F).json

# OWASP ASVS L1 walk
cp docs/security/asvs-l1-controls.md evidence/security/asvs-l1-$(date +%F).md
# Manually annotate each control with implemented | N/A with rationale.

# Auth fuzzing — see scripts/qa/auth-fuzz-runbook.md
# IDOR sweep — see scripts/qa/idor-runbook.md
```

If any High / Critical / secret / bypass / IDOR finding: open `security-blocker`, remediate, re-run.

## Step 8 — Run reliability chaos drills (US-5)

```bash
./tests/chaos/payment/inject-hyperpay-5xx.sh staging
./tests/chaos/payment/verify-dead-letter.sh staging > evidence/reliability/chaos-payment-$(date +%F).md

./tests/chaos/shipping/inject-aramex-webhook-outage.sh staging
./tests/chaos/shipping/verify-timeout-reconciler.sh staging > evidence/reliability/chaos-shipping-$(date +%F).md

./tests/chaos/notification/inject-ses-bounce.sh staging
./tests/chaos/notification/verify-delivery-log.sh staging > evidence/reliability/chaos-notification-$(date +%F).md
```

After each drill, **rerun the reconciliation jobs** and verify orphaned-row attribution is clean:

```bash
# 027 daily reconciliation
curl -X POST -H "X-Admin-Key: $STAGING_ADMIN_KEY" https://staging.api/admin/payments/reconciliation/run

# 026 timeout reconciliation
curl -X POST -H "X-Admin-Key: $STAGING_ADMIN_KEY" https://staging.api/admin/shipping/reconciliation/run

# 025 delivery-log audit
curl -X POST -H "X-Admin-Key: $STAGING_ADMIN_KEY" https://staging.api/admin/notifications/delivery-log/audit
```

## Step 9 — Run performance k6 (US-6)

```bash
# Catalog
./scripts/qa/run-k6-stepped.sh tests/load/catalog.js > evidence/performance/k6-catalog-$(date +%F).json

# Search
./scripts/qa/run-k6-stepped.sh tests/load/search.js > evidence/performance/k6-search-$(date +%F).json

# Checkout
./scripts/qa/run-k6-stepped.sh tests/load/checkout.js > evidence/performance/k6-checkout-$(date +%F).json
```

Each run ramps 1× → 3× → 5× over 60 min, holds 5× for 15 min, asserts p95 budgets.

## Step 10 — Run Production smoke (US-7)

```bash
# Production seed dry-run
ASPNETCORE_ENVIRONMENT=Production dotnet run --project services/backend_api -- seed --mode=dry-run \
  > evidence/production-smoke/seed-dryrun-$(date +%F).log
# Expected: exit 0; SELECT count(*) FROM seed_applied; unchanged.

# Production /health
curl -i https://<production-aca-host>/health > evidence/production-smoke/health-$(date +%F).log

# Production /version
curl -i https://<production-aca-host>/version > evidence/production-smoke/version-$(date +%F).log
```

Author the smoke summary at `evidence/production-smoke/smoke-$(date +%F).md`.

## Step 11 — Container health + rollback rehearsal (US-8)

```bash
./scripts/qa/sample-container-health.sh staging backend_api 60 > evidence/containers/health-backend_api-$(date +%F).md
./scripts/qa/sample-container-health.sh staging admin_web 60 > evidence/containers/health-admin_web-$(date +%F).md
./scripts/qa/sample-container-health.sh staging flutter_web 60 > evidence/containers/health-flutter_web-$(date +%F).md

# Rollback rehearsal — per container
./scripts/qa/rehearse-rollback.sh staging backend_api <previous-tag>
./scripts/qa/rehearse-rollback.sh staging admin_web <previous-tag>
./scripts/qa/rehearse-rollback.sh staging flutter_web <previous-tag>
```

Each rehearsal must complete < 5 min; post-rollback health < 30 s.

## Step 12 — `impeccable-scan` promotion (US-10)

```bash
# 1. Open the rehearsal PR (intentionally breaches a P1 budget)
git checkout -b chore/impeccable-enforcement-rehearsal main
# … introduce a P1 breach in apps/admin_web …
git push -u origin chore/impeccable-enforcement-rehearsal
gh pr create --draft --title "chore: impeccable-scan enforcement rehearsal (DO NOT MERGE)" \
  --body "Rehearsing red → waiver → unblock cycle per spec 029 US-10."

# 2. Verify red check on the rehearsal PR.
# 3. Apply impeccable-waiver label; CODEOWNERS reviewer approves.
# 4. Verify check unblocks.
# 5. Remove the label; verify re-lock.
# 6. Close the rehearsal PR.

# 7. Open the threshold-flip PR
git checkout -b chore/impeccable-enforcement-flip main
# Edit .impeccable/thresholds.json: "mode": "advisory" → "enforced"
# Edit .github/workflows/impeccable-scan.yml: remove "Advisory exit" step; add "Threshold check" step
# Edit CODEOWNERS: add line for .github/workflows/impeccable-scan.yml
git push -u origin chore/impeccable-enforcement-flip
gh pr create --title "chore: promote impeccable-scan to merge-blocking on apps/admin_web" \
  --body "Per spec 029 US-10. Rehearsal PR: <link>. See evidence/impeccable/promotion-<DATE>.md."
```

Capture the promotion + rehearsal evidence at `evidence/impeccable/promotion-$(date +%F).md`.

## Step 13 — Walk the Section 13 launch-readiness checklist (US-9)

```bash
cp specs/phase-1F/029-qa-and-hardening/contracts/launch-readiness-template.md \
   evidence/launch-readiness/launch-readiness-$(date +%F).md
# Walk every line. For each, fill: owner, evidence link, timestamp, sign-off.
# Lines without evidence → open launch-blocker; remediate.
```

When zero blockers remain, capture multi-actor signatures (Product Lead + Engineering Lead + Operations Lead + Security Lead) inline in the document. Commit.

## Step 14 — Launch authorization granted

The signed `evidence/launch-readiness/launch-readiness-<DATE>.md` is the launch-authorization document. Carry its SHA in the launch deploy's release notes. Phase 1F exits. Phase 1.5 begins.
