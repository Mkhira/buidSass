#!/usr/bin/env bash
#
# E1 Phase 2 (T033). Runs the seed CLI inside ca-backend-api-<env> via `az containerapp
# exec`. Refuses --mode=apply when env=production (defense in depth on top of spec 003's
# environment guard in Program.cs).
#
# Usage:
#   scripts/azure/run-seed-job.sh <env> <apply|dry-run>
#
# Exit codes mirror the seed CLI: 0=ok, 1=guard violation, 2=runtime error.

set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <staging|production> <apply|dry-run>" >&2
  exit 2
fi

env="$1"
mode="$2"

case "$env" in
  staging)    env_suffix="stg"; resource_group="rg-dental-stg-ksa" ;;
  production) env_suffix="prd"; resource_group="rg-dental-prd-ksa" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

case "$mode" in
  apply|dry-run) ;;
  *) echo "mode must be 'apply' or 'dry-run', got: $mode" >&2; exit 2 ;;
esac

# Defense-in-depth: production + apply is forbidden at the script layer, even though
# spec 003's SeedGuard in Program.cs already hard-blocks it.
if [[ "$env" == "production" && "$mode" == "apply" ]]; then
  echo "run-seed-job: REFUSED — seed --mode=apply is never executed against production." >&2
  echo "  (Belt-and-suspenders on top of spec 003 SeedGuard.)" >&2
  exit 1
fi

app_name="ca-backend-api-${env_suffix}"
output_file="/tmp/seed-output.${env}.json"

echo "{\"phase\":\"seed-start\",\"env\":\"${env}\",\"mode\":\"${mode}\",\"app\":\"${app_name}\"}"

# Capture both stdout and exit code without losing either. The dotnet CLI emits a single
# JSON summary on stdout in --mode=apply; --mode=dry-run emits a count-only summary.
set +e
az containerapp exec \
  --name "$app_name" \
  --resource-group "$resource_group" \
  --command "/bin/sh -lc 'dotnet /app/backend_api.dll seed --mode=${mode}'" \
  > "$output_file" 2>&1
exit_code=$?
set -e

if [[ "$exit_code" -ne 0 ]]; then
  echo "{\"phase\":\"seed-failed\",\"exit_code\":${exit_code},\"output_path\":\"${output_file}\"}" >&2
  tail -50 "$output_file" >&2 || true
  exit "$exit_code"
fi

echo "{\"phase\":\"seed-done\",\"env\":\"${env}\",\"mode\":\"${mode}\",\"output_path\":\"${output_file}\"}"
exit 0
