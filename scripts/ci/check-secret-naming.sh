#!/usr/bin/env bash
#
# E1 Phase 0 (T004). Validates every secret reference in `infra/azure/keyvault-bootstrap.bicep`
# and `scripts/azure/**/*.sh` against the canonical naming regex from
# `contracts/infrastructure-contract.md §4`:
#
#   ^(payments|shipping|notifications-email|notifications-sms|notifications-push|db|meili|app)/
#    (sa|eg|multi)/[a-z0-9-]+/
#    (api-key|api-secret|webhook-signing-key|client-id|client-secret|account-sid|auth-token|
#     service-account-json|connection-string|master-key|jwt-signing-key|data-protection-key)$
#
# The regex applies to the LOGICAL path form. Azure KV stores names with `/` flattened to `--`;
# this guard checks both forms by normalizing `--` to `/` before applying the regex.
#
# Strategy:
#  1. Extract every string literal that LOOKS LIKE a secret path: matches the rough shape
#     `<word>/<word>/<word>/<word>` or `<word>--<word>--<word>--<word>` inside .bicep and .sh files.
#  2. Skip false-positives: comments (lines starting with `#`, `//`, `/*`), URLs (`https://...`),
#     and known structural Azure paths (`/subscriptions/...`, `/providers/...`).
#  3. For each candidate, normalize `--` to `/` and apply the regex.
#  4. Print every violation; exit non-zero if any.
#
# Exit codes: 0 = all secret refs valid, 1 = one or more violations, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INFRA_FILE="${REPO_ROOT}/infra/azure/keyvault-bootstrap.bicep"
SCRIPTS_GLOB="${REPO_ROOT}/scripts/azure"

# The canonical regex (POSIX ERE form). Note: `[a-z0-9-]+` for provider/key segments.
SECRET_PATH_RE='^(payments|shipping|notifications-email|notifications-sms|notifications-push|db|meili|app)/(sa|eg|multi)/[a-z0-9-]+/(api-key|api-secret|webhook-signing-key|client-id|client-secret|account-sid|auth-token|service-account-json|connection-string|master-key|jwt-signing-key|data-protection-key)$'

# Permissive candidate-extractor: 4 word-ish segments separated by `/` or `--`.
CANDIDATE_RE='[a-zA-Z0-9_-]+(/|--)[a-zA-Z0-9_-]+(/|--)[a-zA-Z0-9_-]+(/|--)[a-zA-Z0-9_-]+'

violations=0
files_scanned=0

scan_file() {
  local file="$1"
  files_scanned=$((files_scanned + 1))
  # Strip line comments (#, //) before matching, leave block-comment contents in place — the
  # bicep parser doesn't honor /* */ but humans sometimes use them in shell.
  while IFS= read -r raw_line; do
    # Drop leading whitespace + skip pure-comment lines.
    case "$raw_line" in
      \#*|//*|/\**) continue ;;
    esac
    # Strip inline `# ...` tail in shell, `// ...` tail in bicep.
    local line
    line="$(printf '%s\n' "$raw_line" | sed -E 's@//.*$@@; s/#.*$//')"
    # Skip URL and Azure-ARM-path noise before regex extraction.
    case "$line" in
      *https://*|*/subscriptions/*|*/providers/*) continue ;;
    esac
    # Extract candidate paths.
    while read -r candidate; do
      [[ -z "$candidate" ]] && continue
      # Normalize storage encoding (Azure KV flattens / to --).
      local logical="${candidate//--/\/}"
      if ! [[ "$logical" =~ $SECRET_PATH_RE ]]; then
        # Skip non-secret structural strings (e.g., paths with extension).
        case "$logical" in
          *.sh|*.bicep|*.json|*.yml|*.yaml|*.md|*.cs|*.py) continue ;;
        esac
        # Only complain about candidates that are CLEARLY trying to be secret paths:
        # they must start with one of the closed domain set words.
        case "$logical" in
          payments/*|shipping/*|notifications-*|db/*|meili/*|app/*) ;;
          *) continue ;;
        esac
        echo "secret-naming violation in $file: $candidate" >&2
        violations=$((violations + 1))
      fi
    done < <(printf '%s\n' "$line" | grep -oE "$CANDIDATE_RE" || true)
  done < "$file"
}

if [[ -f "$INFRA_FILE" ]]; then
  scan_file "$INFRA_FILE"
else
  echo "check-secret-naming: $INFRA_FILE not yet present — skipping (Phase 1 lands it)." >&2
fi

if [[ -d "$SCRIPTS_GLOB" ]]; then
  while IFS= read -r -d '' sh_file; do
    scan_file "$sh_file"
  done < <(find "$SCRIPTS_GLOB" -type f -name "*.sh" -print0)
fi

echo "check-secret-naming: $files_scanned file(s) scanned, $violations violation(s)."
if [[ $violations -gt 0 ]]; then
  exit 1
fi
exit 0
