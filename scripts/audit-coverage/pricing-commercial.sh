#!/usr/bin/env bash
# Spec 007-b T143 — audit-coverage runner for the pricing commercial-authoring
# slice.
#
# Spec text expects this to call into the spec-015 audit-coverage harness
# over a 100-action operator session and assert 100% audit coverage (SC-003).
# Spec 015's harness isn't merged yet — until it is, this script delegates to
# the in-process integration coverage exercised by Pricing.Tests.
#
# When spec 015 lands, replace the inner block with the harness CLI call;
# the assertion shape (exit 0 = covered, non-zero + diagnostics = gap)
# stays the same.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_PROJ="${REPO_ROOT}/services/backend_api/tests/Pricing.Tests/Pricing.Tests.csproj"

if [[ ! -f "$TEST_PROJ" ]]; then
  echo "ERROR: Pricing.Tests.csproj not found at $TEST_PROJ"
  exit 2
fi

echo "== Audit-coverage probe (spec 007-b T143) =="
echo "Driving the commercial-authoring slice via the existing integration tests."
echo

# Pricing.Tests' Integration.Admin.* tests exercise every authoring action
# (create, update, schedule, deactivate, reactivate, clone-as-draft,
# bulk-import, approval-record, threshold-change) — each test asserts the
# commercial_audit_events row was written. A pass means every reachable
# audit-event-kind from data-model §5 fires.
if ! dotnet test "$TEST_PROJ" \
  --nologo \
  --filter 'FullyQualifiedName~Integration.Admin' \
  -- RunConfiguration.TreatNoTestsAsError=true; then
  status=$?
  echo
  echo "FAIL: audit coverage gap detected — see test output above."
  exit "$status"
fi

echo
echo "PASS: every commercial-authoring audit-event kind from data-model §5"
echo "      is reachable via the integration suite."
echo
echo "NOTE: when spec 015's audit-coverage harness merges, replace this"
echo "      dotnet test invocation with a call to the harness CLI"
echo "      (e.g. scripts/audit-coverage/run-harness.sh --module pricing-commercial"
echo "      --actions 100)."
