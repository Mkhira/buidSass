#!/usr/bin/env bash
#
# E1 Phase 0 (T009). Wrapper that translates workflow-friendly flags into a
# `dotnet AdminTool.dll audit-emit` invocation, executed inside the running
# `ca-backend-api-<env>` container via `az containerapp exec`. This keeps the
# audit-emit path co-located with the same database connection / managed identity
# the backend already uses (so the IMDS lookup yields the runtime MI's object id
# rather than the deploy principal's).
#
# Usage:
#   scripts/azure/audit-emit.sh \
#     --env staging \
#     --event-type deploy.attempted \
#     --payload '{"github_run_id":"123","commit_sha":"abc","environment":"staging"}' \
#     --correlation-id <uuid>
#
#   scripts/azure/audit-emit.sh --skip-audit-emit ...   # bootstrap no-op
#
# Exit code mirrors the underlying CLI. The `--skip-audit-emit` flag is the
# bootstrap escape hatch used ONLY before the first successful deploy (no backend
# container exists yet to exec into); documented in runbook.

set -euo pipefail

env=""
event_type=""
payload="{}"
correlation_id=""
skip=0
app_name="ca-backend-api"
resource_group=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env)            env="$2"; shift 2 ;;
    --env=*)          env="${1#*=}"; shift ;;
    --event-type)     event_type="$2"; shift 2 ;;
    --event-type=*)   event_type="${1#*=}"; shift ;;
    --payload)        payload="$2"; shift 2 ;;
    --payload=*)      payload="${1#*=}"; shift ;;
    --correlation-id) correlation_id="$2"; shift 2 ;;
    --correlation-id=*) correlation_id="${1#*=}"; shift ;;
    --skip-audit-emit) skip=1; shift ;;
    --app-name)       app_name="$2"; shift 2 ;;
    --resource-group) resource_group="$2"; shift 2 ;;
    --help|-h)
      grep '^#' "$0" | sed 's/^# \?//' | head -40
      exit 0 ;;
    *)
      echo "audit-emit.sh: unknown flag '$1'" >&2
      exit 2 ;;
  esac
done

if [[ "$skip" -eq 1 ]]; then
  echo "audit-emit.sh: --skip-audit-emit set; recording a stdout marker only."
  printf '{"audit_emit":"skipped","reason":"bootstrap","event_type":"%s"}\n' "$event_type"
  exit 0
fi

if [[ -z "$env" ]]; then
  echo "audit-emit.sh: --env is required" >&2
  exit 2
fi
if [[ -z "$event_type" ]]; then
  echo "audit-emit.sh: --event-type is required" >&2
  exit 2
fi
if [[ -z "$correlation_id" ]]; then
  # Best-effort UUID v4 generation portable across macOS / Linux.
  if command -v uuidgen >/dev/null 2>&1; then
    correlation_id="$(uuidgen)"
  else
    correlation_id="$(cat /proc/sys/kernel/random/uuid 2>/dev/null || true)"
  fi
fi
if [[ -z "$resource_group" ]]; then
  resource_group="rg-dental-${env}-ksa"
fi

# Translate "staging" / "production" → the env suffix used in the container app name.
env_suffix="stg"
[[ "$env" == "production" ]] && env_suffix="prd"
target_app="${app_name}-${env_suffix}"

# Wrap the payload + correlation-id values into the dotnet invocation. The trailing
# `--` separates `az containerapp exec` args from the in-container command.
echo "audit-emit.sh: invoking ${target_app} (rg=${resource_group}, env=${env}, event=${event_type})"

# Note: `az containerapp exec` lacks `--cwd` and runs the command via /bin/sh -c.
# The dotnet entrypoint of the backend image already runs as `dotnet backend_api.dll`,
# so we shell out to a one-shot dotnet invocation that re-uses the same assembly.
# A non-zero exit propagates as our exit.
exec az containerapp exec \
  --resource-group "$resource_group" \
  --name "$target_app" \
  --command "/bin/sh -lc 'dotnet /app/backend_api.dll audit-emit --event-type \"${event_type}\" --payload '"'"'${payload}'"'"' --correlation-id \"${correlation_id}\"'"
