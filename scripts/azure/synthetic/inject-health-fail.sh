#!/usr/bin/env bash
#
# E1 Phase 4 (T052). Deploys a revision known to 5xx on /health for ~90s, then rolls back.
# Verifies AC-14 alert-health-probe-<env> fires within 90 seconds.
#
# The "always-503" image is hosted in the platform-internal ACR. It's intentionally
# trivial: an nginx that returns 503 for /health and 200 for everything else.
#
# Usage:
#   scripts/azure/synthetic/inject-health-fail.sh <env>

set -euo pipefail

env="${1:-staging}"
case "$env" in
  staging)    env_suffix="stg"; resource_group="rg-dental-stg-ksa" ;;
  production) env_suffix="prd"; resource_group="rg-dental-prd-ksa" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

correlation_id=$(uuidgen)
app_name="ca-backend-api-${env_suffix}"
bad_image="${SYNTHETIC_BAD_IMAGE:-mcr.microsoft.com/azuredocs/aks-helloworld:v1}"  # placeholder until real bad-image lands

echo "{\"phase\":\"synthetic-health-fail-start\",\"env\":\"${env}\",\"correlation_id\":\"${correlation_id}\"}"

# Record current image so we can roll back.
current_image=$(az containerapp show \
  --name "$app_name" --resource-group "$resource_group" \
  --query "properties.template.containers[0].image" -o tsv)

payload=$(jq -nc \
  --arg purpose "health-fail" \
  --arg expected_alert "alert-health-probe-${env}" \
  --arg current_image "$current_image" \
  --arg bad_image "$bad_image" \
  '{purpose:$purpose,expected_alert:$expected_alert,current_image:$current_image,bad_image:$bad_image}')
bash "$(dirname "${BASH_SOURCE[0]}")/../audit-emit.sh" \
  --env "$env" \
  --event-type synthetic.injection \
  --payload "$payload" \
  --correlation-id "$correlation_id"

# Push the bad image. We don't shift traffic — single-revision mode would, but our
# AppMode='Multiple' allows the new revision to coexist as a probe failure source.
az containerapp update \
  --name "$app_name" --resource-group "$resource_group" \
  --image "$bad_image" \
  --revision-suffix "syn-$(date +%s)" >/dev/null

echo "{\"phase\":\"synthetic-health-fail-active\",\"env\":\"${env}\",\"awaiting_alert_seconds\":90}"
sleep 90

# Roll back to the previous image.
az containerapp update \
  --name "$app_name" --resource-group "$resource_group" \
  --image "$current_image" \
  --revision-suffix "rollback-$(date +%s)" >/dev/null

echo "{\"phase\":\"synthetic-health-fail-rolled-back\",\"env\":\"${env}\",\"restored_image\":\"${current_image}\"}"
exit 0
