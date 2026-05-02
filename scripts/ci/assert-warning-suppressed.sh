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
# *Module.cs file (or any of its partial-class siblings under the same
# Modules/<Name>/ folder). Modules that don't call AddDbContext are
# skipped — the rule only applies to modules that actually register
# their own DbContext.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODULES_DIR="${REPO_ROOT}/services/backend_api/Modules"
PATTERN="ManyServiceProvidersCreatedWarning"

declare -a MISSING=()

# Returns 0 if the file contains a non-comment, non-string occurrence of
# the given pattern. We strip:
#   - // line comments after stripping leading whitespace
#   - /* ... */ block comments (single-line form)
#   - quoted strings ("..." with optional verbatim @-prefix)
# Bash doesn't have a real C# parser; this regex is a pragmatic filter
# that catches the common false-positive shapes (commented-out reminders
# and pattern names embedded in error-message strings) without needing
# Roslyn.
has_uncommented_match() {
    local file="$1"
    local pattern="$2"
    # Strip block comments first (greedy, single-line), then line comments,
    # then string literals, then grep for the pattern.
    sed -E \
        -e 's,/\*[^*]*\*/,,g' \
        -e 's,//.*$,,' \
        -e 's,@?"([^"\\]|\\.)*",,g' \
        "$file" \
      | grep -q "${pattern}"
}

# Returns 0 if the file (or any sibling under the same module folder)
# registers a DbContext via AddDbContext. We rely on a token search
# rather than full parse — `services.AddDbContext<` (or similar) is a
# stable marker across all modules.
module_calls_add_dbcontext() {
    local module_dir="$1"
    grep -rqs "AddDbContext\s*<" "${module_dir}" 2>/dev/null
}

# Per-module bootstrap files (matches Modules/<Name>/<Name>Module.cs and
# any partial files). Reviews ships ReviewsModule.cs as a partial split
# across .Customer / .Admin / .Workers / etc; the suppression must be in
# the canonical bootstrap file or any partial of each module.
while IFS= read -r module; do
    name="$(basename "${module}" Module.cs)"
    primary="${MODULES_DIR}/${name}/${name}Module.cs"
    module_dir="${MODULES_DIR}/${name}/"
    if [[ ! -f "${primary}" ]]; then
        continue
    fi

    # Modules that don't register a DbContext are not subject to the rule.
    if ! module_calls_add_dbcontext "${module_dir}"; then
        continue
    fi

    # Search the entire module folder (primary + partials) for a non-
    # comment, non-string occurrence of the suppression pattern.
    found=0
    while IFS= read -r candidate; do
        if has_uncommented_match "${candidate}" "${PATTERN}"; then
            found=1
            break
        fi
    done < <(find "${module_dir}" -maxdepth 2 -name "*.cs")

    if [[ ${found} -eq 0 ]]; then
        MISSING+=("${primary}")
    fi
done < <(find "${MODULES_DIR}" -maxdepth 2 -mindepth 2 -name "*Module.cs" -not -name "*.Partial.cs" | sort -u)

if [[ ${#MISSING[@]} -gt 0 ]]; then
    echo "ERROR: ${#MISSING[@]} backend module(s) call AddDbContext<...> without" >&2
    echo "       a (non-comment, non-string) ConfigureWarnings(w => w.Ignore(" >&2
    echo "       CoreEventId.${PATTERN})) call:" >&2
    for f in "${MISSING[@]}"; do
        echo "       - ${f}" >&2
    done
    echo "" >&2
    echo "See spec-022 R14 / project-memory rule. Without this suppression," >&2
    echo "Identity.Tests fails when multiple WebApplicationFactory instances" >&2
    echo "are created in the same test run." >&2
    exit 1
fi

echo "OK: ${PATTERN} suppression present in every per-module *Module.cs that calls AddDbContext."
