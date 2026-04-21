#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CONSTITUTION_FILE="${REPO_ROOT}/.specify/memory/constitution.md"
PLAN_FILE="${REPO_ROOT}/docs/implementation-plan.md"
EXTRACT_SCRIPT="${REPO_ROOT}/scripts/extract-adr-block.sh"
FINGERPRINT_SCRIPT="${REPO_ROOT}/scripts/compute-fingerprint.sh"
DOD_FILE="${REPO_ROOT}/docs/dod.md"

if [[ ! -f "${CONSTITUTION_FILE}" ]]; then
    echo "error: missing ${CONSTITUTION_FILE}" >&2
    exit 1
fi

if [[ ! -f "${PLAN_FILE}" ]]; then
    echo "error: missing ${PLAN_FILE}" >&2
    exit 1
fi

if [[ ! -x "${EXTRACT_SCRIPT}" ]]; then
    echo "error: ${EXTRACT_SCRIPT} is missing or not executable" >&2
    exit 1
fi

if [[ ! -x "${FINGERPRINT_SCRIPT}" ]]; then
    echo "error: ${FINGERPRINT_SCRIPT} is missing or not executable" >&2
    exit 1
fi

FINGERPRINT="$(${FINGERPRINT_SCRIPT})"

extract_constitution_version() {
    local version=""

    # Prefer frontmatter-style `Version: X.Y.Z` if present.
    version="$(awk '
        BEGIN { in_frontmatter = 0; frontmatter_seen = 0 }
        /^---[[:space:]]*$/ {
            if (!frontmatter_seen) {
                in_frontmatter = 1
                frontmatter_seen = 1
                next
            } else if (in_frontmatter) {
                in_frontmatter = 0
                exit
            }
        }
        in_frontmatter && $0 ~ /^[Vv]ersion:[[:space:]]*/ {
            line = $0
            sub(/^[Vv]ersion:[[:space:]]*/, "", line)
            if (match(line, /[0-9]+\.[0-9]+\.[0-9]+/)) {
                print substr(line, RSTART, RLENGTH)
                exit
            }
        }
    ' "${CONSTITUTION_FILE}")"

    if [[ -n "${version}" ]]; then
        printf '%s\n' "${version}"
        return 0
    fi

    # Fallback to markdown metadata line: `**Version**: X.Y.Z | ...`
    version="$(grep -E '^\*\*Version\*\*:' "${CONSTITUTION_FILE}" | head -n1 | grep -Eo '[0-9]+\.[0-9]+\.[0-9]+' || true)"
    if [[ -n "${version}" ]]; then
        printf '%s\n' "${version}"
        return 0
    fi

    printf 'unknown\n'
}

CONSTITUTION_VERSION="$(extract_constitution_version)"

DOD_VERSION="$(grep -E '^\*\*Version\*\*:' "${DOD_FILE}" 2>/dev/null | head -n1 | sed -E 's/^\*\*Version\*\*:[[:space:]]*([^|]+).*/\1/' | xargs || true)"
if [[ -z "${DOD_VERSION}" ]]; then
    DOD_VERSION="unversioned"
fi

