#!/usr/bin/env bash
#
# E1 Phase 2 (T032). Updates a named ACA container app's image to the given tag and
# awaits `Provisioning Succeeded` then `Active`. This is the activation step that runs
# AFTER migrations complete (AC-7 invariant).
#
# Usage:
#   scripts/azure/activate-revision.sh <env> <app_name> <image_tag>
#
# Exit codes:
#   0 = revision active
#   1 = activation failed or timed out
#   2 = invalid arguments

set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <staging|production> <backend-api|admin-web> <image_tag>" >&2
  exit 2
fi

env="$1"
app_kind="$2"
image_tag="$3"

case "$env" in
  staging)    env_suffix="stg"; resource_group="rg-dental-stg-ksa" ;;
  production) env_suffix="prd"; resource_group="rg-dental-prd-ksa" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

case "$app_kind" in
  backend-api|admin-web) ;;
  *) echo "app_name must be 'backend-api' or 'admin-web', got: $app_kind" >&2; exit 2 ;;
esac

app_name="ca-${app_kind}-${env_suffix}"
repo_path="${GITHUB_REPOSITORY:-dental-platform/dental-monorepo}"
new_image="ghcr.io/${repo_path}/${app_kind}:${image_tag}"
# Revision-name suffix is the first 7 chars of the image-tag sha (lowercased, alnum-only).
revision_suffix="$(echo -n "$image_tag" | tr -cd '[:alnum:]' | tr '[:upper:]' '[:lower:]' | head -c12)"

echo "{\"phase\":\"activate-update-image\",\"app\":\"${app_name}\",\"image\":\"${new_image}\",\"revision_suffix\":\"${revision_suffix}\"}"
az containerapp update \
  --name "$app_name" \
  --resource-group "$resource_group" \
  --image "$new_image" \
  --revision-suffix "$revision_suffix" >/dev/null

revision_name="${app_name}--${revision_suffix}"

deadline=$(( $(date +%s) + 600 ))
poll_interval=10
while [[ $(date +%s) -lt $deadline ]]; do
  provisioning=$(az containerapp revision show \
    --name "$app_name" \
    --resource-group "$resource_group" \
    --revision "$revision_name" \
    --query "properties.provisioningState" -o tsv 2>/dev/null || echo "Unknown")
  active=$(az containerapp revision show \
    --name "$app_name" \
    --resource-group "$resource_group" \
    --revision "$revision_name" \
    --query "properties.active" -o tsv 2>/dev/null || echo "false")
  echo "{\"phase\":\"activate-poll\",\"app\":\"${app_name}\",\"revision\":\"${revision_name}\",\"provisioning\":\"${provisioning}\",\"active\":\"${active}\",\"ts\":\"$(date -u +%FT%TZ)\"}"
  if [[ "$provisioning" == "Provisioned" || "$provisioning" == "Succeeded" ]] && [[ "$active" == "true" ]]; then
    # Make this revision the sole-active revision so traffic shifts.
    az containerapp revision activate \
      --name "$app_name" \
      --resource-group "$resource_group" \
      --revision "$revision_name" >/dev/null 2>&1 || true
    az containerapp ingress traffic set \
      --name "$app_name" \
      --resource-group "$resource_group" \
      --revision-weight "${revision_name}=100" >/dev/null 2>&1 || true
    echo "{\"phase\":\"activate-done\",\"app\":\"${app_name}\",\"revision\":\"${revision_name}\"}"
    exit 0
  fi
  if [[ "$provisioning" == "Failed" ]]; then
    echo "{\"phase\":\"activate-done\",\"app\":\"${app_name}\",\"revision\":\"${revision_name}\",\"status\":\"failed\"}" >&2
    exit 1
  fi
  sleep $poll_interval
done

echo "{\"phase\":\"activate-timeout\",\"deadline_seconds\":600}" >&2
exit 1
