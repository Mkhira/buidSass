#!/usr/bin/env bash
#
# E1 Phase 2 (T036). Smoke probe 2 — `seed --mode=dry-run` exits 0 and reports zero
# `seed_applied` rows. Verifies the seed pipeline is reachable and the database is
# accessible from the running backend, without writing anything.
# Contract: contracts/infrastructure-contract.md §7.

set -euo pipefail

env="${1:-staging}"
timeout="${SMOKE_TIMEOUT_SECONDS:-30}"

case "$env" in
  staging)    env_suffix="stg" ;;
  production) env_suffix="prd" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

probe_name="02-seed-dryrun"
app_name="ca-backend-api-${env_suffix}"
resource_group="rg-dental-${env_suffix}-ksa"

start_ns=$(date +%s%N)
set +e
output=$(timeout "$timeout" az containerapp exec \
  --name "$app_name" \
  --resource-group "$resource_group" \
  --command "/bin/sh -lc 'dotnet /app/backend_api.dll seed --mode=dry-run'" 2>&1)
exit_code=$?
set -e
end_ns=$(date +%s%N)
duration_ms=$(( (end_ns - start_ns) / 1000000 ))

if [[ "$exit_code" -ne 0 ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":%d,"message":"seed dry-run exit code %d"}\n' \
    "$probe_name" "$duration_ms" "$exit_code"
  echo "$output" >&2
  exit 1
fi

# The CLI emits a JSON summary on success; we parse seed_applied (count of mutating rows).
# In dry-run mode this MUST be 0.
applied=$(printf '%s' "$output" | grep -oE '"seed_applied"\s*:\s*[0-9]+' | head -1 | grep -oE '[0-9]+$' || echo "0")
if [[ "$applied" -ne 0 ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":%d,"message":"dry-run reported %d applied rows (must be 0)"}\n' \
    "$probe_name" "$duration_ms" "$applied"
  exit 1
fi

printf '{"probe":"%s","status":"pass","duration_ms":%d,"message":"seed dry-run exit 0, 0 applied rows"}\n' \
  "$probe_name" "$duration_ms"
exit 0
