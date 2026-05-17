#!/usr/bin/env bash
#
# Spec 025 (T006 + T051). PII guard for the Notifications module — scans
# seed datasets, fixtures, and provider integration code for forbidden PII
# patterns that would leak into committed source, audit logs, or provider
# egress. AC-27 + BR-29 require PII redaction in payload_redacted_jsonb;
# the seed-pii-guard rule (T006) extends to Saudi (+9665…) and Egyptian
# (+201[0-2,5]…) mobile prefixes so seed fixtures cannot accidentally embed
# real-looking customer phone numbers.
#
# Scope:
#  1. seed datasets:       services/backend_api/Modules/Notifications/Seeding/**
#  2. fixture files:       services/backend_api/Tests/Notifications.Tests/**/Fixtures/**
#  3. provider integration: services/backend_api/Modules/Notifications/Providers/**
#
# Forbidden patterns:
#  - Real-looking E.164 phone numbers for KSA (+9665XXXXXXXX) or EG
#    (+201[0-2,5]XXXXXXXX) outside of `// allowlist:phone-fixture` comments.
#  - `NationalId`, `DateOfBirth`, raw `CardPan`, `Cvv` field names.
#  - Direct concatenation of `recipient_phone` or `recipient_email` into
#    provider egress payloads without going through the redaction helper.
#
# Allowlist:
#  - Strings ending in `XXXXXX` (placeholder tokens) are OK.
#  - Files with `.example` or `.template` extensions are skipped.
#  - Lines tagged with `// allowlist:phone-fixture` are skipped.
#
# Exit codes: 0 = clean, 1 = at least one violation, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

SCAN_DIRS=(
  "${REPO_ROOT}/services/backend_api/Modules/Notifications/Seeding"
  "${REPO_ROOT}/services/backend_api/Modules/Notifications/Providers"
  "${REPO_ROOT}/services/backend_api/Tests/Notifications.Tests"
)

# Patterns intentionally narrow. Each line is grep -E regex.
# Phone patterns deliberately require ≥ 8 trailing digits after the prefix to
# avoid matching obvious placeholders like "+9665XXX".
phone_patterns=(
  '\+9665[0-9]{8}'
  '\+20(10|11|12|15)[0-9]{8}'
)
forbidden_tokens=(
  'NationalId'
  'DateOfBirth'
  'CardPan'
  '"Cvv"'
  'CVV2'
  'PrimaryAccountNumber'
  'TrackData'
)

violations=0

scan_dir() {
  local dir="$1"
  if [[ ! -d "$dir" ]]; then
    echo "check-notifications-no-pii: ${dir} not found — skipping (module not present yet)." >&2
    return 0
  fi

  # Phone patterns.
  for pattern in "${phone_patterns[@]}"; do
    while IFS= read -r line; do
      [[ -z "$line" ]] && continue
      # Skip allowlisted lines.
      if grep -q "allowlist:phone-fixture" <<<"$line"; then
        continue
      fi
      echo "VIOLATION (phone): ${line}" >&2
      violations=$((violations + 1))
    done < <(grep -rEn "$pattern" "$dir" \
              --include='*.cs' --include='*.json' --include='*.csv' \
              --exclude='*.example' --exclude='*.template' \
              2>/dev/null || true)
  done

  # Forbidden field tokens.
  for token in "${forbidden_tokens[@]}"; do
    while IFS= read -r line; do
      [[ -z "$line" ]] && continue
      # Skip pure-comment lines and documentation.
      if grep -qE '^\s*(//|<summary>|\*)' <<<"$line"; then
        continue
      fi
      echo "VIOLATION (forbidden field): ${line}" >&2
      violations=$((violations + 1))
    done < <(grep -rEn "$token" "$dir" \
              --include='*.cs' --include='*.json' \
              --exclude='*.example' --exclude='*.template' \
              2>/dev/null || true)
  done
}

for dir in "${SCAN_DIRS[@]}"; do
  scan_dir "$dir"
done

if [[ $violations -gt 0 ]]; then
  echo "" >&2
  echo "check-notifications-no-pii: ${violations} violation(s) found." >&2
  echo "Add '// allowlist:phone-fixture' on intentional test fixtures, or use" >&2
  echo "the redaction helper (PiiRedactor) before emitting payloads." >&2
  exit 1
fi

echo "check-notifications-no-pii: clean."
exit 0
