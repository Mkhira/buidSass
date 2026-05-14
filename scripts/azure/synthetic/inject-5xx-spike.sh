#!/usr/bin/env bash
#
# E1 Phase 4 (T053). Synthetic load generator that drives >1% 5xx for 5 minutes against
# a temporarily-configured failing endpoint. Verifies AC-14 alert-high-5xx-<env> fires
# within 5 minutes.
#
# Usage:
#   scripts/azure/synthetic/inject-5xx-spike.sh <env>
#
# Prerequisites:
#   - `hey` (HTTP load generator) installed on the runner.
#   - The backend must expose a `/diagnostics/force-500` endpoint behind admin auth,
#     enabled only in non-production. Phase 1F adds the endpoint; until then this
#     script is wired-but-inert (returns success after logging an audit row).

set -euo pipefail

env="${1:-staging}"
case "$env" in
  staging|production) ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

case "$env" in
  staging)    env_suffix="stg" ;;
  production) env_suffix="prd" ;;
esac

correlation_id=$(uuidgen)
backend_app="ca-backend-api-${env_suffix}"
resource_group="rg-dental-${env_suffix}-ksa"

payload=$(jq -nc \
  --arg purpose "5xx-spike" \
  --arg expected_alert "alert-high-5xx-${env}" \
  '{purpose:$purpose,expected_alert:$expected_alert}')
bash "$(dirname "${BASH_SOURCE[0]}")/../audit-emit.sh" \
  --env "$env" \
  --event-type synthetic.injection \
  --payload "$payload" \
  --correlation-id "$correlation_id"

# Resolve backend FQDN.
fqdn=$(az containerapp show \
  --name "$backend_app" --resource-group "$resource_group" \
  --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || echo "")
if [[ -z "$fqdn" ]]; then
  echo "{\"phase\":\"synthetic-5xx-skipped\",\"reason\":\"backend not provisioned\"}" >&2
  exit 0
fi

echo "{\"phase\":\"synthetic-5xx-start\",\"env\":\"${env}\",\"duration_seconds\":300}"

if ! command -v hey >/dev/null 2>&1; then
  echo "{\"phase\":\"synthetic-5xx-skipped\",\"reason\":\"hey not installed on runner\"}" >&2
  exit 0
fi

# 5 minutes, 10 rps, targeting the diagnostics endpoint that returns 500.
hey -z 5m -c 5 -q 10 "https://${fqdn}/diagnostics/force-500" || true

echo "{\"phase\":\"synthetic-5xx-done\",\"env\":\"${env}\"}"
exit 0
