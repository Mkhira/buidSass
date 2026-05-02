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

## Seeder strings (T125)

The `ReviewsV1DevSeeder` synthetic dataset includes Arabic-language review
content used for QA + training in Dev / Staging. Per Principle 4 every
customer-visible AR string must be reviewed before launch even though the
seeder never runs in Production (gated behind `IsDevelopment / IsStaging`).
T142 sign-off unblocks the launch gate, not the merge gate.

| Seed id | Locale | String | Status | Reviewer | Date |
|---|---|---|---|---|---|
| 55555555-…-000000000003 (visible) | ar-EG | "أداء ممتاز يومياً في العيادة" / "استخدمته في عدة جلسات تنظيف وحشوة، نتائج ثابتة. وصل التغليف بحالة جيدة لكن مع تأخر يوم عن الموعد المتوقع." | ⬜ DRAFT | — | — |
| 55555555-…-000000000004 (visible) | ar-EG | "مقبول للاستخدام المتكرر" / "يفي بالغرض في الإجراءات الاعتيادية. الجودة متوسطة مقارنة بالسعر؛ سأبحث عن بديل قبل الطلب القادم." | ⬜ DRAFT | — | — |

Notes for editorial pass:
- Maintain MSA register (avoid Egyptian colloquialisms).
- "العيادة" = clinic (preferred over "المستوصف" in dental commerce context).
- "التغليف" = packaging — confirm against current spec-005 catalog terminology.
- Decimals / numbers: use Arabic-Indic numerals only when the surrounding UI
  uses them (storefront default per spec 014); otherwise leave Latin.

## Procedure

1. Editorial reviewer reads the AR string against the EN counterpart in `reviews.en.icu` (or against the seeder context for the seeder strings table) for semantic fidelity.
2. Reviewer marks ✅ in the Status column with their name + ISO date.
3. If a string needs change, reviewer files a follow-up commit + updates the row's notes column.
4. SC-008 release-gate scan parses this table; any ⬜ blocks launch.

## Sign-off log (T142)

The launch gate (T142) requires every row in the ICU-key table AND the
seeder-strings table above to be ✅ before SC-008 passes. The reviewer logs
an entry below per pass; the entry serves as the audit trail for editorial
sign-off.

| Date | Reviewer | Strings reviewed | Notes |
|---|---|---|---|
| _pending_ | _pending_ | _pending_ | T142 — first editorial pass not yet performed; tracked as launch blocker, not merge blocker per Principle 4. |
