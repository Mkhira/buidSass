# AR Editorial Review — Spec 022 Reviews & Moderation

**Status**: DRAFT — first-pass technical translations.
**Blocks**: launch (T142), not merge.
**Owner**: editorial reviewer assigned at the AR-quality gate.

Per Principle 4 of the dental-commerce constitution, every customer-visible
Arabic string in `reviews.ar.icu` must be reviewed by an editorial-grade
Arabic speaker before the spec hits DoD. This document tracks the per-key
sign-off status; SC-008 verifies the table is fully ticked at launch time.

## Sign-off table

| Key | Status | Reviewer | Date | Notes |
|---|---|---|---|---|
| review.eligibility.no_delivered_purchase | ⬜ pending | — | — | |
| review.eligibility.refunded | ⬜ pending | — | — | |
| review.eligibility.window_closed | ⬜ pending | — | — | |
| review.eligibility.already_reviewed | ⬜ pending | — | — | |
| review.headline.length_invalid | ⬜ pending | — | — | |
| review.body.length_invalid | ⬜ pending | — | — | |
| review.rating.out_of_range | ⬜ pending | — | — | |
| review.locale.invalid | ⬜ pending | — | — | |
| review.media.too_many | ⬜ pending | — | — | |
| review.media.invalid_signed_url | ⬜ pending | — | — | |
| review.edit.window_closed | ⬜ pending | — | — | |
| review.edit.not_author | ⬜ pending | — | — | |
| review.edit.deleted_terminal | ⬜ pending | — | — | |
| review.row.version_conflict | ⬜ pending | — | — | |
| review.row.delete_forbidden | ⬜ pending | — | — | |
| review.report.cannot_report_own_review | ⬜ pending | — | — | |
| review.report.reason_invalid | ⬜ pending | — | — | |
| review.report.note_required | ⬜ pending | — | — | |
| review.report.already_reported_by_actor | ⬜ pending | — | — | |
| review.report.unauthenticated | ⬜ pending | — | — | |
| review.rate_limit.submission_exceeded | ⬜ pending | — | — | |
| review.rate_limit.edit_exceeded | ⬜ pending | — | — | |
| review.rate_limit.report_exceeded | ⬜ pending | — | — | |
| reviews.moderation.forbidden | ⬜ pending | — | — | |
| reviews.moderation.delete_requires_super_admin | ⬜ pending | — | — | |
| reviews.moderation.reason_required | ⬜ pending | — | — | |
| reviews.moderation.invalid_state | ⬜ pending | — | — | |
| reviews.moderation.delete_terminal | ⬜ pending | — | — | |
| reviews.moderation.version_conflict | ⬜ pending | — | — | |
| reviews.moderation.rate_limit_exceeded | ⬜ pending | — | — | |
| reviews.policy.forbidden | ⬜ pending | — | — | |
| reviews.policy.wordlist.term_invalid | ⬜ pending | — | — | |
| reviews.policy.market.value_out_of_range | ⬜ pending | — | — | |
| reviews.policy.body_required | ⬜ pending | — | — | |
| review.row.not_found | ⬜ pending | — | — | |
| reviews.aggregate.market_invalid | ⬜ pending | — | — | |

## Procedure

1. Editorial reviewer reads the AR string against the EN counterpart in `reviews.en.icu` for semantic fidelity.
2. Reviewer marks ✅ in the Status column with their name + ISO date.
3. If a string needs change, reviewer files a follow-up commit + updates the row's notes column.
4. SC-008 release-gate scan parses this table; any ⬜ blocks launch.
