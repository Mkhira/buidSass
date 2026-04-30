# AR Editorial Review — Spec 022 Reviews & Moderation

**Status**: AI-pre-reviewed; **awaiting human editorial sign-off (T142)**.
**Blocks**: launch (T142), not merge.
**Owner**: editorial reviewer assigned at the AR-quality gate.

Per Principle 4 of the dental-commerce constitution, every customer-visible
Arabic string in `reviews.ar.icu` must be reviewed by an **editorial-grade
human Arabic speaker** before the spec hits DoD. This document tracks the
per-key sign-off status; SC-008 verifies the table is fully human-ticked at
launch time.

## AI pre-review (2026-04-30)

A first-pass AI review (Claude Opus 4.7) inspected all 34 strings for:
- Grammatical correctness (verb agreement, gender, case)
- Semantic fidelity to the English counterpart in `reviews.en.icu`
- Obvious calques / "Arabish" patterns that violate Principle 4

**The AI pre-review is NOT a substitute for human editorial sign-off.** It
flags candidates for the human reviewer to focus on, nothing more. Principle 4
explicitly forbids machine translation; an LLM pre-review is closer to a
linter than to editorial judgment.

### Outcomes
- **30 strings**: grammatically correct + semantically faithful to EN.
  Stylistic refinements possible per native taste; no objective errors.
- **2 strings flagged for editorial concern** (see Notes column below).
- **2 strings with minor stylistic notes** (see Notes column).

## Sign-off table

| Key | Status | Reviewer | Date | Notes |
|---|---|---|---|---|
| review.eligibility.no_delivered_purchase | ⬜ pending | — | — | AI-pre-review: clean |
| review.eligibility.refunded | ⬜ pending | — | — | AI-pre-review: clean |
| review.eligibility.window_closed | ⬜ pending | — | — | AI-pre-review: clean |
| review.eligibility.already_reviewed | ⬜ pending | — | — | AI-pre-review: clean |
| review.headline.length_invalid | ⬜ pending | — | — | AI-pre-review: clean (numerals 1-100 in Western digits — confirm market preference for Arabic-Indic digits ١-١٠٠) |
| review.body.length_invalid | ⬜ pending | — | — | AI-pre-review: clean (same numeral note) |
| review.rating.out_of_range | ⬜ pending | — | — | AI-pre-review: clean (same numeral note) |
| review.locale.invalid | ⬜ pending | — | — | AI-pre-review: clean |
| review.media.too_many | ⬜ pending | — | — | AI-pre-review: clean |
| review.media.invalid_signed_url | ⬜ pending | — | — | AI-pre-review: clean |
| review.edit.window_closed | ⬜ pending | — | — | AI-pre-review: clean |
| review.edit.not_author | ⬜ pending | — | — | AI-pre-review: minor — word order "يحق فقط لكاتب التقييم الأصلي تعديله" reads awkwardly; native form may prefer "لا يحق إلا لكاتب التقييم الأصلي تعديله" |
| review.edit.deleted_terminal | ⬜ pending | — | — | AI-pre-review: clean |
| review.row.version_conflict | ⬜ pending | — | — | AI-pre-review: minor — "بواسطة" is a literal calque of English "by"; native form may prefer "قام شخص آخر بتعديله" |
| review.row.delete_forbidden | ⬜ pending | — | — | **🚩 AI flag** — "الحذف ناعم فقط" is a literal calque of "soft delete" that won't read naturally to AR-native users. Suggested rewrite: "يمكن إخفاء التقييم فقط؛ لا يتم حذفه نهائياً من النظام." |
| review.report.cannot_report_own_review | ⬜ pending | — | — | AI-pre-review: clean |
| review.report.reason_invalid | ⬜ pending | — | — | AI-pre-review: clean |
| review.report.note_required | ⬜ pending | — | — | AI-pre-review: clean |
| review.report.already_reported_by_actor | ⬜ pending | — | — | AI-pre-review: clean |
| review.report.unauthenticated | ⬜ pending | — | — | AI-pre-review: clean |
| review.rate_limit.submission_exceeded | ⬜ pending | — | — | AI-pre-review: clean |
| review.rate_limit.edit_exceeded | ⬜ pending | — | — | AI-pre-review: clean |
| review.rate_limit.report_exceeded | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.moderation.forbidden | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.moderation.delete_requires_super_admin | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.moderation.reason_required | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.moderation.invalid_state | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.moderation.delete_terminal | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.moderation.version_conflict | ⬜ pending | — | — | AI-pre-review: minor — "أثناء قرارك" is awkward; native form may prefer "أثناء اتخاذك القرار" |
| reviews.moderation.rate_limit_exceeded | ⬜ pending | — | — | AI-pre-review: clean (note: "التمهل قليلاً" is informal; "يرجى المحاولة لاحقاً" matches the rate-limit family register) |
| reviews.policy.forbidden | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.policy.wordlist.term_invalid | ⬜ pending | — | — | **🚩 AI flag** — "الفلترة" is informal/Arabish; editorial AR prefers "التصفية". Also "بعد التطبيع" is technical jargon — operators may prefer "بعد المعالجة" or "بعد التنسيق". |
| reviews.policy.market.value_out_of_range | ⬜ pending | — | — | AI-pre-review: clean |
| reviews.aggregate.market_invalid | ⬜ pending | — | — | AI-pre-review: clean (slightly casual; "غير معترف به" or "غير صالح" reads more formal) |

## Procedure

1. Editorial reviewer reads each AR string against its EN counterpart in `reviews.en.icu` for semantic fidelity AND editorial register (formal customer-facing tone, market-appropriate for SA + EG).
2. Reviewer marks `✅ approved` in the Status column with their name + ISO date.
3. If a string needs change, reviewer files a follow-up commit updating both `reviews.ar.icu` and the row's Notes column with the rationale.
4. SC-008 release-gate scan parses this table; any `⬜ pending` row blocks launch (not merge — per spec text).
5. The 2 `🚩 AI flag` rows are highest-priority candidates for the editorial reviewer's attention.

## Why this isn't AI-signable

Principle 4 of the constitution: *"Arabic quality MUST be editorial-grade,
not machine-translated."* An LLM pre-review can validate grammar and surface
calques, but cannot judge:
- Brand voice / register appropriate for a medical-marketplace customer
- Regional preferences (SA Modern Standard vs EG dialect-influenced MSA)
- Sensitivity / cultural fit of error-message phrasing

Those judgments belong to a human native-Arabic editorial reviewer.
