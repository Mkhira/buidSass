# Requirements Checklist (T080)

Verified on: 2026-04-19 for branch `003-shared-foundations`.

## Universal Core

- [x] **UC-1**: Acceptance scenarios all pass for the spec.
- [x] **UC-2**: Lint and format checks pass in CI (`lint-format`).
- [x] **UC-3**: Contract drift check passes in CI (`contract-diff`).
- [x] **UC-4**: Context fingerprint in PR description matches canonical hash (`verify-context-fingerprint`).
- [x] **UC-5**: Constitution and ADR-protected paths are not changed without required code-owner approvals.
- [x] **UC-6**: Required human code-owner approvals are present.
- [x] **UC-7**: Merge target enforces signed commits and approved merge policy.
- [x] **UC-8**: Spec header records the constitution version in force.

## Active applicability tags

- [x] `[audit-event]`
- [x] `[storage]`
- [x] `[pdf]`
- [x] `[user-facing-strings]`
- [ ] `[state-machine]`
