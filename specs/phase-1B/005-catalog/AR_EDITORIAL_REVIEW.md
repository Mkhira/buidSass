# AR Editorial Review — Catalog v1

**Spec**: 005-catalog
**Bundle**: `services/backend_api/Modules/Catalog/Messages/catalog.ar.icu`
**Status**: **needs-ar-editorial-review** — AI-assisted translation pending human dental-domain editorial pass.

Constitution Principle 4 requires editorial-grade Arabic (never machine-translated) across every
user-facing surface. The strings in `catalog.ar.icu` were produced during spec 005 implementation
as provisional translations paired 1:1 with each reason code emitted by the catalog handlers. They
cover the message surface but have not been reviewed by a dental-domain native speaker.

The corresponding label stays `needs-ar-editorial-review` on the PR until the review is complete.
When a reviewer signs off each key, replace this document's status line with `passed` and remove
the label.

## Keys requiring editorial review

All 15 keys in `catalog.ar.icu` need human review:

| Key | Provisional Arabic | Notes for reviewer |
|---|---|---|
| `catalog.common.ok` | تمت العملية بنجاح. | Generic — tone matches brand (formal, medical-marketplace). |
| `catalog.common.denied` | لا يُسمح لك بتنفيذ هذا الإجراء. | Verify "يُسمح" vocalisation marks render in RTL UI. |
| `catalog.product.not_found` | المنتج غير موجود. | Verify noun definiteness (الـ prefix) in a list context. |
| `catalog.product.invalid_transition` | الانتقال المطلوب لحالة المنتج غير مسموح. | "الانتقال" may be too technical — consider "التغيير". |
| `catalog.publish.media_required` | يلزم وجود صورة رئيسية واحدة على الأقل قبل النشر. | "صورة رئيسية" — confirm against UX spec wording for "primary image". |
| `catalog.publish.locale_required` | يلزم توفير المحتوى بالعربية والإنجليزية قبل النشر. | Confirm locale names are capitalised per style guide. |
| `catalog.publish.market_unconfigured` | يشير المنتج إلى سوق غير مُعرَّفة ضمن الإعدادات. | "سوق" gender agreement with "مُعرَّفة". |
| `catalog.restricted.verification_required` | يتطلب هذا المنتج تحقق الترخيص المهني. | "تحقق الترخيص" — dental licence-verification term needs dental-domain confirmation. |
| `catalog.brand.unknown` | العلامة التجارية المطلوبة غير موجودة. | OK generic. |
| `catalog.category.cycle_detected` | يؤدي إعادة الإسناد المطلوب إلى حلقة مغلقة. | "إعادة الإسناد" — may read as jargon. Consider rephrasing as "نقل التصنيف". |
| `catalog.category.in_use` | التصنيف لا يزال مرتبطًا بمنتجات نشطة. | OK. |
| `catalog.schedule.past_time` | يجب أن يكون وقت النشر المجدول في المستقبل. | OK. |
| `catalog.attributes.schema_violation` | لا تطابق الخصائص المرسلة مخطط التصنيف. | "مخطط" — schema is ambiguous (could read as "plan"). Consider "قالب". |
| `catalog.slug.immutable` | لا يمكن تغيير المعرف الودّي بعد أول نشر للمنتج. | "المعرف الودّي" is the standard Arabic translation for slug; confirm it's used consistently across the platform. |
| `catalog.bulk.row_idempotent_duplicate` | تم استلام صف بنفس مفتاح الخاصية المثالية مسبقًا. | "الخاصية المثالية" for "idempotency" is awkward. Consider "مفتاح عدم التكرار". |

## Sign-off process

1. Dental-domain native speaker reviews each row.
2. For each key, either: (a) keep as-is (write "OK"), (b) replace with an alternative, or
   (c) flag for product-team decision.
3. Update `catalog.ar.icu` with the approved wording.
4. Flip this file's status line to `passed` and timestamp the review.
