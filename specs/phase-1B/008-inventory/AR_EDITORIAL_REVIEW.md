# AR Editorial Review — Inventory v1

**Spec**: 008-inventory
**Bundle**: `services/backend_api/Modules/Inventory/Messages/inventory.{ar,en}.icu`
**Status**: **needs-ar-editorial-review** — provisional AI-assisted Arabic copy pending dental-domain editorial sign-off.

Constitution Principle 4 requires editorial-grade Arabic for all user-facing messaging. The keys
below were drafted during implementation and must be reviewed by a native Arabic editor familiar
with KSA/EG commerce and medical-market terminology.

## Keys requiring editorial review

| Key | Provisional Arabic | Reviewer notes |
|---|---|---|
| `inventory.insufficient` | الكمية المطلوبة تتجاوز المخزون المتاح. | Validate storefront tone for shortage messaging. |
| `inventory.warehouse_market_mismatch` | لا يمكن حجز المنتج من مستودع هذا السوق. | Confirm wording for "market warehouse" in Arabic UX. |
| `inventory.batch_qty_negative` | لا يمكن أن تكون كمية الدفعة سالبة. | Confirm "دفعة" vs "تشغيلة" consistency. |
| `inventory.negative_on_hand_blocked` | العملية ستؤدي إلى كمية مخزون سالبة. | Admin-facing phrasing; ensure clear actionability. |
| `inventory.reservation.not_found` | لم يتم العثور على الحجز. | Confirm noun choice for reservation. |
| `inventory.reservation.expired` | انتهت صلاحية الحجز. | Confirm tone and tense consistency. |
| `inventory.reservation.already_converted` | تم تحويل الحجز بالفعل. | Confirm "تحويل" wording in checkout context. |
| `inventory.batch.duplicate_lot` | توجد دفعة أخرى بنفس رقم التشغيلة. | Validate lot terminology. |
| `inventory.batch.not_found` | لم يتم العثور على دفعة المخزون. | Verify preferred wording for inventory batch. |
| `inventory.invalid_items` | حمولة عناصر الحجز غير صالحة. | Consider replacing technical "حمولة" if needed. |
| `inventory.invalid_qty` | يجب أن تكون الكمية أكبر من صفر. | Confirm numerical phrasing style. |
| `inventory.invalid_order_id` | يجب توفير معرف الطلب. | Confirm "معرف" vs "رقم" based on product copy. |

## Sign-off workflow

1. Review each key with a native Arabic editor.
2. Apply approved wording in `inventory.ar.icu`.
3. Update this file status to `passed` with reviewer/date.
4. Remove `needs-ar-editorial-review` label from PR once approved.
