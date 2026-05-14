#!/usr/bin/env bash
#
# E1 Phase 2 (T030). Idempotent provisioning of the four federated credentials that
# back the GitHub Actions → Azure OIDC handshake (AC-6).
#
# Creates four credentials per environment:
#   gha-deploy-stg    subject=repo:<org>/<repo>:environment:staging
#   gha-deploy-prd    subject=repo:<org>/<repo>:environment:production
#   gha-drift-stg     subject=repo:<org>/<repo>:ref:refs/heads/main
#   gha-drift-prd     subject=repo:<org>/<repo>:ref:refs/heads/main
#
# Audience: api://AzureADTokenExchange (Azure's standard for OIDC token exchange).
# Each credential attaches to a per-environment user-assigned managed identity
# (id-gha-deploy-<env> / id-gha-drift-<env>) that the workflow logs into via
# azure/login@v2.
#
# This script is NEVER auto-executed by CI. A human operator runs it once per
# environment from a workstation with PIM-elevated permissions. The `--dry-run`
# flag prints what would be created without making any API call.

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: setup-federated-credentials.sh [--dry-run]

Required environment variables:
  AZURE_TENANT_ID        AAD tenant id (e.g. 11111111-...).
  AZURE_SUBSCRIPTION_ID  Subscription id where the managed identities live.
  GITHUB_ORG             e.g. dental-platform
  GITHUB_REPO            e.g. dental-platform-monorepo
  AAD_GHA_DEPLOY_STG_OID Object id of id-gha-deploy-stg managed identity.
  AAD_GHA_DEPLOY_PRD_OID Object id of id-gha-deploy-prd managed identity.
  AAD_GHA_DRIFT_STG_OID  Object id of id-gha-drift-stg managed identity.
  AAD_GHA_DRIFT_PRD_OID  Object id of id-gha-drift-prd managed identity.

Optional flags:
  --dry-run              Print the subject claims and `az identity federated-credential`
                         invocations without executing them.
USAGE
}

dry_run=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) dry_run=1 ;;
    --help|-h) usage; exit 0 ;;
    *) echo "Unknown flag: $arg" >&2; usage; exit 2 ;;
  esac
done

required_vars=(
  AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID GITHUB_ORG GITHUB_REPO
  AAD_GHA_DEPLOY_STG_OID AAD_GHA_DEPLOY_PRD_OID
  AAD_GHA_DRIFT_STG_OID AAD_GHA_DRIFT_PRD_OID
)
for var in "${required_vars[@]}"; do
  if [[ -z "${!var:-}" ]]; then
    echo "missing required env var: $var" >&2
    usage
    exit 2
  fi
done

# Each row: <identity-name> <subject> <description>
credentials=(
  "id-gha-deploy-stg|repo:${GITHUB_ORG}/${GITHUB_REPO}:environment:staging|gha-deploy-stg federated credential for Staging deploys"
  "id-gha-deploy-prd|repo:${GITHUB_ORG}/${GITHUB_REPO}:environment:production|gha-deploy-prd federated credential for Production deploys"
  "id-gha-drift-stg|repo:${GITHUB_ORG}/${GITHUB_REPO}:ref:refs/heads/main|gha-drift-stg federated credential for nightly drift detection (Staging)"
  "id-gha-drift-prd|repo:${GITHUB_ORG}/${GITHUB_REPO}:ref:refs/heads/main|gha-drift-prd federated credential for nightly drift detection (Production)"
)

# Map identity-name → object id (already validated above).
declare -A identity_oid_map=(
  ["id-gha-deploy-stg"]="$AAD_GHA_DEPLOY_STG_OID"
  ["id-gha-deploy-prd"]="$AAD_GHA_DEPLOY_PRD_OID"
  ["id-gha-drift-stg"]="$AAD_GHA_DRIFT_STG_OID"
  ["id-gha-drift-prd"]="$AAD_GHA_DRIFT_PRD_OID"
)

for row in "${credentials[@]}"; do
  IFS='|' read -r identity subject description <<< "$row"
  oid="${identity_oid_map[$identity]}"
  credential_name="fc-${identity}-$(echo -n "$subject" | shasum | head -c8)"
  cmd=(
    az identity federated-credential create
    --name "$credential_name"
    --identity-name "$identity"
    --resource-group "rg-identity-shared"
    --issuer "https://token.actions.githubusercontent.com"
    --subject "$subject"
    --audiences "api://AzureADTokenExchange"
  )
  echo "=== $identity ($oid) ==="
  echo "Subject: $subject"
  echo "Description: $description"
  if [[ "$dry_run" -eq 1 ]]; then
    printf '  DRY-RUN would execute: %s\n' "${cmd[*]}"
  else
    if "${cmd[@]}" 2>/dev/null; then
      echo "  created: $credential_name"
    else
      # Idempotent: an existing credential is a success, not an error.
      if az identity federated-credential show \
          --name "$credential_name" \
          --identity-name "$identity" \
          --resource-group "rg-identity-shared" >/dev/null 2>&1; then
        echo "  already exists: $credential_name"
      else
        echo "  FAILED to create federated credential — check PIM elevation + RG existence." >&2
        exit 1
      fi
    fi
  fi
done

if [[ "$dry_run" -eq 1 ]]; then
  echo
  echo "setup-federated-credentials: DRY RUN complete. 0 changes made."
else
  echo
  echo "setup-federated-credentials: 4 credentials provisioned (or already present)."
fi
