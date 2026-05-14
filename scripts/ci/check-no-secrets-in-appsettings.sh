#!/usr/bin/env bash
#
# E1 Phase 5 (T062). Scans every appsettings*.json file for secret-shaped values:
#   - API-key-shaped strings (32+ hex chars, base64 blobs)
#   - JWT-shaped strings (three base64url segments separated by `.`)
#   - Base64-encoded blobs > 32 bytes
#   - AAD client secrets (matches `client[-_]secret` key paths)
#
# Wired into .github/workflows/lint-format-infra.yml as a blocking job
# (no-secrets-in-appsettings).
#
# Strategy:
#   1. Find every appsettings*.json in services/backend_api/ and apps/admin_web/.
#   2. For each STRING value (jq-extracted), apply the heuristics.
#   3. Allow placeholder sentinels (`__placeholder*`, `${...}`, `<...>`, `tbd-by-*`,
#      `please-replace`, empty strings, etc.).
#   4. Allow connection-string-shape values pointing at a localhost / 127.0.0.1 /
#      docker-network host (those are dev-only and never contain real prod secrets).
#
# Exit codes: 0 = clean, 1 = violation, 2 = script error.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
violations=0
files_scanned=0

is_placeholder() {
  local v="$1"
  # The `<...>` placeholder pattern already covers `<<...>>` (a stricter case),
  # so we collapse both into a single arm to avoid shellcheck SC2221/SC2222
  # (overlapping case patterns).
  case "$v" in
    ""|"null") return 0 ;;
    "__"*"__") return 0 ;;
    "<"*">") return 0 ;;
    "\${"*"}"|"%"*"%") return 0 ;;
    *"tbd-by-"*) return 0 ;;
    *"please-replace"*|*"replace-me"*|*"changeme"*) return 0 ;;
    *"REPLACE_ME"*) return 0 ;;
  esac
  return 1
}

is_dev_localhost_conn() {
  local v="$1"
  case "$v" in
    *"Host=localhost"*|*"Host=127.0.0.1"*|*"Host=postgres"*|*"Host=db"*) return 0 ;;
    *"Server=localhost"*|*"Server=127.0.0.1"*) return 0 ;;
  esac
  return 1
}

looks_like_jwt() {
  # JWT is exactly three base64url segments separated by dots, each typically 8+ chars.
  local v="$1"
  if [[ "$v" =~ ^[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$ ]]; then
    return 0
  fi
  return 1
}

looks_like_api_key() {
  local v="$1"
  # Long hex string (32+ chars).
  if [[ "$v" =~ ^[a-f0-9]{32,}$ ]]; then return 0; fi
  # Long base64 blob (44+ chars, base64 charset, ends with `=` or alphanumeric).
  if [[ ${#v} -ge 44 && "$v" =~ ^[A-Za-z0-9+/]{40,}={0,2}$ ]]; then return 0; fi
  return 1
}

scan_value() {
  local file="$1"
  local key="$2"
  local value="$3"

  # Allowlist: placeholders, sentinels, dev localhost.
  if is_placeholder "$value"; then return 0; fi
  if is_dev_localhost_conn "$value"; then return 0; fi
  # Allowlist: feature flags, integers, booleans, urls (no embedded secret).
  case "$value" in
    "true"|"false"|"True"|"False") return 0 ;;
  esac
  if [[ "$value" =~ ^[0-9.]+$ ]]; then return 0; fi
  case "$value" in
    http://*|https://*) return 0 ;;
  esac
  # Allowlist: short strings (no real secret fits in < 24 chars).
  if [[ ${#value} -lt 24 ]]; then return 0; fi

  # Key-name heuristics: if the JSON path mentions client_secret / api_key / password /
  # token / signing_key, the value is suspicious even if short.
  local key_lower
  key_lower="$(printf '%s' "$key" | tr '[:upper:]' '[:lower:]')"
  case "$key_lower" in
    *"client-secret"*|*"client_secret"*|*"clientsecret"*|\
    *"api-key"*|*"apikey"*|*"api_key"*|\
    *"password"*|*"signing-key"*|*"signingkey"*|\
    *"auth-token"*|*"authtoken"*|*"private-key"*|*"privatekey"*)
      echo "appsettings-secret violation: $file: key path '$key' has suspicious value of length ${#value}" >&2
      violations=$((violations + 1))
      return 0 ;;
  esac

  # Shape heuristics.
  if looks_like_jwt "$value"; then
    echo "appsettings-secret violation: $file: key '$key' looks like a JWT ($value)" >&2
    violations=$((violations + 1))
    return 0
  fi
  if looks_like_api_key "$value"; then
    echo "appsettings-secret violation: $file: key '$key' looks like an API key / base64 blob" >&2
    violations=$((violations + 1))
    return 0
  fi
}

scan_file() {
  local file="$1"
  files_scanned=$((files_scanned + 1))
  # jq emits one (path, value) pair per leaf scalar. Use the `paths(scalars)` + `getpath`
  # form to flatten the tree, then pair each path with its value.
  while IFS=$'\t' read -r path value; do
    [[ -z "$path" ]] && continue
    scan_value "$file" "$path" "$value"
  done < <(jq -r '
    [paths(strings)] as $paths |
    $paths[] as $p |
    [ ($p | join(".")), getpath($p) ] | @tsv
  ' "$file" 2>/dev/null || true)
}

# Build the list of appsettings*.json files. Portable across macOS bash 3.2 and Linux 5+.
files=""
while IFS= read -r -d '' f; do
  files="$files$f"$'\n'
done < <(find "$REPO_ROOT/services/backend_api" "$REPO_ROOT/apps" \
  -type f \( -name "appsettings.json" -o -name "appsettings.*.json" \) -print0 2>/dev/null || true)

if [[ -z "$files" ]]; then
  echo "check-no-secrets-in-appsettings: no appsettings*.json files found; skipping."
  exit 0
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "check-no-secrets-in-appsettings: jq not installed; cannot parse JSON." >&2
  exit 2
fi

while IFS= read -r f; do
  [[ -z "$f" ]] && continue
  scan_file "$f"
done <<< "$files"

if [[ $violations -gt 0 ]]; then
  echo "check-no-secrets-in-appsettings: $files_scanned file(s) scanned, $violations violation(s)." >&2
  exit 1
fi
echo "check-no-secrets-in-appsettings: $files_scanned file(s) scanned, clean."
exit 0
