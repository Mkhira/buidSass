# Spec 029 Readiness Gate

**Version**: 1.0 | **Date**: 2026-05-17

Prerequisites that must close **before** spec 029 (QA + hardening) can kick off, plus parallel-track operator work that must close **before** Section 13 launch sign-off.

Source: cross-reference of `specs/phase-1F/029-qa-and-hardening/` against the launch-readiness checklist in `docs/implementation-plan.md` §13 (lines 1154–1209).

---

## A. Hard prerequisites — 029 cannot start until all green

| ID | Item | Owner | Sign-off | Notes |
|---|---|---|---|---|
| A-1 | Risk 11 resolved: Arabic editorial reviewer named in `docs/risks/risk-register.md` and onboarded | Product Lead | [ ] | T001 gate. If unresolved, US-2 (localization) halts. Critical-path long pole. |
| A-2 | E1 (Infrastructure Integration) Staging stack live in Azure Saudi Arabia Central | Infra | [ ] | All of US-1, US-3–US-8 depend on Staging being at parity with planned Production. |
| A-3 | Staging ACA replica count ≥ 100 % of planned Production count | Infra | [ ] | Needed for US-6 k6 results to be meaningful (T037–T042). |
| A-4 | Azure Key Vault `kv-dental-stg` provisioned with **all** provider sandbox credentials | Infra | [ ] | Covers ADR-007 (payments), ADR-008 (shipping), ADR-009 (notifications). Tracked by `scripts/notifications/populate-kv-slots.sh` + equivalent payments/shipping scripts. |
| A-5 | Provider sandbox accounts active and credentials wired into KV | Ops + per-provider contact | [ ] | HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu (payments); SMSA, Aramex, Bosta (shipping); SES, Unifonic, Vodafone Egypt, FCM, SendGrid, Infobip (notifications). Verified by 029 T007. |
| A-6 | Current `main` image tag deployed to Staging | Infra | [ ] | Smoke-verify `/healthz` returns 200 from Staging before T001 clears. |

---

## B. Soft prerequisites — must close in parallel with 029, blocking only at Section 13 walk

These are NOT 029 tasks (plan §"What 029 Does NOT Do" defers them), but they MUST close before US-9 (Section 13 sign-off) can pass.

| ID | Item | Owner | Target | Sign-off | Notes |
|---|---|---|---|---|---|
| B-1 | Support team trained on admin tools, verification flow, refund flow | Operations Lead | Before US-9 | [ ] | |
| B-2 | Verification SOP authored and reviewed | Operations + Product | Before US-9 | [ ] | Covers professional-credential verification per spec 020. |
| B-3 | Refund SOP authored and reviewed | Operations + Finance | Before US-9 | [ ] | Covers spec 013 returns / refund decisions per Principle 25. |
| B-4 | Order-ops SOP authored and reviewed | Operations | Before US-9 | [ ] | Covers spec 011/018 order lifecycle handling. |
| B-5 | Egypt VAT invoice format reviewed and signed off by accountant | Finance + Compliance | Before US-9 | [ ] | Spec 012 tax-invoice format; Egyptian Law 151/2020 compliance. |
| B-6 | Uptime monitor live (Staging + Production) | Infra | Before US-7 | [ ] | E1 responsibility per implementation-plan §Phase 1E. |
| B-7 | Error tracking active (App Insights or equivalent) | Infra | Before US-7 | [ ] | E1 responsibility. |
| B-8 | Payment-failure alert rules configured and firing | Infra + Ops | Before US-5 | [ ] | Spec 029 chaos drills (T031) exercise the alert path but do not configure it. |

---

## C. Gaps inside spec 029 itself

Items in the §13 checklist that spec 029 does NOT currently have a task for. Decide before kick-off whether to add a task or accept the gap.

| ID | Gap | Recommendation |
|---|---|---|
| C-1 | Migration repeatability from a clean DB (T043 only tests seed idempotency on an existing DB) | Add a one-off task to Phase 8 (Container health): spin up a fresh Postgres, replay all migrations in order, assert schema = current `main`. Owner: Infra. |
| C-2 | Rate-limit enforcement on public endpoints | Plan §13 calls for it but no spec has a verifying test. Consider folding into US-4 (security) as an additional T-task. Owner: Security. |
| C-3 | Backup + restore drill (in-region per ADR-010) | Not mentioned in E1 or 029. Likely an ops runbook, not a code task — record under Section B if so. Owner: Infra. |

---

## Kick-off protocol

1. Walk Section A. **All boxes ticked → 029 may start (T001).** Any unchecked → escalate to the named owner; do NOT start 029.
2. Assign owners + target dates for every Section B item at the same kick-off meeting.
3. Decide C-1 / C-2 / C-3 disposition: add task to 029, accept gap, or defer to Phase 1.5.
4. Track this doc as the source of truth for 029 entry — link from `specs/phase-1F/029-qa-and-hardening/spec.md` § Entry Criteria once that section exists.

---

## References

- `docs/implementation-plan.md` §13 — launch-readiness checklist (lines ~1154–1209)
- `specs/phase-1F/029-qa-and-hardening/spec.md` — QA + hardening scope and US-1..US-10
- `specs/phase-1F/029-qa-and-hardening/tasks.md` — T001..T060 (verify task IDs against current file at sign-off)
- `docs/dod.md` — Definition of Done v1.0 (universal core + per-spec gates)
- `specs/phase-1E/E1-infrastructure-integration/` — Staging + Production runtime
- `specs/phase-1E/025-notifications/tasks.md` T011 — KV credential population (notifications half of A-4 / A-5)
