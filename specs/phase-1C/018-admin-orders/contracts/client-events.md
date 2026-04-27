# Client-emitted events (orders module)

Adds to spec 015's telemetry vocabulary. Same `TelemetryAdapter`, same PII guardrails.

| Event | Trigger | Properties |
|---|---|---|
| `orders.list.opened` | `/orders` rendered | `filter_keys` (sorted, no values), `row_count_bucket` |
| `orders.list.filter.applied` | Filter changed | `filter_kind` (closed enum) |
| `orders.list.sort.changed` | Sort column changed | `sort_key` (closed enum) |
| `orders.list.saved_view.applied` | Saved view selected | — |
| `orders.list.saved_view.created` | New saved view persisted | — |
| `orders.detail.opened` | `/orders/[id]` rendered | `b2b` (bool) |
| `orders.detail.timeline.stream_filtered` | Stream chip toggled | `stream` ('order'/'payment'/'fulfillment'/'refund'), `enabled` (bool) |
| `orders.transition.tapped` | Action button click | `machine`, `to_state` |
| `orders.transition.succeeded` | spec 011 returns 2xx | `machine`, `to_state` |
| `orders.transition.illegal_blocked` | spec 011 returns 409 | `machine`, `attempted_to_state` |
| `orders.transition.conflict_detected` | 412 returned | `machine` |
| `orders.refund.opened` | Refund flow opened | — |
| `orders.refund.line.toggled` | Line selected / deselected | — |
| `orders.refund.over_refund_blocked` | Eager validation blocked submit | — |
| `orders.refund.step_up.required` | Threshold check returned `requiresStepUp = true` | `is_full_amount` (bool) |
| `orders.refund.step_up.succeeded` | Step-up dialog completed | — |
| `orders.refund.step_up.cancelled` | Admin cancelled the step-up | — |
| `orders.refund.submitted` | Submit success | `is_full` (bool), `line_count_bucket` |
| `orders.refund.over_refund_server_blocked` | spec 013 returned 409 over-refund | — |
| `orders.refund.failed` | spec 013 returned other error | `reason_code` |
| `orders.invoice.opened` | Invoice section rendered | `status` |
| `orders.invoice.downloaded` | Download button clicked | — |
| `orders.invoice.regenerate.tapped` | Regenerate clicked | — |
| `orders.invoice.regenerate.succeeded` | Regen returned 2xx | — |
| `orders.invoice.regenerate.failed` | Regen failed | `reason_code` |
| `orders.source_quote.chip.tapped` | Source-quote chip clicked | `quote_admin_shipped` (bool) |
| `orders.customer.chip.tapped` | Customer chip clicked | `customer_admin_shipped` (bool) |
| `orders.export.requested` | Export submit | `filter_keys` (sorted), `row_count_bucket_estimate` |
| `orders.export.completed` | Job reached `done` | `duration_bucket`, `row_count_bucket` |
| `orders.export.failed` | Job reached `failed` | `reason_code` |

## PII guard rails

- No order id, customer id, customer email / phone, line item product id, refund amount value, or filter VALUE leaks. `filter_keys` is the **set of filter names** that have a value — never the values themselves.
- `*_bucket` properties collapse into closed-enum power-of-10 ranges (e.g., row counts: `xs` ≤ 10, `s` ≤ 100, `m` ≤ 1000, `l` ≤ 10000, `xl` > 10000).
- `reason_code` values come from the closed catalogs of specs 011 / 012 / 013.
- Test `tests/unit/orders/telemetry.pii-guard.test.ts` asserts every event's property set against the allow-list.
