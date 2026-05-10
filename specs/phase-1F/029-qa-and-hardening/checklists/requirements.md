# Requirements Checklist: 029 — QA & Launch Hardening

**Spec**: 029-qa-and-hardening
**Phase**: 1F
**Created**: 2026-05-10

## Spec completeness (Principle 29 — Required Spec Output)

- [x] 1. Goal — present in spec.md §Goal.
- [x] 2. User roles — Launch Captain, QA Lead, Engineering Lead, Security Lead, Operations Lead, Compliance Lead, Product Lead, Arabic Editorial Reviewer named in user stories.
- [x] 3. Business rules — BR-1 through BR-16 enumerated.
- [x] 4. User flow — 10 user stories (US-1 through US-10) cover the full pillar set.
- [x] 5. UI states — N/A for this spec (no new UI). Documented in spec.md §Out of Scope.
- [x] 6. Data model — data-model.md states zero new schema; documents existing tables exercised.
- [x] 7. Validation rules — Evidence Bundle frontmatter contract enforced via contracts/evidence-bundle-layout.md.
- [x] 8. API or service requirements — N/A (no new APIs); existing API endpoints exercised by regression + chaos.
- [x] 9. Edge cases — 10 edge cases enumerated in spec.md §Edge Cases.
- [x] 10. Acceptance criteria — Each user story carries Acceptance Scenarios; Success Criteria SC-1..SC-13 listed.
- [x] 11. Phase assignment — Phase 1F · Milestone 9 · sole spec.
- [x] 12. Dependencies — Hard, external (Risk 11), and tooling deps enumerated.

## Constitution compliance

- [x] Principle 4 (Arabic editorial-grade) — BR-2 + US-2 explicitly require named-reviewer sign-off.
- [x] Principle 7 (brand palette) — BR-10 + US-10 promote impeccable-scan to merge-blocking on apps/admin_web.
- [x] Principle 24 (state machines) — re-verification covered by regression + chaos drills.
- [x] Principle 25 (audit) — every QA action captured in Evidence Bundle or audit_log_entries (BR-12).
- [x] Principle 28 (AI-build) — implementation-ready: every pillar has owner, tooling, exit criterion, evidence path.
- [x] Principle 29 (required spec output) — see above.
- [x] Principle 30 (phasing) — strictly Phase 1F; Out-of-Scope routes to Phase 1.5.
- [x] Principle 31 (constitution supremacy) — any QA finding conflicting with constitution opens remediation issue + blocks 029 exit.

## ADR compliance

- [x] ADR-010 (residency) — BR-7 confines Production smoke to Saudi Arabia Central.
- [x] ADR-001 through ADR-006 — all touched by DoD audit (BR-1 / FR-018).

## Guardrails

- [x] Guardrail #1 (lint + format) — already green on main; not re-baselined by 029.
- [x] Guardrail #2 (contract diff) — no contract changes in 029.
- [x] Guardrail #3 (constitution + ADR fingerprint) — present in this session.
- [x] Guardrail #4 (CODEOWNERS) — adds one line for `.github/workflows/impeccable-scan.yml`; no other CODEOWNERS edits.

## Memory rules

- [x] Every spec file fully written; no `.gitkeep` stubs; no "expand later" placeholders. (feedback memory)
- [x] No new seeders introduced (this spec touches no schema). Seeder verification IS in scope for FR-013 / SC-7 (production smoke task #7).
- [x] SpecKit feature.json advance covered in FR-022 / SC-13.

## Risk register touchpoints

- [x] Risk 11 (Arabic editorial reviewer) — called out as external prerequisite, NOT a task. (FR-021)
- [x] Risk 7 (residency) — BR-7 + US-7.
- [x] Risk 3 (payment race) — US-5 chaos drill on payments.

## Cross-spec dependency declarations

- [x] All Phase 1A specs at DoD — listed in spec.md §Dependencies.
- [x] All Phase 1B specs at DoD — listed.
- [x] All Phase 1C specs at DoD — listed (incl. spec 015 admin_web, gating US-10).
- [x] All Phase 1D specs at DoD — listed.
- [x] All Phase 1E specs at DoD — listed (incl. E1 for Production ACA stack).

## Evidence Bundle contract

- [x] Bundle directory shape defined (contracts/evidence-bundle-layout.md §1).
- [x] Frontmatter format defined (contracts/evidence-bundle-layout.md §2).
- [x] Per-pillar required-files matrix defined (contracts/evidence-bundle-layout.md §3).
- [x] Sign-off rules defined (contracts/evidence-bundle-layout.md §4).
- [x] Storage-location default + Operations Lead override path defined (contracts/evidence-bundle-layout.md §5).
- [x] Bundle exit criterion defined (contracts/evidence-bundle-layout.md §6).

## Out-of-scope sanity check

- [x] Out-of-Scope items listed in spec.md (WhatsApp; multi-vendor; ASVS L2/L3; pen-test; > 5× load; marketing-site copy; admin bundle-size beyond impeccable; long-form blog Arabic; full chaos-engineering platform; cross-region BCDR).
- [x] No out-of-scope item is silently in scope inside any user story or task.

## Tasks.md readiness

- [x] tasks.md authored.
- [x] Every Acceptance Scenario in spec.md is traceable to ≥ 1 task in tasks.md.
- [x] Every Success Criterion (SC-1..SC-13) is traceable to ≥ 1 task in tasks.md.
- [x] Tasks ordered by spec phase (Phase 0 setup → Phase 9 launch authorization).
- [x] Parallelizable tasks marked `[P]`.
