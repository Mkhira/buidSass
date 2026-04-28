# Client-emitted events

Events emitted by the customer app shell into the `TelemetryAdapter` (R10). v1 implementation is `NoopTelemetryAdapter` — these events are observable in `Dev` via the `ConsoleTelemetryAdapter` only. A real provider lands in the notifications / observability spec.

## Event vocabulary

| Event | Trigger | Properties |
|---|---|---|
| `app.cold_start` | First-frame rendered after launch | `platform`, `locale`, `market`, `cold_start_ms` |
| `app.foregrounded` | App returns to foreground after ≥ 30 s background | `bg_duration_ms` |
| `auth.register.started` | User taps **Register** | `entry_point` (where they came from) |
| `auth.register.success` | Spec 004 register returns 2xx | — |
| `auth.register.failure` | Spec 004 register returns 4xx | `reason_code` |
| `auth.login.started` | User taps **Login** | `entry_point`, `continue_to_present` (bool) |
| `auth.login.success` | Spec 004 login returns 2xx | — |
| `auth.login.failure` | Spec 004 login returns 4xx | `reason_code` |
| `auth.otp.requested` | OTP screen opened with new challenge | `channel` (sms / email — from spec 004 response) |
| `auth.otp.resent` | User taps **Resend** | `channel` |
| `auth.otp.success` | Spec 004 OTP verify returns 2xx | — |
| `auth.otp.failure` | Spec 004 OTP verify returns 4xx | `reason_code` |
| `auth.password.reset.requested` | User submits reset email | — |
| `auth.password.reset.completed` | Reset confirm screen returns 2xx | — |
| `language.toggled` | User toggles AR ↔ EN | `from`, `to` |
| `home.opened` | Home screen reached interactive state | `time_to_interactive_ms` |
| `listing.opened` | Listing screen rendered | `category_id?`, `query_present` (bool) |
| `listing.facet.applied` | User applies a facet | `facet_kind` |
| `listing.sort.changed` | User changes sort | `sort_key` |
| `detail.opened` | Product detail rendered | `product_id`, `is_restricted` |
| `cart.add` | User taps **Add to cart** | `product_id`, `qty`, `is_restricted_and_unverified` |
| `cart.opened` | Cart screen reached | `revision`, `line_count` |
| `cart.line.removed` | User removes a line | `product_id` |
| `cart.line.qty.changed` | User changes a line qty | `product_id`, `delta` |
| `cart.out_of_sync.detected` | Cart screen surfaces "updated elsewhere" notice | — |
| `checkout.started` | Spec 010 session created | — |
| `checkout.address.selected` | User selects an address | — |
| `checkout.shipping.selected` | User selects a shipping quote | — |
| `checkout.payment.selected` | User selects a payment method | `method_kind` |
| `checkout.submit.tapped` | User taps **Submit** | — |
| `checkout.submit.success` | Spec 010 submit returns 2xx | `order_id`, `payment_state`, `fulfillment_state` |
| `checkout.submit.drift` | Spec 010 submit returns drift | — |
| `checkout.submit.failure` | Spec 010 submit returns 4xx / 5xx | `reason_code` |
| `orders.list.opened` | Orders list rendered | — |
| `order.detail.opened` | Order detail rendered | `order_id` |
| `order.reorder.tapped` | User taps **Reorder** | `order_id`, `out_of_stock_count` |
| `order.support.tapped` | User taps **Support** | `order_id` |
| `more.address.added` | New address saved | — |
| `more.address.edited` | Address edited | — |
| `more.verification.cta.tapped` | User taps verification CTA | `is_placeholder` |
| `more.logout.tapped` | User logs out | — |

## PII guard rails

- Events MUST NOT carry email, phone, full name, address text, payment-method identifiers, or any free-text user input.
- `reason_code` values come from a closed enum defined per Phase 1B spec (e.g., `identity.lockout.active`, `cart.revision_mismatch`) — never the raw error message.
- `customer_id` is **not** included; the telemetry adapter is responsible for joining session + event downstream if a real provider needs it.
- A unit test under `test/observability/` asserts every event's property set against this allow-list.
