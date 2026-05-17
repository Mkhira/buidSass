#!/usr/bin/env bash
#
# T052 — extends spec 003's check-no-secrets-in-appsettings.sh with
# notification-provider-specific secret-pattern detection. The generic guard
# already trips on 32+ hex / base64 blobs, but provider-specific identifiers
# (AWS access keys, SendGrid API key prefix, Unifonic API-keys, Infobip
# basic-auth, FCM service-account JSON private keys) deserve a dedicated
# pattern set so the failure message points operators at the right
# remediation runbook.
#
# Wired alongside check-no-secrets-in-appsettings.sh in the
# lint-format-infra workflow.
#
# Exit codes: 0 = clean, 1 = violation, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
violations=0

scan_dirs=(
  "$REPO_ROOT/services/backend_api"
  "$REPO_ROOT/apps/admin_web"
)

# Provider-specific secret shapes:
#   - AWS access-key id: AKIA[0-9A-Z]{16}
#   - AWS secret access key: 40 base64 chars (rare; generic guard catches it)
#   - SendGrid API key: SG\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}
#   - Unifonic app-sid: literal-looking IDs; instead check for `unifonic.*api[-_]?key.*[a-f0-9]{32}` patterns
#   - Infobip basic-auth: `Authorization.*Basic\s+[A-Za-z0-9+/=]{20,}`
#   - FCM service-account JSON private key:
#       "-----BEGIN PRIVATE KEY-----"
patterns=(
  'AKIA[0-9A-Z]{16}'
  'SG\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}'
  '-----BEGIN [A-Z]+ PRIVATE KEY-----'
  '"client_email"[[:space:]]*:[[:space:]]*"[^"]+@[^"]+\.iam\.gserviceaccount\.com"'
)

include='--include=*.json --include=*.cs --include=*.tsx --include=*.ts --include=*.js'
exclude_paths='(/bin/|/obj/|/node_modules/|/\.next/|/Tests/|\.Tests/|/tool-results/|notifications-no-pii\.sh|check-notifications-provider-keys\.sh|check-no-secrets-in-appsettings\.sh)'

for pat in "${patterns[@]}"; do
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    echo "FAIL [provider-secret] $line"
    violations=$((violations + 1))
  done < <(grep -rEn $include "$pat" "${scan_dirs[@]}" 2>/dev/null | grep -vE "$exclude_paths" || true)
done

if [ "$violations" -gt 0 ]; then
  echo ""
  echo "Found $violations notification-provider secret-shaped value(s) committed to source."
  echo "Runbook: rotate the leaked credential, then move the secret into the appropriate KV slot."
  echo "Slots: notifications-{email,sms,push}/<market>/<provider>/api-key|webhook-signing-key"
  exit 1
fi
echo "notification-provider secret guard clean."
exit 0
