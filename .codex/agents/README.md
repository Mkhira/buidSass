# Spec Kit pipeline → Codex CLI (gpt-5.3-codex)

This directory contains the five Spec Kit pipeline subagents converted from
Claude Code's subagent format to **OpenAI Codex CLI's** subagent format,
running on **gpt-5.3-codex**.

## What changed in the conversion

| Original (Claude Code) | Converted (Codex CLI) |
|---|---|
| `.claude/agents/*.md` with YAML frontmatter | `.codex/agents/*.toml` with TOML keys |
| `name`, `description`, `tools`, `model`, `color` frontmatter | `name`, `description`, `model`, `model_reasoning_effort`, `sandbox_mode`, `nickname_candidates`, plus the body in `developer_instructions = """..."""` |
| `model: opus` / `sonnet` / `inherit` | `model = "gpt-5.3-codex"` |
| `tools: Bash, Read, Write, Edit, Glob, Grep, Task` | Implicit — Codex's built-in shell + apply_patch + file tools cover Read/Write/Edit/Bash/Glob/Grep. The `Task` tool is replaced by Codex's native subagent spawning (see below). |
| `Task` tool dispatches a child agent | Parent agent instructs Codex via natural language: *"Spawn the speckit-reviewer subagent with payload {…}, wait for its return."* Codex's runtime handles spawn/wait/return. |
| Slash commands at `.claude/commands/speckit-*.md` (hyphenated) | Slash commands at `.codex/prompts/speckit.*.md` (Spec Kit's default for Codex is dotted; both styles work and the agents auto-detect). |
| Persistent memory at `~/.claude/agent-memory/<name>/` | Persistent memory at `~/.codex/agent-memory/<name>/` |
| `model: opus` orchestrators | `model_reasoning_effort = "high"` for orchestrators (conductor, implementer, reviewer, spec-creator) and `"medium"` for the more procedural pr-handler. |

The substantive instructions for each agent — boot sequences, dispatch rules,
state schemas, fix-loops, structured-return formats — are preserved verbatim.
Only the *meta-shell* (frontmatter, dispatch syntax, paths) was rewritten.

## Files

| File | Purpose |
|---|---|
| `.codex/config.toml` | Project Codex config: pins `gpt-5.3-codex`, sets `agents.max_depth = 2` so the implementer can spawn reviewer + pr-handler. |
| `.codex/agents/spec-creator.toml` | Converts a phase of the implementation plan into Spec Kit artifacts. |
| `.codex/agents/speckit-conductor.toml` | Top-level orchestrator. Dispatches implementer per spec, reconciles PRs, exits cleanly. |
| `.codex/agents/speckit-implementer.toml` | Per-spec phase-by-phase implementer. Spawns reviewer (×2) and pr-handler. |
| `.codex/agents/speckit-reviewer.toml` | Two-pass reviewer (correctness + conformance). Authorized to fix spec drift. |
| `.codex/agents/speckit-pr-handler.toml` | Pushes branches, opens PRs with durable markers, drives CodeRabbit, marks merge-ready. |

## Installation

### 1. Project-scoped install (recommended)

From the repo root:

```bash
mkdir -p .codex/agents
cp /path/to/this/.codex/config.toml      .codex/config.toml
cp /path/to/this/.codex/agents/*.toml    .codex/agents/
```

Verify Codex sees them:

```bash
codex --ask-for-approval never "List active subagents."
```

You should see `spec-creator`, `speckit-conductor`, `speckit-implementer`,
`speckit-reviewer`, `speckit-pr-handler` alongside the built-ins (`default`,
`worker`, `explorer`).

### 2. Personal (cross-repo) install — optional

If you want these available everywhere:

```bash
mkdir -p ~/.codex/agents
cp .codex/agents/*.toml ~/.codex/agents/
```

Project-scoped agents take precedence over personal ones with the same name.

### 3. Initialize Spec Kit for Codex

If the repo isn't already initialized for Codex Spec Kit:

```bash
uvx --from git+https://github.com/github/spec-kit.git specify init --here --ai codex
```

This creates `.specify/` (templates, memory, scripts) and the slash-command
prompt files Codex reads.

### 4. Verify Codex auth and gh CLI

```bash
codex --version
codex --ask-for-approval never "echo ready"
gh auth status
```

The conductor and pr-handler both depend on `gh` being authenticated.

## Usage

### Spec creation

```
You: Use spec-creator to spec out Phase 1.5 from docs/implementation-plan.md
```

Codex spawns `spec-creator`, which runs the 6-step Spec Kit workflow per spec
unit (specify → clarify → plan → tasks → analyze → gap analysis) and returns a
summary table.

### Multi-spec implementation

Once you have specs in `specs/phase-1D/` (for example):

```
You: Use speckit-conductor to run @specs/phase-1D --max-parallel 3
```

The conductor:
1. Builds the dependency DAG.
2. Reconciles with live `gh` state (open / merged / rejected PRs).
3. Dispatches `speckit-implementer` for ready specs.
4. Each implementer walks tasks.md phase-by-phase, spawning `speckit-reviewer`
   (×2) then `speckit-pr-handler` per phase.
5. Returns a final report listing PRs to merge in dependency order.
6. **Exits.** It does not poll. After you merge PRs, re-invoke it.

## Key design point: subagent depth

Claude Code lets agents call other agents through a `Task` tool that's
effectively unbounded in depth.

Codex bounds nesting via `agents.max_depth` — **default 1** (one level deep).
Our pipeline needs **depth 2** because:

```
speckit-conductor             (depth 0, root)
  └── speckit-implementer     (depth 1)
        ├── speckit-reviewer  (depth 2)  ← needs max_depth = 2
        └── speckit-pr-handler (depth 2) ← needs max_depth = 2
```

`.codex/config.toml` in this bundle sets `agents.max_depth = 2`. If you skip
that setting, the implementer will fail to spawn reviewer/pr-handler.

## Headless / CI usage

You can drive any of these subagents non-interactively with `codex exec`:

```bash
codex exec "Use speckit-conductor to run @specs/phase-1D --dry-run"
```

For full automation, pair `codex exec --json --output-last-message <file>`
with your CI runner.

## Notes & caveats

- **`gpt-5.3-codex`** is the production-tuned Codex model. The OpenAI docs also
  reference `gpt-5.3-codex-spark` (lighter, faster) and `gpt-5.4` (the larger
  generalist) — feel free to swap the `model = "..."` line per agent if you
  want, e.g. `gpt-5.4` for the reviewer's high-stakes second pass and
  `gpt-5.3-codex-spark` for the more procedural pr-handler.

- **`Task` tool absence is intentional.** In Codex, you describe what you want
  spawned in plain English inside the parent agent's instructions. Codex's
  runtime resolves the agent name and handles the spawn/wait/return cycle.
  The agent files retain their structured-return JSON schemas, so the parent
  agent receives well-formed responses regardless.

- **Sandbox mode is `workspace-write`** for every agent. None of these agents
  are pure observers — even the reviewer commits fixes. If you want the
  reviewer to be observation-only and have the implementer apply its findings
  instead, change `sandbox_mode = "read-only"` in `speckit-reviewer.toml` and
  rework the FIX-ALL POLICY section accordingly.

- **Memory paths** assume macOS user `mohamedkhira`. If you move machines or
  share these files, update the `/Users/mohamedkhira/.codex/agent-memory/...`
  paths in each `developer_instructions` block, or replace with `$HOME` and
  let Codex expand it at run time.

- **Slash command naming.** Spec Kit's Codex initializer typically writes
  dotted prompt files (`speckit.specify.md`). Older / Claude-targeted
  initializers write hyphenated ones (`speckit-specify.md`). Each agent's
  boot sequence detects which style is present and adapts.

## Troubleshooting

**"Subagent X not found"** — check `.codex/agents/X.toml` exists and has
`name = "X"` matching exactly. The filename is convention; the `name` field is
the source of truth.

**"max_depth exceeded"** — confirm `.codex/config.toml` has
`[agents] max_depth = 2`. Codex defaults to 1.

**"Can't find /speckit.specify"** — Spec Kit isn't initialized for Codex. Run
`specify init --here --ai codex`.

**Conductor reports `gh-unavailable`** — `gh auth status` is failing. Re-auth
with `gh auth login` or fix your token.

**Implementer reports `phase-timeout`** — `/speckit.implement` exceeded the
60-minute budget. Either bump `--implement-budget 120` on the conductor
invocation or split the offending phase into smaller phases in `tasks.md`.
