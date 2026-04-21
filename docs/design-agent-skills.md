# Design-Agent Skills (D1 workstream)

**Status**: scaffolded 2026-04-22
**Scope**: tooling layer — does not amend the Constitution and does not depend on Phase 1A artifacts
**Authority**: subordinate to `.specify/memory/constitution.md` v1.0.0 (Principles 4, 7, 27, 31, 32)
**Owner**: Lane B primary (frontend + admin), Lane A reviewer
**Tracking**: `docs/implementation-plan.md` → Phase 1B trailing subsection "D1 · design-agent-skills"

---

## 1. Purpose

AI-agent-generated UI drifts toward generic design (system fonts, unverified contrast, arbitrary spacing, unconsidered motion). Principles 4 (editorial Arabic + RTL), 7 (locked palette), and 27 (premium medical marketplace UX) treat that drift as a launch risk.

D1 installs [impeccable](https://github.com/pbakaus/impeccable) — an AI design-skill system (reference docs + steering commands + a 24-rule CLI scanner) — as the enforcement layer at the agent prompt level. It is **not** a runtime dependency, component library, or design system. It plugs into agent tooling directories the same way `.claude/skills/speckit-*` already does.

---

## 2. What is installed

| Location                                        | Purpose                                                                                                 |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| `.impeccable/VERSION`                           | Pinned upstream commit SHA, date, license pointer                                                       |
| `.impeccable/LICENSE`, `.impeccable/NOTICE.md`  | Apache-2.0 text and attribution (impeccable + Anthropic frontend-design base)                           |
| `.impeccable/thresholds.json`                   | CI scan thresholds (advisory through Phase 1C/1D/1E; enforced by spec 029 in Phase 1F)                  |
| `.claude/skills/<18 skills>/`                   | Impeccable skills — single committed source of truth (Claude format, `/` command prefix)                |
| `.claude/skills/impeccable-brand/SKILL.md`      | **Brand-adapter overlay** — palette/typography/RTL/tone; overrides upstream defaults                    |
| `.github/workflows/impeccable-scan.yml`         | Advisory CI job — scans Next.js build output on `apps/admin_web/**` PRs                                 |

> **Why only `.claude/skills/`?** `.agents/` is gitignored in this repo — it's populated locally by per-harness installers (see `.specify/integrations/codex.manifest.json` for the equivalent speckit pattern). The canonical committed location for agent-facing assets is `.claude/skills/`. Codex and GLM access impeccable via their context files (`.codex/system.md`, `GLM_CONTEXT.md`), which the D1 block points at the `.claude/skills/impeccable*/` paths.

The 18 impeccable skills copied verbatim from upstream: `adapt`, `animate`, `audit`, `bolder`, `clarify`, `colorize`, `critique`, `delight`, `distill`, `harden`, `impeccable`, `layout`, `optimize`, `overdrive`, `polish`, `quieter`, `shape`, `typeset`.

---

## 3. Precedence

1. `.specify/memory/constitution.md` (Constitution — Principle 31 supremacy)
2. `.claude/skills/impeccable-brand/SKILL.md` (brand overlay)
3. `packages/design_system/tokens.css` and the design-system Dart package
4. Upstream impeccable skills

Where the overlay and upstream disagree, the overlay wins. Where the overlay and the Constitution disagree, the Constitution wins.

---

## 4. When agents invoke impeccable

Invoke the skills **only** on UI-bearing work:

- Any spec numbered **014–024 or 029**, or
- Any change touching `apps/customer_flutter/**`, `apps/admin_web/**`, or `packages/design_system/**`.

Backend-only specs (001–013, 025–028) MUST NOT load impeccable. Doing so wastes context and is grounds for PR revision.

The Guardrail #3 injection script (`scripts/gen-agent-context.sh`) now writes a "Design-agent skills (D1 workstream)" block into every agent context file (`CLAUDE.md`, `.codex/system.md`, `GLM_CONTEXT.md`) stating this rule.

### Expected cadence per UI spec

| Stage of the spec                     | Command(s)                                      |
|---------------------------------------|-------------------------------------------------|
| Before implementation begins          | `/shape` to plan UX & UI                        |
| During implementation                 | `/impeccable`, `/layout`, `/typeset`, `/colorize` |
| Pre-PR self-review                    | `/audit` (required), `/polish` (recommended)    |
| Targeted polish passes                | `/critique`, `/harden`, `/clarify`              |
| Motion / delight                      | `/animate`, `/delight` (use sparingly)          |

The `/audit` report MUST be attached to the PR description for any UI-bearing spec PR in Phase 1C–1E.

---

## 5. Advisory CI scan (`impeccable-scan`)

`.github/workflows/impeccable-scan.yml`:

- Triggers on PRs touching `apps/admin_web/**`, `.github/workflows/impeccable-scan.yml`, or `.impeccable/**`.
- Installs dependencies via pnpm, builds the Next.js app, then runs `npx github:pbakaus/impeccable#<pinned-sha> detect` on the build output.
- Uploads a `impeccable-report` artifact (JSON + text) and posts a summary comment to the PR.
- **Advisory only** through Phase 1C/1D/1E — always exits 0. Spec 029 (`docs/implementation-plan.md`) replaces the advisory exit with a thresholds-enforcing step in Phase 1F.
- Does **not** run on `apps/customer_flutter/**`. The scanner is HTML/CSS-focused; Flutter output would false-positive. Flutter gets the reference-doc and steering-command value via the in-session skills only.

Until spec 015 scaffolds `apps/admin_web/package.json`, the workflow skips build + scan steps cleanly — it will not fail a PR just because the Next.js app doesn't yet exist.

---

## 6. Thresholds & waivers

`.impeccable/thresholds.json`:

```json
{
  "mode": "advisory",
  "scope": ["apps/admin_web"],
  "ignore_paths": ["apps/customer_flutter/**"],
  "severity_budgets": { "P0": 0, "P1": 5, "P2": 20, "P3": null },
  "waiver_path": "CODEOWNERS-approved PR tagged `impeccable-waiver`"
}
```

Budgets are advisory now, enforced in Phase 1F. Once promoted:

- `P0` budget of 0 blocks merge on any critical finding.
- `P1`/`P2` budgets can be exceeded only with an `impeccable-waiver`-labeled PR approved by a CODEOWNERS reviewer (same approval path as constitution/ADR edits, per Guardrail #4).
- Budgets MUST be tightened, not loosened, over time. Any loosening requires a written rationale in the PR description.

---

## 7. Upgrade procedure

1. Open a scratch worktree.
2. Fetch new commit: `git clone --depth 1 https://github.com/pbakaus/impeccable.git /tmp/impeccable-up`.
3. Build: `cd /tmp/impeccable-up && bun install --frozen-lockfile && bun run build` (requires `bun`).
4. Diff provider output against our vendored copy:
   ```bash
   diff -ruN .claude/skills /tmp/impeccable-up/.claude/skills | less
   ```
5. Review the diff with a human — upstream skill-name changes (e.g. `arrange → layout`) require updating the Guardrail #3 context block in `scripts/gen-agent-context.sh` and the cadence table in Section 4 above. Do NOT overwrite `impeccable-brand/SKILL.md` — it is our overlay.
6. Copy the new skill dirs into place: `rsync -a --delete --exclude impeccable-brand /tmp/impeccable-up/.claude/skills/ .claude/skills/`. The `--exclude` keeps our overlay safe; `--delete` cleans up deprecated skills (e.g. the old `arrange` dir when it's been merged into `layout`).
7. Update `.impeccable/VERSION` with the new commit SHA + date.
8. Re-run `scripts/gen-agent-context.sh` so CLAUDE.md / .codex/system.md / GLM_CONTEXT.md pick up the new pinned SHA.
9. Open a PR — CODEOWNERS review required because the upgrade changes agent context (Guardrail #3 territory).

Upgrades MUST be deliberate PRs. Never auto-pull latest from upstream at session init.

---

## 8. Local CLI usage

Agents run commands through the harness (`/audit`, `/polish`). Humans can invoke the CLI scanner directly:

```bash
# One-off scan of a locally built admin_web
pnpm --filter ./apps/admin_web build
npx github:pbakaus/impeccable#$(awk '/^commit:/ {print $2; exit}' .impeccable/VERSION) detect apps/admin_web/.next

# Interactive browser overlay (watch mode)
npx github:pbakaus/impeccable#$(awk '/^commit:/ {print $2; exit}' .impeccable/VERSION) live --port 3456
```

The overlay mode is useful for polish passes on a running dev server — it does not affect production.

---

## 9. What D1 does NOT change

- No Phase 1A artifact. Specs 001–003 and A1 are untouched. Validate with:
  ```bash
  git diff origin/main -- specs/phase-1A/ packages/design_system/ infra/local/ services/backend_api/Dockerfile scripts/dev/
  ```
- No runtime dependency on impeccable. The Flutter app, Next.js admin, and .NET backend do not import it.
- No constitutional change. D1 is tooling; Principle 32 amendment path is not triggered.
- No new ADR. The locked stack (ADR-001 through ADR-010) is unaffected.
- No new design tokens. The brand overlay READS from `packages/design_system/tokens.css`; spec 003 remains the token authority.

---

## 10. Phase 1F promotion (spec 029)

`docs/implementation-plan.md` spec 029 `qa-and-hardening` carries the D1 promotion task:

> Promote `impeccable-scan` to a merge-blocking CI job on `apps/admin_web`; configure thresholds in `.impeccable/thresholds.json`; document waiver path via CODEOWNERS.

Promotion steps:

1. Edit `.github/workflows/impeccable-scan.yml`: replace the final "Advisory exit" step with a thresholds check that exits non-zero when severity budgets in `.impeccable/thresholds.json` are exceeded. Remove `continue-on-error` from the scan step.
2. Flip `.impeccable/thresholds.json` `mode` to `enforced`.
3. Add `impeccable-waiver` label routing in CODEOWNERS or a GitHub ruleset — label application requires CODEOWNERS approval.
4. Exercise the promotion on a throwaway PR: seed a P0 finding, verify red check; apply waiver, verify unblocked.
5. Capture the sign-off in the Section 13 launch-readiness checklist (`docs/implementation-plan.md`).

Flutter stays scanner-exempt in 1F. Flutter UX quality is gated by `/audit` + editorial review + visual regression (per spec 029 existing tasks), not the impeccable CLI.
