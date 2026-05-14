#!/usr/bin/env bash
#
# E1 Phase 4 (T049). Reduces a raw `az deployment sub what-if --no-pretty-print` JSON
# file to a structured array per data-model.md §4:
#
#   [ { "resource_id": "...", "change_kind": "Create"|"Modify"|"Delete", "fields_changed": ["...", ...] } ]
#
# Usage:
#   scripts/azure/parse-whatif.sh <input-json-path> [<output-json-path>]
#
# When output path is omitted, writes to stdout. Exit codes: 0=ok, 1=parse error, 2=usage.

set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <whatif.json> [<parsed.json>]" >&2
  exit 2
fi

input="$1"
output="${2:--}"

if [[ ! -f "$input" ]]; then
  echo "parse-whatif: input file not found: $input" >&2
  exit 1
fi

# The `az deployment sub what-if --no-pretty-print` JSON shape:
#   { "changes": [ { "resourceId": "...", "changeType": "Modify"|"Create"|"Delete"|"NoChange"|"Ignore",
#                    "delta": [ { "path": "...", "before": ..., "after": ... }, ... ] }, ... ],
#     "status": "...", ... }
#
# We KEEP Modify | Create | Delete; drop NoChange and Ignore.
parsed=$(jq '[ .changes[]
  | select(.changeType == "Modify" or .changeType == "Create" or .changeType == "Delete")
  | {
      resource_id: .resourceId,
      change_kind: .changeType,
      fields_changed: (
        if (.delta // empty) then ( .delta | map(.path) ) else [] end
      )
    } ]' "$input")

if [[ "$output" == "-" ]]; then
  printf '%s\n' "$parsed"
else
  printf '%s\n' "$parsed" > "$output"
fi
exit 0
