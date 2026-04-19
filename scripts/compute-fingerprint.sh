#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CONSTITUTION_FILE="${REPO_ROOT}/.specify/memory/constitution.md"
EXTRACT_SCRIPT="${REPO_ROOT}/scripts/extract-adr-block.sh"

if [[ ! -f "${CONSTITUTION_FILE}" ]]; then
    echo "error: missing ${CONSTITUTION_FILE}" >&2
    exit 1
fi

if [[ ! -x "${EXTRACT_SCRIPT}" ]]; then
    echo "error: ${EXTRACT_SCRIPT} is missing or not executable" >&2
    exit 1
fi

if command -v shasum >/dev/null 2>&1; then
    HASH_CMD=(shasum -a 256)
elif command -v sha256sum >/dev/null 2>&1; then
    HASH_CMD=(sha256sum)
else
    echo "error: no SHA-256 command found (shasum or sha256sum)" >&2
    exit 1
fi

{
    cat "${CONSTITUTION_FILE}"
    "${EXTRACT_SCRIPT}"
} | "${HASH_CMD[@]}" | awk '{ print $1 }'
