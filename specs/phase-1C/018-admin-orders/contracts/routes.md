# Orders Routes

All routes inside spec 015's `(admin)` group; same auth-proxy + middleware order.

| Path | Permission required | Notes |
|---|---|---|
| `/orders` | `orders.read` | Orders list. |
| `/orders?refundFilter=any` | `orders.read` | The **Refunds** sub-entry (per spec FR-001). Same `/orders` page rendered with a preset `refundState != none` filter chip; the chip cannot be removed without leaving the entry (returning to `/orders`). No separate page is introduced. |
| `/orders/[orderId]` | `orders.read` | Order detail. Transition actions gated separately by `orders.fulfillment.write`, `orders.payment.write`, `orders.cancel`, etc. |
| `/orders/[orderId]/refund` | `orders.refund.initiate` | Refund flow (intercepting route — opens as side-panel over detail; renders standalone on direct nav). |
| `/orders/[orderId]/invoice` | `orders.invoice.read` | Invoice section drilldown. Regenerate gated on `orders.invoice.regenerate`. |
| `/orders/exports` | `orders.export` | Export jobs list. |
| `/orders/exports/[jobId]` | `orders.export` | Export job detail (filter snapshot + status + download). |

## Permission matrix (initial)

```
orders.read
orders.pii.read                  # email / phone visibility on the customer card
orders.fulfillment.write         # mark packed / handed-to-carrier / delivered transitions
orders.payment.write             # admin-driven payment-state forces (rare; gated on platform-level role)
orders.cancel                    # cancel order
orders.refund.initiate           # refund flow
orders.invoice.read              # invoice download
orders.invoice.regenerate        # invoice retry
orders.export                    # export create + view
orders.quote.read                # source-quote chip visibility (defined when spec 021 ships)
```

The shell already enforces middleware permission checks. New keys above are added to spec 004's permission catalog via the standard escalation channel (file `spec-004:gap:orders-permissions` if any are missing on day 1).

## Sidebar group registration

When the admin's nav-manifest includes any `orders.*` read key, spec 015's sidebar surfaces an "Orders" group with the relevant sub-entries. Entries are contributed via the manifest, never hard-coded.

## Refund intercepting-route pattern

`app/(admin)/orders/[orderId]/refund/page.tsx` is mounted via Next.js intercepting routes:

- Direct navigation (browser refresh / share) → renders the form as a full page.
- In-app navigation from the detail page → intercepts and renders as a side-panel over the detail.

Both paths use the same component tree; only the layout container differs. This preserves a shareable URL while keeping the in-flow UX side-panel-friendly.

## Step-up assertion lifecycle

```
[refund submit above threshold]
        ↓ (refund form opens StepUpDialog)
POST /api/auth/step-up/start                  (Next.js route handler proxying spec 004)
        ↓ (returns challenge id)
[StepUpDialog presents challenge UI]
        ↓ (admin completes TOTP / push)
POST /api/auth/step-up/complete               (Next.js proxy)
        ↓ (returns assertion id, TTL = 5 min default)
POST /api/orders/[orderId]/refund             (with X-StepUp-Assertion header)
        ↓ (spec 013 verifies assertion id with spec 004)
[Submitted]
```

Assertion ids are short-lived; refund retries within the TTL reuse the same id. After TTL expires, the form re-prompts step-up.
