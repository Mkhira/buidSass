#!/usr/bin/env bash
#
# E1 Phase 2 (T037). Smoke probe 3 — one Meilisearch query against the `products` index
# returns ≥ 1 hit. Fetches the master key from KV at probe time (no secret in workflow
# secrets store).
# Contract: contracts/infrastructure-contract.md §7.

set -euo pipefail

env="${1:-staging}"
timeout="${SMOKE_TIMEOUT_SECONDS:-30}"

case "$env" in
  staging)    env_suffix="stg" ;;
  production) env_suffix="prd" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

probe_name="03-meili-query"
meili_app="ca-meili-${env_suffix}"
resource_group="rg-dental-${env_suffix}-ksa"
vault_name="kv-dental-${env_suffix}"
secret_name="meili--multi--self-hosted--master-key"  # storage-flattened logical path

start_ns=$(date +%s%N)

# Resolve Meilisearch endpoint (internal-only ingress; we exec into the backend app and
# query from there so we use the in-cluster routing).
fqdn=$(az containerapp show \
  --name "$meili_app" \
  --resource-group "$resource_group" \
  --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || echo "")
if [[ -z "$fqdn" ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":0,"message":"could not resolve meili fqdn"}\n' \
    "$probe_name"
  exit 1
fi

# Fetch master key from KV (the workflow's deploy principal has Secrets User on this vault).
master_key=$(az keyvault secret show \
  --vault-name "$vault_name" \
  --name "$secret_name" \
  --query "value" -o tsv 2>/dev/null || echo "")
if [[ -z "$master_key" ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":0,"message":"could not read meili master key from %s"}\n' \
    "$probe_name" "$vault_name"
  exit 1
fi

# Issue the query. Meili returns a JSON `{"hits": [...], "estimatedTotalHits": N, ...}` shape.
response=$(curl -fsS -m "$timeout" \
  -H "Authorization: Bearer $master_key" \
  -H "Content-Type: application/json" \
  -X POST "https://${fqdn}/indexes/products/search" \
  -d '{"q":"","limit":1}' 2>/dev/null || echo "")
end_ns=$(date +%s%N)
duration_ms=$(( (end_ns - start_ns) / 1000000 ))

if [[ -z "$response" ]]; then
  printf '{"probe":"%s","status":"fail","duration_ms":%d,"message":"meili search returned empty body"}\n' \
    "$probe_name" "$duration_ms"
  exit 1
fi

hits=$(printf '%s' "$response" | grep -oE '"estimatedTotalHits"\s*:\s*[0-9]+' | head -1 | grep -oE '[0-9]+$' || echo "0")
if [[ "$hits" -ge 1 ]]; then
  printf '{"probe":"%s","status":"pass","duration_ms":%d,"message":"meili search returned %d total hits"}\n' \
    "$probe_name" "$duration_ms" "$hits"
  exit 0
fi

printf '{"probe":"%s","status":"fail","duration_ms":%d,"message":"meili search returned 0 hits"}\n' \
  "$probe_name" "$duration_ms"
exit 1
