# DoD Walkthrough — Spec 004

## Universal Core (`docs/dod.md` v1.0)

- UC-1 Acceptance scenarios: Covered by contract/integration tests added across US1–US6.
- UC-2 Lint/format: pending CI verification in PR pipeline.
- UC-3 Contract drift: pending CI verification in PR pipeline.
- UC-4 Fingerprint: captured in `PR_NOTES.md`.
- UC-5/UC-6/UC-7 governance and approvals: pending PR checks.
- UC-8 Spec header constitution version: unchanged and preserved.

## Spec 004 Quickstart DoD checks

- 42 FR contract coverage: in progress; core P1/P2 scenarios now implemented and covered by focused contract tests.
- 13 SC measurable checks: covered by contract/integration + audit tests for implemented flows.
- 9 state machines: implemented with dedicated unit tests.
- Plaintext secrets scanner: added `scripts/dev/scan-plaintext-secrets.sh`.
- AR editorial review: documented in `AR_EDITORIAL_REVIEW.md`.
- Fingerprint attached: yes.
- Migration + seed-admin CLI checks: previously implemented in foundational phase.

## Notes

Final CI run in the PR is still required for merge-gate artifacts.
