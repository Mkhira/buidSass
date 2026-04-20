# Requirements Checklist (T026)

Verified on: 2026-04-19 against PR #1 (merge commit d5f86ec).

## Universal Core

- [x] **UC-1**: Acceptance scenarios all pass for the spec. — verified via PR #1 merge.
- [x] **UC-2**: Lint and format checks pass in CI (`lint-format`). — green on PR #1.
- [x] **UC-3**: Contract drift check passes in CI (`contract-diff`). — green on PR #1 (baseline openapi.json present).
- [x] **UC-4**: Context fingerprint in PR description matches canonical hash (`verify-context-fingerprint`). — first-run bootstrap note: this PR seeded the main-branch baseline; subsequent PRs will verify against it.
- [x] **UC-5**: Constitution and ADR-protected paths are not changed without required code-owner approvals. — CODEOWNERS rules in place; enforced by branch protection (T018).
- [x] **UC-6**: Required human code-owner approvals are present. — PR #1 approved and merged by @Mkhira.
- [x] **UC-7**: Merge target enforces signed commits and approved merge policy. — settings documented in `docs/branch-protection.md`; applied via `scripts/apply-branch-protection.sh` (T018).
- [x] **UC-8**: Spec header records the constitution version in force. — `specs/phase-1A/001-governance-and-setup/spec.md` records `**Constitution**: v1.0.0`.

## Active applicability tags

none — this spec has no state machine, audit events, storage, PDF, or user-facing strings.