build_adr_table() {
    awk '
        BEGIN {
            in_section = 0
            adr = ""
            title = ""
            status = ""
            decision = ""
            print "| ADR | Title | Status | Decision |"
            print "|---|---|---|---|"
        }
        /^## 7\. / { in_section = 1; next }
        in_section && /^## [0-9]+\./ { in_section = 0 }
        !in_section { next }

        /^### ADR-[0-9]{3} · / {
            if (adr != "") {
                print "| " adr " | " title " | " status " | " decision " |"
            }

            line = $0
            sub(/^### /, "", line)
            n = split(line, parts, " · ")
            adr = parts[1]
            title = parts[2]
            status = (n >= 3 ? parts[3] : "Unknown")
            gsub(/\*\*/, "", status)
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", status)
            decision = ""
            next
        }

        /^\*\*Decision\*\*:/ {
            line = $0
            sub(/^\*\*Decision\*\*:[[:space:]]*/, "", line)
            gsub(/\*\*/, "", line)
            gsub(/`/, "\\`", line)
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", line)
            decision = line
            next
        }

        END {
            if (adr != "") {
                print "| " adr " | " title " | " status " | " decision " |"
            }
        }
    ' "${PLAN_FILE}"
}

write_context_file() {
    local output_file="$1"
    local agent_name="$2"

    cat > "${output_file}" <<EOF
# ${agent_name} Agent Context

<!-- context-fingerprint: ${FINGERPRINT} -->
<!-- context-fingerprint-source: .specify/memory/constitution.md + docs/implementation-plan.md §7 -->
<!-- generated-by: scripts/gen-agent-context.sh -->

## Constitution Principles (Verbatim)

EOF
    cat "${CONSTITUTION_FILE}" >> "${output_file}"

    cat >> "${output_file}" <<EOF

## ADR Decisions Table

EOF
    build_adr_table >> "${output_file}"

    cat >> "${output_file}" <<EOF

## Four Guardrails

1. Lint + format checks must pass on every PR.
2. Contract diff checks must pass on every PR.
3. Constitution + ADR fingerprint must be included and verified on PRs.
4. Constitution and ADR edits require protected human code-owner approval.

## How to work in this repo

- Respect all 32 constitution principles at all times.
- Principle 32 amendment procedure applies to all governance changes.
- Use the ADR decisions table as the default architectural baseline.
- Compute PR fingerprint with \`scripts/compute-fingerprint.sh\`.
- Apply DoD from \`docs/dod.md\` (DoD version: ${DOD_VERSION}).
- Constitution version in source context: ${CONSTITUTION_VERSION}.

## Design-agent skills (D1 workstream)

Impeccable design skills are vendored under this repo at commit \`$(cat "${REPO_ROOT}/.impeccable/VERSION" 2>/dev/null | awk '/^commit:/ {print $2; exit}' || echo 'unknown')\` (see \`.impeccable/VERSION\`, Apache-2.0).

**When working on a UI-bearing spec** — any spec numbered 014–024 or 029, OR any change touching \`apps/customer_flutter/**\`, \`apps/admin_web/**\`, or \`packages/design_system/**\`:

1. Load the \`impeccable-brand\` skill **before** any \`impeccable\` command (\`/impeccable\`, \`/audit\`, \`/polish\`, \`/critique\`, \`/typeset\`, \`/colorize\`, \`/layout\`, \`/harden\`, \`/adapt\`, \`/animate\`, \`/delight\`, \`/quieter\`, \`/bolder\`, \`/distill\`, \`/clarify\`, \`/shape\`, \`/optimize\`, \`/overdrive\`). The brand overlay encodes Principle 7 palette, Principle 4 Arabic/RTL editorial rules, and medical-marketplace tone; it overrides upstream impeccable defaults where they conflict.
2. Run \`/audit\` before opening a UI PR; attach the report to the PR description.
3. The impeccable CLI scanner runs as an **advisory** CI job on \`apps/admin_web\` PRs (see \`.github/workflows/impeccable-scan.yml\`); it is **promoted to merge-blocking in Phase 1F spec 029** per \`docs/design-agent-skills.md\`.

**Backend-only specs (001–013, 025–028) must not invoke impeccable.** The skills are not loaded there; reaching for them wastes context.

Upgrade procedure, CLI usage, and waiver path live in \`docs/design-agent-skills.md\`.
EOF
}

mkdir -p "${REPO_ROOT}/.codex"

write_context_file "${REPO_ROOT}/CLAUDE.md" "Claude"
write_context_file "${REPO_ROOT}/.codex/system.md" "Codex"
write_context_file "${REPO_ROOT}/GLM_CONTEXT.md" "GLM"

echo "Generated: CLAUDE.md, .codex/system.md, GLM_CONTEXT.md"
