#!/usr/bin/env bash
#
# Spec 027 (T012). Populates the 21 KV slots required by the Payments module
# (7 providers × 3 keys: api-key, api-secret, webhook-signing-key) and emits
# one `secret.placeholder_replaced` audit event per slot, satisfying AC-3.
#
# Per `infra/azure/HANDOFF-FOR-025-026-027.md`, downstream specs OWN the
# placeholder replacement; E1 only provisions the `tbd-by-027` sentinels.
#
# Slots (canonical logical-path → on-disk KV name uses `--`):
#   payments/sa/hyperpay/api-key | api-secret | webhook-signing-key
#   payments/sa/tap/...
#   payments/sa/tabby/...
#   payments/sa/tamara/...
#   payments/eg/paymob/...
#   payments/eg/kashier/...
#   payments/eg/valu/...
#
# Usage:
#   bash scripts/payments/populate-kv-slots.sh \
#     --env stg --vault kv-dental-stg \
#     --hyperpay-api-key-file /secure/path/hyperpay.key \
#     --hyperpay-api-secret-file /secure/path/hyperpay.secret \
#     --hyperpay-webhook-key-file /secure/path/hyperpay.whk \
#     ... (repeat for tap, tabby, tamara, paymob, kashier, valu)
#
# Flags:
#   --skip-vault-write   Dry-run: skip the `az keyvault secret set` step
#                        and the matching audit-emit step (no audit row
#                        without a real transition — AC-3 traceability).
#   --skip-audit-emit    Vault-only repair: write secrets without emitting
#                        the audit events.
#
# Exit codes: 0 success, 1 usage/pre-flight, 2 vault op failed, 3 audit failed.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
AUDIT_EMIT="${REPO_ROOT}/scripts/azure/audit-emit.sh"

env_short=""
vault_name=""
skip_vault_write=0
skip_audit_emit=0

# (market, provider) tuples in canonical order.
providers=(
  "sa hyperpay"
  "sa tap"
  "sa tabby"
  "sa tamara"
  "eg paymob"
  "eg kashier"
  "eg valu"
)
keys=("api-key" "api-secret" "webhook-signing-key")

# Slot file paths populate `key_files[market/provider/key]` and are resolved
# from CLI flags below. Declared with a placeholder so set -u tolerates
# unset lookups.
declare -A key_files=()

print_usage() {
  sed -n '2,40p' "${BASH_SOURCE[0]}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) env_short="$2"; shift 2 ;;
    --vault) vault_name="$2"; shift 2 ;;
    --skip-vault-write) skip_vault_write=1; shift ;;
    --skip-audit-emit) skip_audit_emit=1; shift ;;
    --hyperpay-api-key-file) key_files["sa/hyperpay/api-key"]="$2"; shift 2 ;;
    --hyperpay-api-secret-file) key_files["sa/hyperpay/api-secret"]="$2"; shift 2 ;;
    --hyperpay-webhook-key-file) key_files["sa/hyperpay/webhook-signing-key"]="$2"; shift 2 ;;
    --tap-api-key-file) key_files["sa/tap/api-key"]="$2"; shift 2 ;;
    --tap-api-secret-file) key_files["sa/tap/api-secret"]="$2"; shift 2 ;;
    --tap-webhook-key-file) key_files["sa/tap/webhook-signing-key"]="$2"; shift 2 ;;
    --tabby-api-key-file) key_files["sa/tabby/api-key"]="$2"; shift 2 ;;
    --tabby-api-secret-file) key_files["sa/tabby/api-secret"]="$2"; shift 2 ;;
    --tabby-webhook-key-file) key_files["sa/tabby/webhook-signing-key"]="$2"; shift 2 ;;
    --tamara-api-key-file) key_files["sa/tamara/api-key"]="$2"; shift 2 ;;
    --tamara-api-secret-file) key_files["sa/tamara/api-secret"]="$2"; shift 2 ;;
    --tamara-webhook-key-file) key_files["sa/tamara/webhook-signing-key"]="$2"; shift 2 ;;
    --paymob-api-key-file) key_files["eg/paymob/api-key"]="$2"; shift 2 ;;
    --paymob-api-secret-file) key_files["eg/paymob/api-secret"]="$2"; shift 2 ;;
    --paymob-webhook-key-file) key_files["eg/paymob/webhook-signing-key"]="$2"; shift 2 ;;
    --kashier-api-key-file) key_files["eg/kashier/api-key"]="$2"; shift 2 ;;
    --kashier-api-secret-file) key_files["eg/kashier/api-secret"]="$2"; shift 2 ;;
    --kashier-webhook-key-file) key_files["eg/kashier/webhook-signing-key"]="$2"; shift 2 ;;
    --valu-api-key-file) key_files["eg/valu/api-key"]="$2"; shift 2 ;;
    --valu-api-secret-file) key_files["eg/valu/api-secret"]="$2"; shift 2 ;;
    --valu-webhook-key-file) key_files["eg/valu/webhook-signing-key"]="$2"; shift 2 ;;
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

