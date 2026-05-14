#!/usr/bin/env bash
#
# E1 Phase 0 (T005). Fails the CI run if any of `.github/workflows/`, `infra/`, or `scripts/`
# contains a string matching `AZURE_CLIENT_SECRET` or `client-secret\s*[:=]`. This is the
# AC-6 "OIDC, no client secret" guard: passwordless federated credentials are the only
# acceptable auth pattern for GitHub Actions → Azure.
#
# Allowlist:
#   - This script itself (which has to literally contain the pattern in order to grep for it).
#   - Comments documenting the prohibition (line starts with `#` or `//` AND mentions
#     "DO NOT" / "MUST NOT" / "forbidden" / "prohibited").
#   - The `client-secret` key-name in the secret-naming taxonomy (a legitimate OAuth client
#     secret slot in Key Vault — that's a STORED secret, not a workflow secret). This is
#     detected by the surrounding context being a `domain/market/provider/client-secret`
#     style path, not a key=value workflow assignment.
#
# Exit codes: 0 = clean, 1 = violation found.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TARGETS=(".github/workflows" "infra" "scripts")

violations=0

for target in "${TARGETS[@]}"; do
  full="${REPO_ROOT}/${target}"
  [[ -d "$full" ]] || continue
  # Use grep -nE with line-level allowlist filtering.
  # We exclude this guard script itself (self-reference) and the legitimate stored-secret slots.
  while IFS= read -r match; do
    [[ -z "$match" ]] && continue
    file="${match%%:*}"
    rest="${match#*:}"
    lineno="${rest%%:*}"
    content="${rest#*:}"
    # Allowlist 1: this script itself.
    [[ "$file" == "${REPO_ROOT}/scripts/ci/check-no-client-secret.sh" ]] && continue
    # Allowlist 2: prose / comments / workflow-job-name documenting the prohibition.
    case "$content" in
      *"DO NOT"*|*"do not"*|*"MUST NOT"*|*"forbidden"*|*"prohibited"*|*"prohibits"*|*"forbids"*) continue ;;
      *"Forbid AZURE_CLIENT_SECRET"*|*"Forbid client-secret"*) continue ;;
      *"no-client-secret"*|*"no_client_secret"*) continue ;;
    esac
    # Allowlist 3: stored-secret-slot reference. The taxonomy uses `client-secret` as a
    # key-name in `<domain>/<market>/<provider>/client-secret`. Detect by surrounding `/`.
    if echo "$content" | grep -qE '(payments|shipping|notifications-[a-z]+|db|meili|app)/[a-z]+/[a-z0-9-]+/client-secret'; then
      continue
    fi
    # Allowlist 4: comment-only mentions documenting the regex pattern itself.
    if echo "$content" | grep -qE '^\s*(#|//|/\*).*(AZURE_CLIENT_SECRET|client-secret)'; then
      continue
    fi
    echo "client-secret violation: $file:$lineno: $content" >&2
    violations=$((violations + 1))
  done < <(grep -RInE 'AZURE_CLIENT_SECRET|client-secret\s*[:=]' "$full" 2>/dev/null || true)
done

if [[ $violations -gt 0 ]]; then
  echo "check-no-client-secret: $violations violation(s) — OIDC is the only allowed auth pattern (AC-6)." >&2
  exit 1
fi
echo "check-no-client-secret: clean."
exit 0
