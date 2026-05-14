#!/usr/bin/env bash
#
# E1 Phase 2 (T039). Smoke probe 5 — SWA `/` AND `/main.dart.js` both return 200.
# Verifies the Flutter web bundle is uploaded and routes are configured correctly.
# Contract: contracts/infrastructure-contract.md §7.

set -euo pipefail

env="${1:-staging}"
timeout="${SMOKE_TIMEOUT_SECONDS:-30}"

case "$env" in
  staging)    env_suffix="stg" ;;
  production) env_suffix="prd" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

probe_name="05-flutter-web-index"
swa_name="swa-customer-flutter-${env_suffix}"
resource_group="rg-dental-${env_suffix}-ksa"

hostname=$(az staticwebapp show \
  --name "$swa_name" \
  --resource-group "$resource_group" \
  --query "defaultHostname" -o tsv 2>/dev/null || echo "")
if [[ -z "$hostname" ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":0,"message":"could not resolve hostname for %s"}\n' \
    "$probe_name" "$swa_name"
  exit 1
fi

start_ns=$(date +%s%N)
root_code=$(curl -fsS -o /dev/null -m "$timeout" -w "%{http_code}" "https://${hostname}/" 2>/dev/null || echo "000")
js_code=$(curl -fsS -o /dev/null -m "$timeout" -w "%{http_code}" "https://${hostname}/main.dart.js" 2>/dev/null || echo "000")
end_ns=$(date +%s%N)
duration_ms=$(( (end_ns - start_ns) / 1000000 ))

if [[ "$root_code" == "200" && "$js_code" == "200" ]]; then
  printf '{"probe":"%s","status":"pass","duration_ms":%d,"message":"GET / -> %s, GET /main.dart.js -> %s on %s"}\n' \
    "$probe_name" "$duration_ms" "$root_code" "$js_code" "$hostname"
  exit 0
fi

printf '{"probe":"%s","status":"fail","duration_ms":%d,"message":"GET / -> %s, GET /main.dart.js -> %s on %s"}\n' \
  "$probe_name" "$duration_ms" "$root_code" "$js_code" "$hostname"
exit 1
