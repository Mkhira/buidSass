# AR editorial review checklist — spec 025 Notifications

**Status**: PENDING REVIEWER
**Constitution principle**: Principle 4 (Arabic quality MUST be editorial-grade, not machine-translated)
**Blocks**: T058 in `tasks.md`; ADR-009 flip to `Accepted` (T057); production rollout
**Reviewer**: <reviewer-name>
**Date**: <fill-on-completion>

---

## Scope

This checklist enumerates every Arabic template body / subject line that ships
with spec 025. The seeder (`Modules/Notifications/Seeding/NotificationsV1Seeder.cs`)
inserts these rows with `template_versions.ar_editorial_reviewed = false`. A
human Arabic-editorial reviewer MUST:

1. Read each row's `body_ar` and (where present) `subject_ar` in context.
2. Apply the rubric below.
3. For each row that passes, run the `Approve` admin endpoint with
   `ar_editorial_reviewed = true` (V-1 gate at `Approve` rejects otherwise).
4. Tick the row in this file in the PR that ratifies the review.

Once **all rows below are ticked**:

- Re-run the seeder with the approved AR copy (or PATCH the existing rows).
- Flip ADR-009 in `CLAUDE.md` from `Proposed (narrowed)` to `Accepted`.
- Mark T058 `[X]` in `tasks.md` (drop the `[~]` placeholder).
- Bump fingerprint via `scripts/compute-fingerprint.sh`.

## Rubric

For each Arabic string:

- **Editorial grade**: reads as native, dental-marketplace-appropriate
  Arabic — not literal translation of the English copy.
- **Tone**: matches the channel — transactional bodies are concise and
  factual; marketing bodies are warmer but not effusive.
- **Placeholders**: the `{{...}}` tokens are preserved bit-for-bit and grammar
  flows around them in both AR and EN. Watch for plural-agreement traps where
  AR needs different morphology than EN (e.g. order count = 1 vs 2 vs ≥3).
- **RTL**: no bidirectional-rendering accidents (mixed LTR fragments inside
  AR sentences must use proper RLM/LRM marks if needed; or be replaced with
  Arabic equivalents).
- **Number format**: dates / currencies use the market's localized form
  (KSA = SAR; EG = EGP) — driven by `currency` placeholder; reviewer checks
  the placeholder is used consistently.
- **Brand voice**: avoids machine-translation tells ("شكرًا لطلبك" vs the
  more natural "شكرًا لك على طلبك"), avoids over-formal phrasing in
  customer-facing copy.

## Rows

### Transactional event kinds (5 order + 2 refund + 2 verification + 1 OTP = 10 rows)

- [ ] `auth.otp_requested` — subject_ar + body_ar
- [ ] `order.placed` — subject_ar + body_ar
- [ ] `order.confirmed` — subject_ar + body_ar
- [ ] `order.shipped` — subject_ar + body_ar (carrier + tracking placeholders)
- [ ] `order.delivered` — subject_ar + body_ar
- [ ] `order.cancelled` — subject_ar + body_ar (reason placeholder)
- [ ] `order.refund_initiated` — subject_ar + body_ar (amount + currency placeholders)
- [ ] `order.refund_completed` — subject_ar + body_ar (amount + currency placeholders)
- [ ] `verification.approved` — subject_ar + body_ar
- [ ] `verification.rejected` — subject_ar + body_ar (reason placeholder)

### Marketing event kinds (4 rows)

- [ ] `pricing.price_dropped` — subject_ar + body_ar (product, old_price, new_price, currency placeholders)
- [ ] `inventory.restocked` — subject_ar + body_ar
- [ ] `cart.abandoned_24h` — subject_ar + body_ar (item_count, cart_total, currency placeholders)
- [ ] `shipping.status_changed` — subject_ar + body_ar (status placeholder)

### Per-market footer copy (AC-21)

Marketing emails carry an unsubscribe footer per market. The reviewer must
sign off on these two strings separately (they're plumbed at render-time by
`TemplateRenderer` + `MarketSchema.unsubscribe_footer_ar`).

- [ ] KSA marketing-footer AR copy (`market_schemas.sa.unsubscribe_footer_ar`)
- [ ] EG marketing-footer AR copy (`market_schemas.eg.unsubscribe_footer_ar`)

## Out of scope for this review

- English copy — reviewer focus is exclusively AR. EN bodies are owned
  by a separate copy-edit pass.
- Push notification truncation rules — handled by the push-channel renderer.
- Banner / hero / blog content — spec 024 CMS scope.

## Completion log

When the review pass closes, append:

```
Reviewer: <name>
Date: YYYY-MM-DD
Notes: <any deviations from the rubric>
Rows accepted: 14/14
Rows revised: <N> (see commit <sha>)
```
