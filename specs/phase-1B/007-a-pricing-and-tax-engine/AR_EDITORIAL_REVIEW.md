# AR Editorial Review — Pricing & Tax Engine v1

**Spec**: 007-a-pricing-and-tax-engine
**Bundle**: `services/backend_api/Modules/Pricing/Messages/pricing.ar.icu`
**Status**: **needs-ar-editorial-review** — AI-assisted translation pending dental-domain native speaker editorial pass.

Constitution Principle 4 requires editorial-grade Arabic (never machine-translated) for every
user-facing surface. The 23 keys in `pricing.ar.icu` were produced during spec 007-a
implementation as provisional translations paired 1:1 with each reason code emitted by the
pricing handlers (customer + admin + internal). They cover the surface but have not been
reviewed by a native speaker in the dental-commerce / KSA & EG financial idiom.

The corresponding label stays `needs-ar-editorial-review` on the PR until review is complete.
When a reviewer signs off each key, replace this document's status line with `passed` and
remove the label.

## Keys requiring editorial review

All 23 keys in `pricing.ar.icu` need human review:

| Key | Provisional Arabic | Notes for reviewer |
|---|---|---|
| `pricing.product.not_found` | المنتج المطلوب غير متوفر. | OK generic. |
| `pricing.product.no_price` | لا يوجد سعر مكوّن لهذا المنتج. | "مكوّن" may read as "component"; confirm intent = "configured/set". |
| `pricing.invalid_qty` | يجب أن تكون الكمية 1 على الأقل. | OK. |
| `pricing.lines_required` | يُطلب سطر واحد على الأقل. | "سطر" is accounting-style "line item"; confirm against UX cart wording. |
| `pricing.currency_mismatch` | المنتج لا يُباع في هذه السوق. | Verify "السوق" gender agreement throughout ar.icu. |
| `pricing.tax_rate_missing` | لا توجد نسبة ضريبة نشطة لهذه السوق. | "نسبة ضريبة" — confirm vs "معدّل ضريبي" per finance team. |
| `pricing.coupon.invalid` | رمز الكوبون غير صالح. | "الكوبون" vs "القسيمة" — pick one consistently. |
| `pricing.coupon.expired` | انتهت صلاحية الكوبون. | OK. |
| `pricing.coupon.limit_reached` | تم الوصول إلى الحد الأقصى لاستخدام الكوبون. | Long; acceptable for API messages. |
| `pricing.coupon.excludes_restricted` | لا يمكن تطبيق الكوبون على المنتجات المقيّدة. | "المقيّدة" — dental-domain term for restricted. |
| `pricing.coupon.already_applied` | يمكن تطبيق كوبون واحد فقط في وقت واحد. | OK. |
| `pricing.coupon.duplicate_code` | يوجد كوبون آخر بنفس الرمز. | Confirm "الرمز" vs "الكود". |
| `pricing.tax.invalid_rate` | يجب أن تكون نسبة الضريبة بين 0 و 10000 نقطة أساس. | "نقطة أساس" (basis points) is technical — admin-only surface, OK. |
| `pricing.tax.not_found` | لم يتم العثور على نسبة الضريبة. | OK. |
| `pricing.promotion.not_found` | لم يتم العثور على العرض الترويجي. | OK. |
| `pricing.tier.not_found` | لم يتم العثور على فئة الأعمال. | "فئة الأعمال" = "business tier"; confirm vs "شريحة الأعمال". |
| `pricing.tier.duplicate_slug` | توجد فئة بنفس المعرّف. | "المعرّف" = slug — consistent with catalog. |
| `pricing.tier_price.invalid` | يجب أن يكون سعر الفئة أكبر من أو يساوي صفر. | OK. |
| `pricing.tier_price.not_found` | لم يتم العثور على سعر الفئة. | OK. |
| `pricing.explanation.not_found` | لم يتم العثور على تفسير السعر. | "تفسير السعر" — admin-only. Confirm vs "شرح السعر". |
| `pricing.explanation.invalid_kind` | يجب أن يكون نوع المالك عرض سعر أو طلب أو معاينة. | "عرض سعر" = quote. "طلب" = order. "معاينة" = preview. OK. |
| `pricing.invalid_mode` | يجب أن يكون الوضع معاينة أو إصدار. | OK. |
| `pricing.floor.clamped_to_zero` | تم تثبيت الإجمالي عند صفر. | Edge case; confirm "تثبيت" vs "حصر". |

## Sign-off process

1. Dental-domain native speaker reviews each row.
2. For each key, either keep as-is, replace with an alternative, or flag for product decision.
3. Update `pricing.ar.icu` with the approved wording.
4. Flip this file's status line to `passed` and timestamp the review.
