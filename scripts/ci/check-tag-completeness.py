#!/usr/bin/env python3
"""
E1 Phase 0 (T003). Parses every .bicep file under infra/azure/ and verifies that
each `Microsoft.*` resource declaration carries the four mandatory tags:
`environment`, `market_codes`, `cost_center`, `owner` (data-model.md §1).

Strategy: bicep doesn't have a stable JSON AST exposed by `az bicep build` for
arbitrary parameter shapes without a full transpile, so this guard does a
syntactic-but-tolerant scan:

1. Walk every `.bicep` file under `infra/azure/`.
2. For every `resource <symbol> '<Microsoft.*/...>@<api-version>'` declaration,
   capture the full block (matched by paren-counting `{ ... }`).
3. Within the block, look for a `tags:` assignment. Accept either inline
   (`tags: { environment: ..., market_codes: ..., cost_center: ..., owner: ... }`)
   or by reference (`tags: commonTags` / `tags: tags`) — modules that derive
   `commonTags` from main.bicep are the canonical source.
4. For inline tag assignments, assert all four keys appear.
5. For reference assignments, accept the reference name `commonTags` or `tags`
   (the contract is that main.bicep computes the four-tag object and threads it
   through every module). Other reference names FAIL with a hint.

Exit codes:
   0 = all resources tagged correctly
   1 = one or more violations (prints them to stdout)
   2 = bicep file parse error

This is a pre-deploy guard — `az deployment sub what-if` is the source of truth
once a sandbox is allocated (T071 promotes that to merge-blocking).
"""

from __future__ import annotations
import pathlib
import re
import sys


MANDATORY_TAGS = ("environment", "market_codes", "cost_center", "owner")
ACCEPTED_TAG_REFS = ("commonTags", "tags", "resourceTags")
INFRA_ROOT = pathlib.Path(__file__).resolve().parents[2] / "infra" / "azure"

# Match: resource <symbol> 'Microsoft.<rp>/<type>@<api-version>' = {
RESOURCE_HEADER_RE = re.compile(
    r"^\s*resource\s+(\w+)\s+'(Microsoft\.[\w./-]+@[\w-]+)'\s*=\s*(?:if\s*\([^)]+\)\s*)?\{",
    re.MULTILINE,
)


def find_block_end(text: str, open_idx: int) -> int:
    depth = 0
    i = open_idx
    while i < len(text):
        c = text[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def extract_resources(content: str):
    for m in RESOURCE_HEADER_RE.finditer(content):
        symbol, type_str = m.group(1), m.group(2)
        open_idx = content.find("{", m.start())
        if open_idx == -1:
            continue
        close_idx = find_block_end(content, open_idx)
        if close_idx == -1:
            continue
        yield symbol, type_str, content[open_idx : close_idx + 1]


TAG_INLINE_RE = re.compile(r"tags\s*:\s*\{([^{}]*)\}", re.DOTALL)
TAG_REF_RE = re.compile(r"tags\s*:\s*(\w+)\b")


def check_tags(block: str) -> tuple[bool, str]:
    inline = TAG_INLINE_RE.search(block)
    if inline:
        body = inline.group(1)
        missing = [t for t in MANDATORY_TAGS if not re.search(rf"\b{t}\s*:", body)]
        if missing:
            return False, f"inline tags missing: {', '.join(missing)}"
        return True, "inline-ok"
    ref = TAG_REF_RE.search(block)
    if ref:
        if ref.group(1) in ACCEPTED_TAG_REFS:
            return True, f"reference-ok ({ref.group(1)})"
        return False, (
            f"tag reference '{ref.group(1)}' is not in the accepted set "
            f"{ACCEPTED_TAG_REFS}; threading the four mandatory tags through "
            "`commonTags` is the canonical pattern."
        )
    return False, "no tags: block found"


def main() -> int:
    violations: list[str] = []
    bicep_files = sorted(INFRA_ROOT.rglob("*.bicep"))
    if not bicep_files:
        print(f"check-tag-completeness: no .bicep files under {INFRA_ROOT}", file=sys.stderr)
        return 0  # not yet provisioned; downstream `bicep-lint` job will surface this.
    for path in bicep_files:
        try:
            content = path.read_text(encoding="utf-8")
        except OSError as exc:
            print(f"check-tag-completeness: cannot read {path}: {exc}", file=sys.stderr)
            return 2
        for symbol, type_str, block in extract_resources(content):
            ok, reason = check_tags(block)
            if not ok:
                violations.append(
                    f"{path.relative_to(INFRA_ROOT.parent.parent)}:{symbol} ({type_str}): {reason}"
                )
    if violations:
        print("tag-completeness violations:", file=sys.stderr)
        for v in violations:
            print(f"  {v}", file=sys.stderr)
        return 1
    print(f"check-tag-completeness: {len(bicep_files)} bicep file(s) scanned, all resources tagged.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
