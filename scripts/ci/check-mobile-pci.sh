#!/usr/bin/env bash
#
# Phase 4 T-4.18 — PCI scope guard for the customer Flutter app.
#
# Scope: PCI SAQ-A per ADR-007. The mobile app NEVER touches raw PAN /
# CVV / track-data — provider SDKs collect those in their own hosted
# widgets and hand back an opaque token. The guard scans every Dart
# source file outside `lib/features/checkout/payment_adapters/` for
# any reference to cardholder-shape identifiers and rejects PRs that
# introduce one.
#
# Strategy:
#   1. Scope: apps/customer_flutter/lib AND apps/customer_flutter/test.
#   2. Exclude: lib/features/checkout/payment_adapters/** — the only
#      directory permitted to *receive* a provider token (still no raw
#      cardholder data; only the opaque string).
#   3. Allow comment lines so this file and adapter doc-strings can
#      reference the prohibition.
#
# Exit codes: 0 = clean, 1 = at least one violation.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_DIR="${REPO_ROOT}/apps/customer_flutter"

if [[ ! -d "${APP_DIR}" ]]; then
  echo "check-mobile-pci: no customer_flutter directory, skipping."
  exit 0
fi

# Word-boundary tokens — same shape as the backend guard, lowercase
# variants included for snake_case fields and JSON keys.
forbidden=(
  "cardNumber"
  "card_number"
  "cardPan"
  "primaryAccountNumber"
  "primary_account_number"
  "cvv"
  "cvc"
  "track1"
  "track2"
  "magstripe"
  "cardPin"
  "card_pin"
)

# Lines exempt from the scan: Dart `//` and `///` comments and `/*`
# block comments. Allows this file's neighbors to reference the
# prohibition without tripping the guard.
exempt_regexes=(
  "^[[:space:]]*//"
  "^[[:space:]]*\\*"
  "^[[:space:]]*/\\*"
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

scan_dir() {
  local dir="$1"
  [[ ! -d "$dir" ]] && return 0
  while IFS= read -r -d '' file; do
    # Skip the payment_adapters folder — the one place a provider token
    # variable name can legitimately mention card-shape (e.g. when
    # routing a hosted-fields callback).
    case "$file" in
      *"/payment_adapters/"*) continue ;;
    esac
    files_scanned=$((files_scanned + 1))
    for token in "${forbidden[@]}"; do
      while IFS= read -r match; do
        [[ -z "$match" ]] && continue
        line_no="${match%%:*}"
        line="${match#*:}"
        if is_exempt_line "$line"; then
          continue
        fi
        echo "mobile-pci violation: ${file}:${line_no}: forbidden token '${token}'" >&2
        echo "  > ${line}" >&2
        violations=$((violations + 1))
      done < <(grep -n -i -E "\\b${token}\\b" "$file" 2>/dev/null || true)
    done
  done < <(find "$dir" -type f -name "*.dart" -print0 2>/dev/null)
}

scan_dir "${APP_DIR}/lib"
scan_dir "${APP_DIR}/test"

echo "check-mobile-pci: ${files_scanned} file(s) scanned, ${violations} violation(s)."
if [[ $violations -gt 0 ]]; then
  exit 1
fi
exit 0
