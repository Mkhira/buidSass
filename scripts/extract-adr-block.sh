#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
IMPLEMENTATION_PLAN="${REPO_ROOT}/docs/implementation-plan.md"

if [[ ! -f "${IMPLEMENTATION_PLAN}" ]]; then
    echo "error: missing ${IMPLEMENTATION_PLAN}" >&2
    exit 1
fi

# Extract section 7 (Architecture decisions / ADRs) until the next top-level section.
awk '
    /^## 7\. / { in_section = 1 }
    in_section {
        if ($0 ~ /^## [0-9]+\./ && $0 !~ /^## 7\. /) {
            exit
        }
        print
    }
' "${IMPLEMENTATION_PLAN}"
