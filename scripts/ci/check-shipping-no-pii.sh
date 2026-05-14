#!/usr/bin/env bash
#
# Spec 026 (T050). PII guard for the Shipping module — scans the provider
# integration code paths for forbidden field names that would leak personal
# data to provider APIs at egress. BR-15 enforces minimum-sufficient payload:
# recipient name (redacted to first + last initial), phone masked to last-4,
# and address. Anything else (national_id, DOB, loyalty data, raw phone, full
# legal name, email) MUST be stripped before the request leaves the runtime.
#
# Strategy:
#  1. Scope: `services/backend_api/Modules/Shipping/Providers/**/*.cs` (the
#     only code path that actually performs egress to provider APIs).
#  2. Reject any direct reference to forbidden field names. These tokens are
#     never legitimate inside the providers folder; they would only appear
#     if a developer accidentally re-introduced the un-redacted ship-to.
#  3. Allowlist the keywords as documented constants when used inside test
#     fixtures or comments (heuristic: skip lines starting with `//` or
#     containing only string literals matching a comment-shape token).
#
# Exit codes: 0 = clean, 1 = at least one violation, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCAN_DIR="${REPO_ROOT}/services/backend_api/Modules/Shipping/Providers"

if [[ ! -d "$SCAN_DIR" ]]; then
  echo "check-shipping-no-pii: ${SCAN_DIR} not found — nothing to scan (Shipping module not present yet)." >&2
  exit 0
fi

# Forbidden tokens that would indicate a PII leak at provider egress. The list
# is INTENTIONALLY narrow: redacted variants (RecipientNameRedacted,
# RecipientPhoneMaskedLast4, AddressMinimized) are allowed; their raw cousins
# are not.
forbidden=(
  "RecipientNameFull"
  "RecipientPhoneRaw"
  "NationalId"
  "DateOfBirth"
  "LoyaltyId"
  "RecipientEmail"
  "PassportNumber"
)

violations=0
files_scanned=0

# Files allowed to mention these tokens (NONE inside Providers/). The
# redactor lives outside Providers/.
while IFS= read -r -d '' file; do
  files_scanned=$((files_scanned + 1))
  for token in "${forbidden[@]}"; do
    # Skip comment-only lines. Use grep -n with --include only on .cs files.
    while IFS= read -r match; do
      [[ -z "$match" ]] && continue
      line_no="${match%%:*}"
      line="${match#*:}"
      # Strip leading whitespace, then skip line comments / xmldoc.
      trimmed="$(printf '%s' "$line" | sed -E 's/^[[:space:]]+//')"
      case "$trimmed" in
        //*|"/*"*|"*"*) continue ;;
      esac
      echo "shipping-pii violation: ${file}:${line_no}: forbidden token '${token}' in provider code" >&2
      violations=$((violations + 1))
    done < <(grep -n "$token" "$file" 2>/dev/null || true)
  done
done < <(find "$SCAN_DIR" -type f -name "*.cs" -print0)

echo "check-shipping-no-pii: ${files_scanned} file(s) scanned, ${violations} violation(s)."
if [[ $violations -gt 0 ]]; then
  exit 1
fi
exit 0
