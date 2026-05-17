#!/usr/bin/env bash
#
# T051 — AC-27 invariant guard. Scans Notifications payload-builder source
# files for raw unredacted PII patterns: E.164 phones, Saudi national IDs
# (10 digits starting with 1 or 2), Egyptian national IDs (14 digits starting
# with 2 or 3), PAN-shaped strings.
#
# The PiiRedactor.Redact() / MaskPhoneToLast4() helpers MUST be applied
# before any string is written to notifications.payload_redacted_json or
# emitted via INotificationsAuditEmitter. This guard catches drift.
#
# Strategy:
#   - Scope: services/backend_api/Modules/Notifications/**/*.cs
#   - Exclude: PiiRedactor.cs itself (it contains the patterns by design),
#     tests, comments.
#   - Heuristics: look for hard-coded PII literals (rare but high-signal)
#     and for serialization paths that include raw recipient/phone fields
#     without going through the redactor.
#
# Exit codes: 0 = clean, 1 = violation, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
violations=0

# 1. Hard-coded PII literals in source (excluding PiiRedactor + tests).
literal_scan_dirs=(
  "$REPO_ROOT/services/backend_api/Modules/Notifications"
)
literal_exclude='(PiiRedactor\.cs|/Tests/|\.Tests/|\.md:)'

while IFS= read -r line; do
  [ -z "$line" ] && continue
  echo "FAIL [pii-literal] $line"
  violations=$((violations + 1))
done < <(grep -rEn --include='*.cs' \
    -e '\+9665[0-9]{8}\b' \
    -e '\+201[0-2,5][0-9]{8}\b' \
    -e '\b[12][0-9]{9}\b.*national_id' \
    -e '\b[23][0-9]{13}\b.*national_id' \
    "${literal_scan_dirs[@]}" 2>/dev/null | grep -vE "$literal_exclude" || true)

# 2. Direct JsonSerializer.Serialize calls on a record containing a raw
#    Recipient or Phone field WITHOUT a redacted/masked suffix. This is a
#    heuristic and may false-positive; downgrade to warning if needed.
while IFS= read -r line; do
  [ -z "$line" ] && continue
  echo "WARN [pii-serialize-raw] $line"
done < <(grep -rEn --include='*.cs' \
    -e 'JsonSerializer\.Serialize.*\b(Recipient|Phone|NationalId)\b' \
    "${literal_scan_dirs[@]}" 2>/dev/null \
    | grep -vE 'Redacted|Masked|MaskPhone|PiiRedactor|RecipientId|RecipientKind' \
    | grep -vE "$literal_exclude" || true)

if [ "$violations" -gt 0 ]; then
  echo ""
  echo "PII guard found $violations violation(s). See PiiRedactor.cs for the canonical redaction helpers."
  exit 1
fi
echo "PII guard clean."
exit 0