# Pre-flight: verify every required key file exists and is non-empty.
missing=0
for entry in "${providers[@]}"; do
  read -r market provider <<< "$entry"
  for key in "${keys[@]}"; do
    slot="${market}/${provider}/${key}"
    path="${key_files[$slot]:-}"
    if [[ -z "$path" ]]; then
      # Map canonical KV key → actual CLI flag suffix:
      # `webhook-signing-key` is shortened to `webhook-key` in the flag form
      # (matches the CLI declarations above); api-key / api-secret pass through.
      flag_key="$key"
      [[ "$key" == "webhook-signing-key" ]] && flag_key="webhook-key"
      echo "error: missing --${provider}-${flag_key}-file flag for slot ${slot}" >&2
      missing=$((missing + 1))
      continue
    fi
    if [[ ! -s "$path" ]]; then
      echo "error: key file '$path' for slot ${slot} missing or empty" >&2
      missing=$((missing + 1))
    fi
  done
done
if [[ $missing -gt 0 ]]; then
  echo "error: ${missing} pre-flight failures — aborting" >&2
  exit 1
fi

now_iso() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
rotation_ts="$(now_iso)"

declare -i vault_written=0
declare -i audit_emitted=0

for entry in "${providers[@]}"; do
  read -r market provider <<< "$entry"
  for key in "${keys[@]}"; do
    logical="payments/${market}/${provider}/${key}"
    ondisk="payments--${market}--${provider}--${key}"
    key_path="${key_files[${market}/${provider}/${key}]}"

    echo "==> ${logical}  (file: ${ondisk}, env: ${env_short})"

    if [[ $skip_vault_write -eq 0 ]]; then
      if ! command -v az >/dev/null 2>&1; then
        echo "error: az CLI not available" >&2
        exit 2
      fi
      # Delete the per-market placeholder if it exists. Idempotent.
      tbd_ondisk="payments--${market}--tbd-by-027--${key}"
      if az keyvault secret show --vault-name "$vault_name" --name "$tbd_ondisk" >/dev/null 2>&1; then
        echo "  - deleting placeholder ${tbd_ondisk}"
        az keyvault secret delete --vault-name "$vault_name" --name "$tbd_ondisk" --output none
      else
        echo "  - placeholder ${tbd_ondisk} not present (already populated or extra slot)"
      fi
      echo "  - writing ${ondisk}"
      az keyvault secret set \
        --vault-name "$vault_name" \
        --name "$ondisk" \
        --file "$key_path" \
        --tags \
          set_by_spec=027 \
          rotated_at="$rotation_ts" \
          rotation_cadence_days=90 \
          logical_path="$logical" \
          environment="$env_short" \
        --output none
      vault_written=$((vault_written + 1))
    else
      echo "  - (skip-vault-write) would write ${ondisk}"
      continue
    fi

    if [[ $skip_audit_emit -eq 0 ]]; then
      if [[ ! -x "$AUDIT_EMIT" ]]; then
        echo "error: audit-emit script ${AUDIT_EMIT} is missing or not executable" >&2
        exit 3
      fi
      payload="$(cat <<EOF
{"vault_name":"${vault_name}","secret_name":"${ondisk}","logical_path":"${logical}","replaced_by_spec":"027","environment":"${env_short}","rotated_at":"${rotation_ts}"}
EOF
)"
      if ! "$AUDIT_EMIT" \
          --env "$env_short" \
          --event-type secret.placeholder_replaced \
          --payload "$payload"; then
        echo "error: audit-emit failed for ${ondisk}" >&2
        exit 3
      fi
      audit_emitted=$((audit_emitted + 1))
    fi
  done
done

echo
echo "populate-kv-slots: vault writes=${vault_written}, audit events=${audit_emitted}"
exit 0
