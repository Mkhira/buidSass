# Spec 007-b — Arabic Editorial Review Queue

Per Constitution Principle 4 and SC-007, every customer-visible Arabic string in
spec 007-b MUST be reviewed by an editorial-grade Arabic reviewer before launch.

This file is the queue. Strings marked `[ ]` need review. Mark `[x]` once an
editorial reviewer signs off (annotate with reviewer initials + date).

This file blocks launch but NOT PR merge. The reviewer sign-off can land in a
follow-up.

## Sources

### `pricing.commercial.ar.icu` — operator-facing reason codes

Every key in this file is operator/admin-facing (shown in spec 015 admin UI
error toasts + audit panel detail). 50 keys total.

- [ ] All 50 ICU keys reviewed for editorial-grade Arabic phrasing (no machine
      translation, no English-as-Arabic word order, professional register).

### `PromotionsV1DevSeeder` (spec 007-b T118) — synthetic AR labels

Dev seeder populates ~30 customer-visible AR strings:

- [ ] Coupon labels (6 coupons spanning every lifecycle state × {percent_off, amount_off}).
- [ ] Promotion labels (4 promotions).
- [ ] Campaign names (3 campaigns).
- [ ] Coupon descriptions (where present).

These are dev-only strings but should still pass an editorial pass so the
training corpus is good.

## Reviewer attribution

| File | Reviewer | Sign-off date |
|---|---|---|
| `pricing.commercial.ar.icu` | _pending_ | _pending_ |
| `PromotionsV1DevSeeder` (synthetic labels) | _pending_ | _pending_ |
