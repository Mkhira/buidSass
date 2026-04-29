# AR Editorial Review Tracker — Verification (Spec 020)

Tracks Arabic ICU keys in `verification.ar.icu` that are awaiting editorial sign-off
per **Principle 4** (Arabic quality MUST be editorial-grade, not machine-translated).

## Workflow

1. Each phase adds AR keys to `verification.ar.icu` alongside their EN counterparts in `verification.en.icu`.
2. New AR keys are listed in the "Pending review" table below with the slice that introduced them, the originating PR, and the engineer who staged the AR copy.
3. The editorial reviewer signs off by checking the row's `Reviewed?` box and moving the row into "Approved keys" with a date.
4. SC-008 (DoD) requires every AR key in `verification.ar.icu` to appear in the "Approved keys" section before the spec can ship.

## Pending review

The keys staged across Phase 3 batch 1 + Phase 4 batches 1–3 are listed below as **first-pass technical translations**. They cover the customer-visible reason codes touched by US1 + US2 (submission, eligibility outcomes, reviewer decisions, document-purge messaging). Editorial review is required before DoD.

| Slice / Phase | Key prefix | Added by | PR | Reviewed? |
|---|---|---|---|---|
| Phase 3 batch 1 / US1 | `verification.eligibility.*` (11 keys) | implementation pass | PR (TBD) | ☐ |
| Phase 3 batch 1 / US1 | `verification.required_field_missing`, `verification.regulator_identifier_invalid`, `verification.documents_invalid`, `verification.document.*` (9 keys) | implementation pass | PR (TBD) | ☐ |
| Phase 3 batch 1 / US1 | `verification.already_pending`, `verification.cooldown_active`, `verification.account_inactive`, `verification.market_unsupported`, `verification.renewal_*` (5 keys) | implementation pass | PR (TBD) | ☐ |
| Phase 4 batch 1 / US2 | `verification.review.reason_required`, `verification.already_decided`, `verification.invalid_state_for_action`, `verification.reviewer.scope_mismatch`, `verification.pii.access_forbidden` (5 keys) | implementation pass | PR (TBD) | ☐ |
| Phase 4 batch 1 / US2 | `verification.row.version_conflict`, `verification.idempotency.*`, `verification.linked_entity_unavailable`, `verification.eligibility.cache_stale` (4 keys) | implementation pass | PR (TBD) | ☐ |
| Phase 4 batch 3 / US2 | `verification.review_permission_required`, `verification.revoke_permission_required`, `verification.document_not_found`, `verification.document_purged`, `verification.not_found` (5 keys) | implementation pass | PR (TBD) | ☐ |
| Phase 3 reference data | `verification.field.profession.label`, `verification.field.regulator_identifier.{ksa,eg}.label` (3 keys) | reference seeder | PR (TBD) | ☐ |

**Total**: 42 keys awaiting AR editorial sign-off.

## Approved keys

| Key | Reviewed by | Approved date |
|---|---|---|

_(empty — populated as the editorial reviewer signs off batches above)_

## Notes for reviewers

- AR strings render right-to-left; ICU placeholders (`{cooldown_until}`, `{expired_at}`, `{old_market}`, `{new_market}`, `{required_profession}`, `{reason}`) MUST preserve their syntax exactly — do not translate the placeholder names.
- The current first-pass translations:
  - Use formal MSA (Modern Standard Arabic).
  - Translate technical/regulatory terms to their official Arabic equivalents (SCFHS = هيئة التخصصات الصحية; Egyptian Medical Syndicate = النقابة الطبية المصرية).
  - Are intentionally conservative on tone — the reviewer should adjust to match the dental commerce platform's customer-facing voice once that voice is finalized.
- Verification copy frequently references regulator-specific terminology; use the official Arabic name from each regulator's own publications, not a literal translation of the English label.
- For market-aware error messages, KSA-AR and EG-AR may differ slightly in formal vs. colloquial register; flag any reviewer concern in the "Reviewed?" column with a brief note.
- Phase 5 (US3) and beyond will introduce additional eligibility-message keys consumed by Catalog/Cart/Checkout. Those keys reuse the same translation conventions — coordinate with the reviewer of those phases for consistency.
