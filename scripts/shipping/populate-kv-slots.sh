#!/usr/bin/env bash
#
# Spec 026 (T009). Wires the four provider API keys for the Shipping module
# into the target Key Vault and emits one `secret.placeholder_replaced` audit
# event per slot, satisfying AC-3.
#
# Per `infra/azure/HANDOFF-FOR-025-026-027.md` §2.1, downstream specs OWN the
# placeholder replacement; E1 only provisions the `tbd-by-026` sentinels.
#
# Slots to populate (canonical logical-path → on-disk KV name uses `--`):
#   shipping/sa/smsa/api-key        (SMSA Express — KSA primary)
#   shipping/sa/aramex-ksa/api-key  (Aramex KSA — KSA backup)
#   shipping/eg/bosta/api-key       (Bosta — EG primary)
#   shipping/eg/aramex-eg/api-key   (Aramex EG — EG backup)
#
# Before this script runs, the corresponding `shipping/<market>/tbd-by-026/api-key`
# placeholders should exist in the vault (provisioned by `infra/azure/keyvault-bootstrap.bicep`).
# This script deletes the sentinel (per §2.1 step 2) BEFORE creating the
# provider-named secret, then emits a `secret.placeholder_replaced` event
# whose payload identifies the new secret name + the spec that authored it.
#
# Usage:
#   bash scripts/shipping/populate-kv-slots.sh \
#     --env staging \
#     --vault kv-dental-stg \
#     --smsa-key-file /secure/path/smsa.key \
#     --aramex-ksa-key-file /secure/path/aramex-ksa.key \
#     --bosta-key-file /secure/path/bosta.key \
#     --aramex-eg-key-file /secure/path/aramex-eg.key
#
# Flags:
#   --skip-vault-write  Skip the `az keyvault secret set` step (dry-run; still
#                       emits the audit events). Useful for local rehearsal.
#   --skip-audit-emit   Skip the audit-emit step (vault-only repair runs).
#
# Exit codes: 0 = success, 1 = usage / pre-flight error, 2 = vault op failed,
# 3 = audit-emit failed (vault writes still happened — manual reconciliation needed).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AUDIT_EMIT="${REPO_ROOT}/scripts/azure/audit-emit.sh"

env_short=""
vault_name=""
smsa_key_file=""
aramex_ksa_key_file=""
bosta_key_file=""
aramex_eg_key_file=""
skip_vault_write=0
skip_audit_emit=0

print_usage() {
  sed -n '2,40p' "${BASH_SOURCE[0]}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) env_short="$2"; shift 2 ;;
    --vault) vault_name="$2"; shift 2 ;;
    --smsa-key-file) smsa_key_file="$2"; shift 2 ;;
    --aramex-ksa-key-file) aramex_ksa_key_file="$2"; shift 2 ;;
    --bosta-key-file) bosta_key_file="$2"; shift 2 ;;
    --aramex-eg-key-file) aramex_eg_key_file="$2"; shift 2 ;;
    --skip-vault-write) skip_vault_write=1; shift ;;
    --skip-audit-emit) skip_audit_emit=1; shift ;;
    -h|--help) print_usage; exit 0 ;;
    *) echo "unknown flag: $1" >&2; print_usage; exit 1 ;;
  esac
done

if [[ -z "$env_short" || -z "$vault_name" ]]; then
  echo "error: --env and --vault are required" >&2
  print_usage
  exit 1
fi

case "$env_short" in
  stg|prd) ;;
  *) echo "error: --env must be 'stg' or 'prd' (got '$env_short')" >&2; exit 1 ;;
esac

# Verify every key file exists and is non-empty BEFORE touching the vault.
for var in smsa_key_file aramex_ksa_key_file bosta_key_file aramex_eg_key_file; do
  path="${!var}"
  if [[ -z "$path" ]]; then
    echo "error: --${var//_/-} flag missing" >&2
    exit 1
  fi
  if [[ ! -s "$path" ]]; then
    echo "error: key file '$path' missing or empty" >&2
    exit 1
  fi
done

# Canonical logical-path → on-disk KV name conversion (per E1 data-model.md §2).
# Logical: shipping/<market>/<provider>/api-key
# On-disk: shipping--<market>--<provider>--api-key
slot_logical() { printf '%s' "shipping/$1/$2/api-key"; }
slot_ondisk() { printf '%s' "shipping--$1--$2--api-key"; }

# Single source-of-truth slot list: (market, provider, key-file-var-name).
slots=(
  "sa smsa smsa_key_file"
  "sa aramex-ksa aramex_ksa_key_file"
  "eg bosta bosta_key_file"
  "eg aramex-eg aramex_eg_key_file"
)

declare -i vault_written=0
declare -i audit_emitted=0

now_iso() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
rotation_ts="$(now_iso)"

for entry in "${slots[@]}"; do
  read -r market provider key_var <<< "$entry"
  logical="$(slot_logical "$market" "$provider")"
  ondisk="$(slot_ondisk "$market" "$provider")"
  key_path="${!key_var}"

  echo "==> ${logical}  (file: ${ondisk}, env: ${env_short})"

  if [[ $skip_vault_write -eq 0 ]]; then
    if ! command -v az >/dev/null 2>&1; then
      echo "error: az CLI not available" >&2
      exit 2
    fi
    # Step 1 (per §2.1) — delete the placeholder if it exists. Idempotent.
    tbd_ondisk="shipping--${market}--tbd-by-026--api-key"
    if az keyvault secret show --vault-name "$vault_name" --name "$tbd_ondisk" >/dev/null 2>&1; then
      echo "  - deleting placeholder ${tbd_ondisk}"
      az keyvault secret delete --vault-name "$vault_name" --name "$tbd_ondisk" --output none
    else
      echo "  - placeholder ${tbd_ondisk} not present (already populated or extra slot)"
    fi
    # Step 3 — create the provider-named secret with required tags.
    echo "  - writing ${ondisk}"
    az keyvault secret set \
      --vault-name "$vault_name" \
      --name "$ondisk" \
      --file "$key_path" \
      --tags \
        set_by_spec=026 \
        rotated_at="$rotation_ts" \
        rotation_cadence_days=90 \
        logical_path="$logical" \
        environment="$env_short" \
      --output none
    vault_written+=1
  else
    echo "  - (skip-vault-write) would write ${ondisk}"
  fi

  if [[ $skip_audit_emit -eq 0 ]]; then
    # Step 4 — emit secret.placeholder_replaced via the shared audit-emit wrapper.
    if [[ ! -x "$AUDIT_EMIT" ]]; then
      echo "error: audit-emit script ${AUDIT_EMIT} is missing or not executable" >&2
      exit 3
    fi
    payload="$(cat <<EOF
{"vault_name":"${vault_name}","secret_name":"${ondisk}","logical_path":"${logical}","replaced_by_spec":"026","environment":"${env_short}","rotated_at":"${rotation_ts}"}
EOF
)"
    if ! "$AUDIT_EMIT" \
        --env "$env_short" \
        --event-type secret.placeholder_replaced \
        --payload "$payload"; then
      echo "error: audit-emit failed for ${ondisk}" >&2
      exit 3
    fi
    audit_emitted+=1
  else
    echo "  - (skip-audit-emit) would emit secret.placeholder_replaced"
  fi
done

echo
echo "populate-kv-slots: vault writes=${vault_written}, audit events=${audit_emitted}"
exit 0
