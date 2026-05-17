#!/usr/bin/env bash
#
# Spec 025 (T011). Populates the 7 KV slots required by the Notifications
# module and emits one `secret.placeholder_replaced` audit event per slot,
# satisfying AC-3.
#
# Per `infra/azure/HANDOFF-FOR-025-026-027.md`, downstream specs OWN the
# placeholder replacement; E1 only provisioned the `tbd-by-025` sentinels:
#   notifications-email/multi/tbd-by-025/api-key
#   notifications-sms/sa/tbd-by-025/api-key
#   notifications-sms/eg/tbd-by-025/api-key
#   notifications-push/multi/tbd-by-025/service-account-json
#
# Spec 025 expands the placeholder set to seven concrete provider slots:
#   notifications-email/multi/ses/api-key                  (SES primary)
#   notifications-email/multi/sendgrid/api-key             (SendGrid backup)
#   notifications-sms/sa/unifonic/api-key                  (Unifonic KSA primary)
#   notifications-sms/sa/infobip/api-key                   (Infobip KSA backup)
#   notifications-sms/eg/vodafone-egypt/api-key            (Vodafone Egypt primary)
#   notifications-sms/eg/infobip/api-key                   (Infobip EG backup)
#   notifications-push/multi/fcm/service-account-json      (FCM)
#
# Usage:
#   bash scripts/notifications/populate-kv-slots.sh \
#     --env stg --vault kv-dental-stg \
#     --ses-api-key-file        /secure/path/ses.key \
#     --sendgrid-api-key-file   /secure/path/sendgrid.key \
#     --unifonic-api-key-file   /secure/path/unifonic.key \
#     --infobip-sa-api-key-file /secure/path/infobip-sa.key \
#     --vodafone-egypt-api-key-file /secure/path/vodafone-eg.key \
#     --infobip-eg-api-key-file /secure/path/infobip-eg.key \
#     --fcm-service-account-file /secure/path/fcm-sa.json
#
# Flags:
#   --dry-run            Skip both the vault write and the audit-emit
#                        step (no audit row without a real transition —
#                        AC-3 traceability). Useful for pre-flight only.
#   --skip-vault-write   Same as --dry-run on the vault side (kept for
#                        symmetry with scripts/payments/populate-kv-slots.sh).
#   --skip-audit-emit    Vault-only repair: write secrets without emitting
#                        the audit events.
#
# Idempotent: re-running with the same key files leaves the vault rows
# unchanged (az keyvault secret set overwrites with identical content;
# `tbd-by-025` placeholders are deleted only on the first run).
#
# Exit codes: 0 success, 1 usage/pre-flight, 2 vault op failed, 3 audit failed.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
AUDIT_EMIT="${REPO_ROOT}/scripts/azure/audit-emit.sh"

env_short=""
vault_name=""
dry_run=0
skip_vault_write=0
skip_audit_emit=0

# (logical-path-suffix, on-disk-name-suffix) tuples, in canonical order.
# Each suffix is appended after the channel prefix (notifications-email |
# notifications-sms | notifications-push).
declare -a slots=(
  "notifications-email/multi/ses/api-key|notifications-email--multi--ses--api-key|notifications-email--multi--tbd-by-025--api-key"
  "notifications-email/multi/sendgrid/api-key|notifications-email--multi--sendgrid--api-key|"
  "notifications-sms/sa/unifonic/api-key|notifications-sms--sa--unifonic--api-key|notifications-sms--sa--tbd-by-025--api-key"
  "notifications-sms/sa/infobip/api-key|notifications-sms--sa--infobip--api-key|"
  "notifications-sms/eg/vodafone-egypt/api-key|notifications-sms--eg--vodafone-egypt--api-key|notifications-sms--eg--tbd-by-025--api-key"
  "notifications-sms/eg/infobip/api-key|notifications-sms--eg--infobip--api-key|"
  "notifications-push/multi/fcm/service-account-json|notifications-push--multi--fcm--service-account-json|notifications-push--multi--tbd-by-025--service-account-json"
)

# Slot file paths populate `slot_files[logical-suffix]`.
declare -A slot_files=()

