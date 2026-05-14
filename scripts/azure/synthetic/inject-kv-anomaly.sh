#!/usr/bin/env bash
#
# E1 Phase 4 (T054). Reads a secret from kv-dental-prd using a non-managed-identity
# principal (a PIM-elevated test account). Verifies AC-14 alert-kv-anomaly-<env> fires
# within 5 minutes.
#
# Usage:
#   scripts/azure/synthetic/inject-kv-anomaly.sh <env>
#
# Prerequisites:
#   - The invoker is `az login`'d as a PIM-elevated human account (NOT a managed
#     identity). The alert rule fires on `principal != id-aca-<env>` reads, so
#     ANY non-MI principal triggers it.
#   - The invoker must have been temporarily granted Key Vault Secrets User on
#     the target vault via PIM elevation immediately before running this script.

set -euo pipefail

env="${1:-production}"   # default production because the alert is most useful there
case "$env" in
  staging)    env_suffix="stg" ;;
  production) env_suffix="prd" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

correlation_id=$(uuidgen)
vault_name="kv-dental-${env_suffix}"
# We pick a placeholder secret (the sentinel value is harmless if read).
target_secret="app--multi--backend-api--jwt-signing-key"

# Confirm the current az context is NOT a managed identity (defense in depth).
actor_type=$(az account show --query "user.type" -o tsv)
if [[ "$actor_type" == "servicePrincipal" ]]; then
  echo "inject-kv-anomaly: az context is a service principal. The alert rule fires on" >&2
  echo "  non-managed-identity reads, but a service principal is borderline — re-run" >&2
  echo "  from a HUMAN account via 'az login' for a clean signal." >&2
  exit 1
fi

payload=$(jq -nc \
  --arg purpose "kv-anomaly" \
  --arg expected_alert "alert-kv-anomaly-${env}" \
  --arg vault "$vault_name" \
  --arg secret "$target_secret" \
  '{purpose:$purpose,expected_alert:$expected_alert,vault:$vault,secret:$secret}')
bash "$(dirname "${BASH_SOURCE[0]}")/../audit-emit.sh" \
  --env "$env" \
  --event-type synthetic.injection \
  --payload "$payload" \
  --correlation-id "$correlation_id"

echo "{\"phase\":\"synthetic-kv-anomaly-start\",\"vault\":\"${vault_name}\",\"secret\":\"${target_secret}\"}"

# Make a single read. The value is discarded; the diagnostic log entry is the point.
az keyvault secret show \
  --vault-name "$vault_name" \
  --name "$target_secret" \
  --query "id" -o tsv >/dev/null

echo "{\"phase\":\"synthetic-kv-anomaly-done\",\"expected_alert_within_seconds\":300}"
exit 0
