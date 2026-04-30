#!/usr/bin/env bash
# Spec 022 T140 — audit-coverage runner for the reviews module.
#
# Spec text expects this to call into the spec-015 audit-coverage harness
# over a 100-action operator + customer session and assert 100% audit
# coverage. Spec 015's harness isn't merged yet — until it is, this script
# delegates to the in-process integration coverage that already runs every
# audit-event kind through its lifecycle path.
#
# When spec 015 lands, replace the inner block with the harness CLI call;
# the assertion shape (exit 0 = covered, non-zero + diagnostics = gap)
# stays the same.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_PROJ="${REPO_ROOT}/services/backend_api/Tests/Reviews.Tests/Reviews.Tests.csproj"

if [[ ! -f "$TEST_PROJ" ]]; then
  echo "ERROR: Reviews.Tests.csproj not found at $TEST_PROJ"
  exit 2
fi

echo "== Audit-coverage probe (spec 022 T140) =="
echo "Driving every audit-event kind via AuditCoverageTests E2E walk."
echo

# AuditCoverageTests exercises the full surface (submit, edit, threshold,
# decide, refund, admin-note, wordlist upsert, market-schema update) and
# asserts every one of the 14 audit-event kinds is reachable.
# Use `if !` so set -e doesn't exit before the diagnostic prints —
# under `set -euo pipefail`, a failing command short-circuits the script
# before the legacy `status=$?` branch could ever run.
if ! dotnet test "$TEST_PROJ" \
  --nologo \
  --filter 'FullyQualifiedName~AuditCoverageTests' \
  -- RunConfiguration.TreatNoTestsAsError=true; then
  status=$?
  echo
  echo "FAIL: audit coverage gap detected — see test output above."
  exit "$status"
fi

echo
echo "PASS: every audit-event kind from data-model §5 is reachable via review_moderation_decisions"
echo "      / review_admin_notes / review_flags / reviews_filter_wordlists / reviews_market_schemas."
echo
echo "NOTE: when spec 015's audit-coverage harness merges, replace this dotnet test invocation"
echo "      with a call to the harness CLI (e.g. scripts/audit-coverage/run-harness.sh --module reviews"
echo "      --actions 100). The harness probe is stronger because it samples random action sequences;"
echo "      this script is the deterministic-walk fallback."
