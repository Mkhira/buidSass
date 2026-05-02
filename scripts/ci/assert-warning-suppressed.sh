#!/usr/bin/env bash
# spec-022 T147 — CI grep guard.
#
# Every per-module backend bootstrap that calls AddDbContext<...>() must
# suppress CoreEventId.ManyServiceProvidersCreatedWarning in its EF Core
# options builder. The suppression is required because Identity tests spin
# up multiple WebApplicationFactories per test run; without it the warning
# is upgraded to an error and Identity.Tests breaks.
#
# Project-memory rule: "every new module's AddDbContext must suppress
# ManyServiceProvidersCreatedWarning or Identity tests break."
#
# This script asserts the suppression is present in each module's
# *Module.cs file.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODULES_DIR="${REPO_ROOT}/services/backend_api/Modules"
PATTERN="ManyServiceProvidersCreatedWarning"

declare -a MISSING=()

# Per-module bootstrap files (matches Modules/<Name>/<Name>Module.cs and
# any partial files). Reviews ships ReviewsModule.cs as a partial split
# across .Customer / .Admin / .Workers / etc; the suppression must be in
# the canonical bootstrap file of each module.
while IFS= read -r module; do
    name="$(basename "${module}" Module.cs)"
    primary="${MODULES_DIR}/${name}/${name}Module.cs"
    if [[ ! -f "${primary}" ]]; then
        continue
    fi
    if ! grep -q "${PATTERN}" "${primary}"; then
        # Allow the suppression to live in any partial of the same module.
        if ! grep -rqs "${PATTERN}" "${MODULES_DIR}/${name}/" 2>/dev/null; then
            MISSING+=("${primary}")
        fi
    fi
done < <(find "${MODULES_DIR}" -maxdepth 2 -mindepth 2 -name "*Module.cs" -not -name "*.Partial.cs" | sort -u)

if [[ ${#MISSING[@]} -gt 0 ]]; then
    echo "ERROR: ${#MISSING[@]} backend module(s) are missing the required" >&2
    echo "       ConfigureWarnings(w => w.Ignore(CoreEventId.${PATTERN})) call:" >&2
    for f in "${MISSING[@]}"; do
        echo "       - ${f}" >&2
    done
    echo "" >&2
    echo "See spec-022 R14 / project-memory rule. Without this suppression," >&2
    echo "Identity.Tests fails when multiple WebApplicationFactory instances" >&2
    echo "are created in the same test run." >&2
    exit 1
fi

echo "OK: ${PATTERN} suppression present in every per-module *Module.cs."
