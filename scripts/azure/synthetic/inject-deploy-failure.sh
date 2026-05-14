#!/usr/bin/env bash
#
# E1 Phase 4 (T051). Synthetic-failure injection — forces deploy-staging.yml to fail at
# the migrations step by triggering it with an invalid image tag. Verifies AC-14 alert-
# deploy-failure-stg fires within 2 minutes.
#
# Logs an `event_type=synthetic.injection` audit row BEFORE triggering (so the deliberate
# failure is distinguishable from a real deploy failure in the audit log).
#
# Usage:
#   scripts/azure/synthetic/inject-deploy-failure.sh <env> [<bad_image_tag>]
#
# Default bad tag: "nonexistent-sha-syn"

set -euo pipefail

env="${1:-staging}"
bad_tag="${2:-nonexistent-sha-syn-$(date +%s)}"

case "$env" in
  staging|production) ;;
  *) echo "env must be 'staging' or 'production', got: $env" >&2; exit 2 ;;
esac

correlation_id=$(uuidgen)

echo "{\"phase\":\"synthetic-inject-start\",\"env\":\"${env}\",\"bad_tag\":\"${bad_tag}\",\"correlation_id\":\"${correlation_id}\"}"

# Emit the synthetic-injection audit row first.
payload=$(jq -nc \
  --arg purpose "deploy-failure" \
  --arg expected_alert "alert-deploy-failure-${env}" \
  --arg bad_tag "$bad_tag" \
  '{purpose:$purpose,expected_alert:$expected_alert,bad_tag:$bad_tag}')
bash "$(dirname "${BASH_SOURCE[0]}")/../audit-emit.sh" \
  --env "$env" \
  --event-type synthetic.injection \
  --payload "$payload" \
  --correlation-id "$correlation_id"

# Trigger the deploy workflow via gh CLI with the bad tag. The workflow will fail at
# run-migrations-job.sh (the image won't be pullable) and emit deploy.completed_failed.
gh workflow run deploy-${env}.yml \
  -f image_tag="$bad_tag" \
  -f skip_migrations=false \
  -f skip_seed_apply=true \
  --ref main

echo "{\"phase\":\"synthetic-inject-triggered\",\"env\":\"${env}\",\"bad_tag\":\"${bad_tag}\",\"expected_alert\":\"alert-deploy-failure-${env}\",\"expected_within_seconds\":120}"
exit 0
