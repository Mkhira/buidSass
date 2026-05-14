#!/usr/bin/env bash
#
# E1 Phase 2 (T031). Updates the caj-ef-migrate-<env> job's image to the given tag,
# starts the job, and polls for terminal status with a 15-minute hard timeout.
#
# Usage:
#   scripts/azure/run-migrations-job.sh <env> <image_tag>
#
# Exit codes:
#   0 = migrations succeeded
#   1 = migrations failed or timed out
#   2 = invalid arguments
#
# Emits a structured-JSON log line on each poll for machine-parsing.

set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <staging|production> <image_tag>" >&2
  exit 2
fi

env="$1"
image_tag="$2"

case "$env" in
  staging)    env_suffix="stg"; resource_group="rg-dental-stg-ksa" ;;
  production) env_suffix="prd"; resource_group="rg-dental-prd-ksa" ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

job_name="caj-ef-migrate-${env_suffix}"
# The backend image registry is pinned in the workflow; the tag floats. The job's image
# template is `ghcr.io/<org>/backend-api:<tag>` — we patch it in place.
new_image="ghcr.io/${GITHUB_REPOSITORY:-dental-platform/dental-monorepo}/backend-api:${image_tag}"

echo "{\"phase\":\"migrate-update-image\",\"job\":\"${job_name}\",\"image\":\"${new_image}\"}"
az containerapp job update \
  --name "$job_name" \
  --resource-group "$resource_group" \
  --image "$new_image" >/dev/null

echo "{\"phase\":\"migrate-start\",\"job\":\"${job_name}\"}"
execution_name=$(az containerapp job start \
  --name "$job_name" \
  --resource-group "$resource_group" \
  --query "id" -o tsv | awk -F'/' '{print $NF}')
echo "{\"phase\":\"migrate-execution-started\",\"job\":\"${job_name}\",\"execution\":\"${execution_name}\"}"

# Poll for terminal status with a 15-minute hard timeout. Status values per
# Azure docs: Pending | Running | Succeeded | Failed | Stopped.
deadline=$(( $(date +%s) + 900 ))
poll_interval=15
while [[ $(date +%s) -lt $deadline ]]; do
  status=$(az containerapp job execution show \
    --name "$job_name" \
    --resource-group "$resource_group" \
    --job-execution-name "$execution_name" \
    --query "properties.status" -o tsv 2>/dev/null || echo "Unknown")
  echo "{\"phase\":\"migrate-poll\",\"job\":\"${job_name}\",\"execution\":\"${execution_name}\",\"status\":\"${status}\",\"ts\":\"$(date -u +%FT%TZ)\"}"
  case "$status" in
    Succeeded)
      echo "{\"phase\":\"migrate-done\",\"status\":\"succeeded\"}"
      exit 0 ;;
    Failed|Stopped)
      echo "{\"phase\":\"migrate-done\",\"status\":\"failed\",\"reason\":\"${status}\"}" >&2
      exit 1 ;;
    Pending|Running|Unknown)
      sleep $poll_interval ;;
    *)
      echo "{\"phase\":\"migrate-unknown\",\"status\":\"${status}\"}" >&2
      sleep $poll_interval ;;
  esac
done

echo "{\"phase\":\"migrate-timeout\",\"deadline_seconds\":900}" >&2
exit 1
