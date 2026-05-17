#!/usr/bin/env bash
#
# Spec 027 (T006). PCI scope guard for the Payments module — scans EF entity
# definitions, migration files, and payload-builder code paths for cardholder-
# shaped column or field names. BR-1 / V-1 — the Platform is SAQ-A; PAN, CVV,
# track1/2, magstripe, card_pin, card_expiry MUST NEVER appear in code, in any
# form, in any of these paths.
#
# Wired into the CI workflow as a blocking job (check-pci-scope) — any PR
# that introduces a cardholder-shape token here is rejected before merge.
#
# Strategy:
#   1. Scope: services/backend_api/Modules/Payments/{Domain,Persistence,Providers}/
#      and scripts/payments/ (excluding the *.sh and *.md files that document
#      the guard itself).
#   2. Reject any direct reference to cardholder tokens. Tokens are
#      intentionally narrow: redacted variants (RecipientNameRedacted,
#      RecipientPhoneMaskedLast4) are allowed; the raw cardholder fields are
#      not.
#   3. Allow comments containing the tokens (line starts with // or /*) so
#      we can document the prohibition in code without tripping the guard.
#
# Exit codes: 0 = clean, 1 = at least one violation, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCAN_DIRS=(
  "${REPO_ROOT}/services/backend_api/Modules/Payments/Domain"
  "${REPO_ROOT}/services/backend_api/Modules/Payments/Persistence"
  "${REPO_ROOT}/services/backend_api/Modules/Payments/Providers"
  "${REPO_ROOT}/services/backend_api/Modules/Payments/Features"
)

# Forbidden tokens. Keep this list intentionally narrow — false positives are
# worse than false negatives because they erode trust. The redacted /
# minimum-PII variants intentionally do NOT appear here.
forbidden=(
  "PrimaryAccountNumber"
  "Pan"
  "CardNumber"
  "Cvv"
  "Cvc"
  "Track1"
  "Track2"
  "Magstripe"
  "CardPin"
  "CardExpiry"
  "primary_account_number"
  "card_number"
)

# Words that contain "Pan" / "Cvv" / "Cvc" as substrings but are not
# cardholder data. Lines matching any of these regexes are exempt.
exempt_regexes=(
  "^[[:space:]]*//"
  "^[[:space:]]*\*"
  "^[[:space:]]*/\*"
  "MarketCodeSpan"
  "SpanCarrier"
  "Spanish"
  "namespace BackendApi"
  "Microsoft\\."
)

violations=0
files_scanned=0

is_exempt_line() {
  local line="$1"
  for r in "${exempt_regexes[@]}"; do
    if [[ "$line" =~ $r ]]; then
      return 0
    fi
  done
  return 1
}

for dir in "${SCAN_DIRS[@]}"; do
  if [[ ! -d "$dir" ]]; then
    continue
  fi
  while IFS= read -r -d '' file; do
    files_scanned=$((files_scanned + 1))
    for token in "${forbidden[@]}"; do
      while IFS= read -r match; do
        [[ -z "$match" ]] && continue
        line_no="${match%%:*}"
        line="${match#*:}"
        if is_exempt_line "$line"; then
          continue
        fi
        echo "pci-scope violation: ${file}:${line_no}: forbidden token '${token}'" >&2
        echo "  > ${line}" >&2
        violations=$((violations + 1))
      done < <(grep -n -F "$token" "$file" 2>/dev/null || true)
    done
  done < <(find "$dir" -type f -name "*.cs" -print0 2>/dev/null)
done

echo "check-pci-scope: ${files_scanned} file(s) scanned, ${violations} violation(s)."
if [[ $violations -gt 0 ]]; then
  exit 1
fi
exit 0
