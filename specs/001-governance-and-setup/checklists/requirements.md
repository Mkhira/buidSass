# Requirements Checklist (T026)

## Universal Core

- [ ] **UC-1**: Acceptance scenarios all pass for the spec.
- [ ] **UC-2**: Lint and format checks pass in CI (`lint-format`).
- [ ] **UC-3**: Contract drift check passes in CI (`contract-diff`).
- [ ] **UC-4**: Context fingerprint in PR description matches canonical hash (`verify-context-fingerprint`).
- [ ] **UC-5**: Constitution and ADR-protected paths are not changed without required code-owner approvals.
- [ ] **UC-6**: Required human code-owner approvals are present.
- [ ] **UC-7**: Merge target enforces signed commits and approved merge policy.
- [ ] **UC-8**: Spec header records the constitution version in force.

## Active applicability tags

none — this spec has no state machine, audit events, storage, PDF, or user-facing strings.
