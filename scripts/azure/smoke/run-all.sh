#!/usr/bin/env bash
#
# E1 Phase 2 (T040). Smoke-probe orchestrator. Runs probes 01–05 against the given env,
# aggregates JSON to /tmp/smoke-results.json, exits 0 only if all five pass.
# Contract: contracts/infrastructure-contract.md §7.

set -uo pipefail   # NOT -e: we want to run every probe even if one fails.

env="${1:-staging}"
output="${SMOKE_RESULTS_PATH:-/tmp/smoke-results.${env}.json}"

case "$env" in
  staging|production) ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
probes=(
  "${script_dir}/01-health.sh"
  "${script_dir}/02-seed-dryrun.sh"
  "${script_dir}/03-meili-query.sh"
  "${script_dir}/04-admin-index.sh"
  "${script_dir}/05-flutter-web-index.sh"
)

# Build aggregated results JSON array.
echo "[" > "$output"
first=1
failures=0

for probe in "${probes[@]}"; do
  if [[ ! -x "$probe" ]]; then
    echo "run-all: probe not executable: $probe" >&2
    failures=$((failures + 1))
    continue
  fi
  result="$(bash "$probe" "$env" || true)"
  if [[ "$first" -eq 0 ]]; then
    echo "," >> "$output"
  fi
  first=0
  # Append the probe's JSON line (each probe is contractually a single JSON line).
  printf '  %s' "$result" >> "$output"
  # Capture failure.
  if [[ "$result" == *'"status":"fail"'* || -z "$result" ]]; then
    failures=$((failures + 1))
    # Also echo to stderr so the workflow log surfaces the probe-name on failure.
    echo "FAILED: $probe — $result" >&2
  fi
done

echo "" >> "$output"
echo "]" >> "$output"

# Emit summary line.
total=${#probes[@]}
passed=$((total - failures))
printf '{"phase":"smoke-run-all","env":"%s","total":%d,"passed":%d,"failed":%d,"results_file":"%s"}\n' \
  "$env" "$total" "$passed" "$failures" "$output"

if [[ "$failures" -gt 0 ]]; then
  exit 1
fi
exit 0
