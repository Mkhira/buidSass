# Definition of Done

**Version**: 1.0 | **Date**: 2026-04-19

## Universal Core

- [ ] **UC-1**: Acceptance scenarios all pass for the spec.
  - Verification: Manual + Automated
  - Spec reference: FR-009
- [ ] **UC-2**: Lint and format checks pass in CI (`lint-format`).
  - Verification: Automated
  - Spec reference: FR-005, FR-009
- [ ] **UC-3**: Contract drift check passes in CI (`contract-diff`).
  - Verification: Automated
  - Spec reference: FR-006, FR-009
- [ ] **UC-4**: Context fingerprint in PR description matches canonical hash (`verify-context-fingerprint`).
  - Verification: Automated
  - Spec reference: FR-007a, FR-009
- [ ] **UC-5**: Constitution and ADR-protected paths are not changed without required code-owner approvals.
  - Verification: Automated + Manual
  - Spec reference: FR-009, FR-012
- [ ] **UC-6**: Required human code-owner approvals are present.
  - Verification: Automated
  - Spec reference: FR-002, FR-003, FR-014
- [ ] **UC-7**: Merge target enforces signed commits and approved merge policy.
  - Verification: Automated + Manual
  - Spec reference: FR-004, FR-015, FR-016, FR-017
- [ ] **UC-8**: Spec header records the constitution version in force.
  - Verification: Manual
  - Spec reference: FR-013

## Applicability-Tagged Items

### [trigger: state-machine]

If the spec defines or changes a stateful workflow, it must explicitly list states, transitions, actors, transition guards, and failure/retry handling.

### [trigger: audit-event]

If the spec performs critical writes/decisions, it must emit auditable events including actor, timestamp, resource, before/after values, and reason.

### [trigger: storage]

If the spec handles file/object storage, it must verify residency routing, signed URL policy, and access-control boundaries.

### [trigger: pdf]

If the spec generates PDF outputs, it must validate Arabic + English rendering, RTL support, font embedding, and visual regression baseline.

### [trigger: user-facing-strings]

If the spec introduces user-facing strings, it must include Arabic editorial review evidence and localization key coverage.
