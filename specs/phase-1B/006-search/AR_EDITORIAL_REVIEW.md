# AR Editorial Review — Search v1

**Spec**: `006-search`  
**Bundle**: `services/backend_api/Modules/Search/Messages/search.ar.icu`  
**Status**: **needs-ar-editorial-review** — translation surface is complete, but final dental-domain native review is pending.

Constitution Principle 4 requires editorial-grade Arabic across all user-facing reason codes.
This implementation pass ensured key parity and terminology consistency, but it does not replace
human editorial sign-off.

`needs-ar-editorial-review: true`

## Reviewed Key Set

All current search reason-code keys are present in Arabic and English:

| Key | Arabic copy |
|---|---|
| `search.engine_unavailable` | خدمة البحث غير متاحة حاليًا. يُرجى إعادة المحاولة بعد قليل. |
| `search.reindex.in_progress` | توجد عملية إعادة فهرسة جارية لهذا الفهرس حاليًا. |
| `search.invalid_sort` | خيار الفرز المطلوب غير مدعوم. |
| `search.invalid_status` | عامل تصفية الحالة المطلوب غير مدعوم. |
| `search.invalid_locale` | اللغة المطلوبة غير صالحة. |
| `search.invalid_market` | السوق المطلوب غير صالح. |
| `search.market_locale_index_missing` | لا يوجد فهرس مطابق للسوق واللغة المطلوبَين. |
| `search.auth_required` | يلزم رمز وصول إداري (JWT) لتنفيذ هذا الإجراء. |
| `search.no_matches` | لا توجد منتجات مطابقة لطلب البحث. |
| `search.restricted_market` | بعض المنتجات مقيّدة في السوق المحدد. |

## Required Sign-off

1. Native Arabic dental-domain reviewer validates tone/terminology per key.
2. Any approved wording changes are applied directly to `search.ar.icu`.
3. Set this file status to `passed` and remove PR label `needs-ar-editorial-review`.