print_usage() {
  sed -n '2,55p' "${BASH_SOURCE[0]}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) env_short="$2"; shift 2 ;;
    --vault) vault_name="$2"; shift 2 ;;
    --dry-run) dry_run=1; shift ;;
    --skip-vault-write) skip_vault_write=1; shift ;;
    --skip-audit-emit) skip_audit_emit=1; shift ;;
    --ses-api-key-file) slot_files["notifications-email/multi/ses/api-key"]="$2"; shift 2 ;;
    --sendgrid-api-key-file) slot_files["notifications-email/multi/sendgrid/api-key"]="$2"; shift 2 ;;
    --unifonic-api-key-file) slot_files["notifications-sms/sa/unifonic/api-key"]="$2"; shift 2 ;;
    --infobip-sa-api-key-file) slot_files["notifications-sms/sa/infobip/api-key"]="$2"; shift 2 ;;
    --vodafone-egypt-api-key-file) slot_files["notifications-sms/eg/vodafone-egypt/api-key"]="$2"; shift 2 ;;
    --infobip-eg-api-key-file) slot_files["notifications-sms/eg/infobip/api-key"]="$2"; shift 2 ;;
    --fcm-service-account-file) slot_files["notifications-push/multi/fcm/service-account-json"]="$2"; shift 2 ;;
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

# In dry-run mode, both vault and audit are skipped.
if [[ $dry_run -eq 1 ]]; then
  skip_vault_write=1
  skip_audit_emit=1
fi

# Pre-flight: every slot must have a matching key file (unless we're in dry-run).
missing=0
for entry in "${slots[@]}"; do
  IFS='|' read -r logical_suffix _ondisk _tbd <<<"$entry"
  path="${slot_files[$logical_suffix]:-}"
  if [[ -z "$path" ]]; then
    echo "error: missing key file for slot ${logical_suffix}" >&2
    missing=$((missing + 1))
    continue
  fi
  if [[ $dry_run -eq 0 && ! -s "$path" ]]; then
    echo "error: key file '$path' for slot ${logical_suffix} missing or empty" >&2
    missing=$((missing + 1))
  fi
done
if [[ $missing -gt 0 ]]; then
  echo "error: ${missing} pre-flight failures — aborting" >&2
  exit 1
fi

now_iso() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
rotation_ts="$(now_iso)"

declare -i vault_written=0
declare -i audit_emitted=0
declare -i placeholders_deleted=0

for entry in "${slots[@]}"; do
  IFS='|' read -r logical_suffix ondisk tbd_ondisk <<<"$entry"
  key_path="${slot_files[$logical_suffix]}"

  echo "==> ${logical_suffix}  (file: ${ondisk}, env: ${env_short})"

  if [[ $skip_vault_write -eq 0 ]]; then
    if ! command -v az >/dev/null 2>&1; then
      echo "error: az CLI not available" >&2
      exit 2
    fi
    # Delete the per-channel placeholder if it exists. Idempotent.
    if [[ -n "$tbd_ondisk" ]]; then
      if az keyvault secret show --vault-name "$vault_name" --name "$tbd_ondisk" >/dev/null 2>&1; then
        echo "  - deleting placeholder ${tbd_ondisk}"
        az keyvault secret delete --vault-name "$vault_name" --name "$tbd_ondisk" --output none
        placeholders_deleted=$((placeholders_deleted + 1))
      else
        echo "  - placeholder ${tbd_ondisk} not present (already populated or extra slot)"
      fi
    fi
    echo "  - writing ${ondisk}"
    az keyvault secret set \
      --vault-name "$vault_name" \
      --name "$ondisk" \
      --file "$key_path" \
      --tags \
        set_by_spec=025 \
        rotated_at="$rotation_ts" \
        rotation_cadence_days=90 \
        logical_path="$logical_suffix" \
        environment="$env_short" \
      --output none
    vault_written=$((vault_written + 1))
  else
    echo "  - (dry-run / skip-vault-write) would write ${ondisk}"
    continue
  fi

  if [[ $skip_audit_emit -eq 0 ]]; then
    if [[ ! -x "$AUDIT_EMIT" ]]; then
      echo "error: audit-emit script ${AUDIT_EMIT} is missing or not executable" >&2
      exit 3
    fi
    payload="$(cat <<EOF
{"vault_name":"${vault_name}","secret_name":"${ondisk}","logical_path":"${logical_suffix}","replaced_by_spec":"025","environment":"${env_short}","rotated_at":"${rotation_ts}"}
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

echo
echo "populate-kv-slots: vault writes=${vault_written}, placeholders deleted=${placeholders_deleted}, audit events=${audit_emitted}"
exit 0
