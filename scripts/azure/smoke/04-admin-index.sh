#!/usr/bin/env bash
#
# E1 Phase 2 (T038). Smoke probe 4 — admin web `/` returns 200.
# Contract: contracts/infrastructure-contract.md §7.

set -euo pipefail

env="${1:-staging}"
timeout="${SMOKE_TIMEOUT_SECONDS:-30}"

case "$env" in
  staging)    env_suffix="stg" ;;
  production) env_suffix="prd" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

probe_name="04-admin-index"
app_name="ca-admin-web-${env_suffix}"
resource_group="rg-dental-${env_suffix}-ksa"

fqdn=$(az containerapp show \
  --name "$app_name" \
  --resource-group "$resource_group" \
  --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || echo "")
if [[ -z "$fqdn" ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":0,"message":"could not resolve fqdn for %s"}\n' \
    "$probe_name" "$app_name"
  exit 1
fi

start_ns=$(date +%s%N)
http_code=$(curl -fsS -o /dev/null -m "$timeout" -w "%{http_code}" "https://${fqdn}/" 2>/dev/null || echo "000")
end_ns=$(date +%s%N)
duration_ms=$(( (end_ns - start_ns) / 1000000 ))

if [[ "$http_code" == "200" ]]; then
  printf '{"probe":"%s","status":"pass","duration_ms":%d,"message":"GET https://%s/ -> %s"}\n' \
    "$probe_name" "$duration_ms" "$fqdn" "$http_code"
  exit 0
fi

printf '{"probe":"%s","status":"fail","duration_ms":%d,"message":"GET https://%s/ -> %s"}\n' \
  "$probe_name" "$duration_ms" "$fqdn" "$http_code"
exit 1
