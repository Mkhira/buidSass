#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

"${REPO_ROOT}/scripts/gen-agent-context.sh" >/dev/null

if ! grep -q "Principle 32" "${REPO_ROOT}/CLAUDE.md"; then
    echo "Missing expected text: Principle 32" >&2
    exit 1
fi

if ! grep -q "ADR-010" "${REPO_ROOT}/CLAUDE.md"; then
    echo "Missing expected text: ADR-010" >&2
    exit 1
fi

echo "test-gen-agent-context: PASS"
