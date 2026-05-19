# Spec — Phase 4: Customer Mobile Cart & Checkout

> **Phase:** 4 of 8 · **Owner:** mobile + checkout + pricing · **Last updated:** 2026-05-19
> **OpenAPI sources:** [`openapi.checkout.json`](../../../services/backend_api/openapi.checkout.json), [`openapi.pricing.json`](../../../services/backend_api/openapi.pricing.json), [`openapi.inventory.json`](../../../services/backend_api/openapi.inventory.json), and `identity.json` for address management (addresses are part of the customer profile; see notes below).
> **Endpoint count:** 8 checkout + 1 pricing + 1 inventory = 10 customer-callable.
> **Depends on:** Phase 1 (foundation; address management from the customer profile `me` payload — see BR-10), Phase 2 (price/inventory gateways, PDP price-quote contract).

---

## 1. Goal

Complete the customer purchase flow end-to-end: cart panel with pricing engine, promo/coupon entry, multi-step checkout (address → shipping → payment → review/submit), drift handling on conflicting cart state, COD + bank-transfer flows, and a confirmation screen that hands off to Phase 5 orders.

Cart is **client-state**. The backend touches the cart only via `POST /customer/pricing/price-cart` (preview, never mutates) and `POST /v1/customer/checkout/sessions` (materialize for checkout).

## 2. User roles

| Role | Phase 4 scope |
|---|---|
| Unauthenticated visitor | Can build a local cart; on Proceed to Checkout, routes to Login (Phase 1) preserving the cart in `SessionStore`. |
| Authenticated consumer | Full checkout: address, shipping, payment, submit. COD eligibility per market. |
| Authenticated B2B buyer | Same surface; pricing engine returns business-tier prices. Coupon entry respects B2B eligibility. |
| Restricted-product blocker | Add-to-cart enforces gate: cart still allows the item if added before verification was revoked (server returns 409 drift on price-cart); submit forces resolution. |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Cart lives in `lib/features/cart/data/cart_store.dart` (in-memory + `shared_preferences` persistence). Survives app restarts; cleared on submit-success and on sign-out (configurable via setting, default clear). | Principle 11 |
| BR-2 | All totals come from `POST /customer/pricing/price-cart`. UI never computes totals locally. | Principle 10 |
| BR-3 | Checkout submit MUST send `Idempotency-Key`; the key is generated when the user enters Review screen and reused across retries of submit. | Principle 13 |
| BR-4 | Drift (price/quantity/availability changes after checkout session creation) returns 409 from any checkout PATCH or submit; client renders `ConflictDialog` from Phase 1 with delta and forces user to accept or refresh. | Principle 24 |
| BR-5 | Payment methods are server-driven per market via the checkout session's "available methods" payload — never hardcoded client-side. | Principle 13 |
| BR-6 | COD eligibility evaluated server-side; if ineligible, the COD option is absent from the available methods list. | Principle 5, 13 |
| BR-7 | Bank-transfer flow shows a reference number + bank details after submit; order enters `pending_bank_transfer` state. | Principle 17 |
| BR-8 | Failed payment (provider declines, etc.) routes user back to Payment step with an error banner; retry uses the same checkout session and same Idempotency-Key. | Principle 13 |
| BR-9 | Cart panel batches `inventory/availability` for all line items on every open; lines that became unavailable get a strike-through + remove CTA. | Principle 11 |
| BR-10 | Addresses are managed in the customer profile. Checkout READS the existing address list from the `me` payload and the checkout session summary; checkout WRITES the chosen/new address via `PATCH /v1/customer/checkout/sessions/{sessionId}/address`. Standalone address CRUD outside of checkout (add/edit/remove from `/more/addresses`) is **out of Phase 4 scope** — the existing `apps/customer_flutter/lib/features/more/screens/addresses_screen.dart` is treated as a Phase 1 More-hub destination. **Gap documented:** if the OpenAPI surface lacks dedicated profile-level address CRUD endpoints at the time of Phase 4 implementation, the More-hub addresses screen reads-only from `me` and add/edit flows route the user into a checkout-session-scoped flow; resolution belongs to the team owning the identity contract (spec 004), not to Phase 4. | Principle 27 |

## 4. Screens

### S-4.1 Cart

**Status:** **Done** — `features/cart/screens/cart_screen.dart` exists; verify against this spec.
**Route:** `/cart` · **Bottom nav:** visible (Cart tab)
**OpenAPI source:** `openapi.pricing.json` + `openapi.inventory.json`
**Wireframe:** [`#phase-4-cart`](../../../docs/mobile-screens-wireframes.md#phase-4-cart--s-41-cart)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /customer/pricing/price-cart | on cart change, coupon entry, mount | safe* | preview only |
| GET | /v1/customer/inventory/availability | on mount + after each cart change | safe | batched |

#### Response data shape
See Phase 2 data-model for both shapes (`price-cart` preview, `availability`).

#### UI states

| State | Trigger | What renders |
|---|---|---|
| empty | empty cart | empty illustration + "Continue shopping" CTA |
| loading-quote | price-cart in-flight | inline spinner over totals row |
| loaded | both calls 2xx | line items + promo input + totals + Proceed |
| validation-coupon | 422 on coupon | inline error under coupon input |
| availability-drift | inventory returns `inStock=false` on a line | line shown strikethrough + Remove CTA + warning banner |
| error-5xx | 5xx | retry banner over totals row |
| offline | network | cached totals + offline badge; coupon entry disabled |

#### Bloc scaffold
- `CartBloc` reads/writes `CartStore`.
- Events: `CartStarted`, `CartLineQtyChanged(productId, qty)`, `CartLineRemoved(productId)`, `CartCouponApplied(code)`, `CartCouponCleared`, `CartRefreshed`, `CartProceedRequested`.
- States: `CartLoading`, `CartLoaded({lines, quote, availabilityById, coupon, error?})`, `CartEmpty`, `CartFailure(reason, correlationId)`, `CartProceeding(checkoutSessionId)`.

#### Acceptance criteria
- [ ] Quantity changes debounced 300 ms then trigger price-cart.
- [ ] Coupon entry: optimistic UI then 422-on-failure rollback.
- [ ] Drift line: strikethrough + Remove + a banner explaining "Item became unavailable in your market".
- [ ] Proceed disabled while a quote is in-flight or any drift line is unresolved.
- [ ] AR currency placement + EN currency placement.
- [ ] Tests.

#### Edge cases
- Adding the 6th of a max-5 item ⇒ qty clamps + toast.
- Cart with 0 lines on cold open from previous session ⇒ render `empty` directly.

---

### S-4.3 Checkout start

**Route:** `/checkout` · **Bottom nav:** hidden (modal flow)
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-checkout-start`](../../../docs/mobile-screens-wireframes.md#phase-4-checkout-start--s-43-checkout-start)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/checkout/sessions | on screen mount (with cart payload) | yes | materializes cart server-side |

#### Response data shape
```json
{
  "sessionId": "uuid",
  "expiresAt": "iso8601",
  "summary": { /* same shape as summary endpoint */ },
  "availableSteps": ["address", "shipping", "payment", "review"]
}
```

#### UI states
loading on mount → loaded routes immediately to `/checkout/summary` (S-4.4); on 409 from drift (server saw a cart mismatch vs the materialized expectation), show `ConflictDialog` referencing cart deltas.

#### Bloc scaffold
- `CheckoutStartBloc`.
- Events: `CheckoutStarted(cartSnapshot)`, `CheckoutRetried`.
- States: `CheckoutStarting`, `CheckoutStarted(sessionId)`, `CheckoutStartFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] Single POST; no retry without explicit user action.
- [ ] Session id persisted to `SessionStore.checkout` so the back stack can resume.
- [ ] On 409 drift: surface dialog with line-level deltas; on Accept → re-POST.
- [ ] Tests.

---

### S-4.4 Checkout summary

**Route:** `/checkout/{sessionId}/summary` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-summary`](../../../docs/mobile-screens-wireframes.md#phase-4-summary--s-44-checkout-summary)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/checkout/sessions/{sessionId}/summary | mount + after each step | safe | |

#### Response data shape
```json
{
  "sessionId": "uuid",
  "expiresAt": "iso8601",
  "lines": [{ "productId": "uuid", "name": "string", "qty": 1, "unitPrice": "120.00", "lineTotal": "120.00" }],
  "address": { /* AddressDto */ } ,
  "shipping": { "method": "string?", "cost": { "amount": "15.00", "currency": "SAR" } },
  "payment": { "method": "card | apple_pay | mada | stc_pay | bank_transfer | cod | tabby | tamara | valu | meeza" },
  "totals": { "subtotal": "...", "discount": "...", "tax": "...", "shipping": "...", "grandTotal": "..." },
  "stepStatus": { "address": "complete | pending", "shipping": "...", "payment": "...", "review": "..." }
}
```

#### UI states
loading skeleton → loaded with stepper; error/offline standard.

#### Bloc scaffold
- `CheckoutSummaryBloc`. Other step Blocs delegate refresh of summary via `CheckoutSummaryRefreshed` event.
- States standard.

#### Acceptance criteria
- [ ] Stepper UI reflects `stepStatus`.
- [ ] Continue button activates only when next step is `pending` and prerequisites are met.
- [ ] Pull-to-refresh re-fetches summary.
- [ ] Tests.

---

### S-4.5 Address step

**Route:** `/checkout/{sessionId}/address` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-address`](../../../docs/mobile-screens-wireframes.md#phase-4-address--s-45-address-step)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| PATCH | /v1/customer/checkout/sessions/{sessionId}/address | submit | yes | |

#### Response data shape
Returns updated summary or address subset; see data-model.

#### UI states
form (pre-fill from saved address if any) → loading → loaded routes to S-4.6.

Address-list picker: shows existing addresses (from `me` profile addresses or a profile addresses endpoint). New address inline form covers name, phone, city/region, street.

#### Bloc scaffold
- `CheckoutAddressBloc`.
- Events: `AddressFieldChanged`, `AddressSubmitted`, `AddressPickerSelected(addressId)`.
- States: form + loading + success + failure.

#### Acceptance criteria
- [ ] Pre-fill from session's existing address if present.
- [ ] Phone normalized to E.164.
- [ ] On success: route to S-4.6 Shipping.
- [ ] On 409 drift: ConflictDialog.
- [ ] Tests.

---

### S-4.6 Shipping step

**Route:** `/checkout/{sessionId}/shipping` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-shipping-quotes`](../../../docs/mobile-screens-wireframes.md#phase-4-shipping-quotes--s-46-shipping-step)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/checkout/sessions/{sessionId}/shipping-quotes | mount | safe | |
| PATCH | /v1/customer/checkout/sessions/{sessionId}/shipping | submit | yes | |

#### Response data shape
```json
// shipping-quotes
[
  { "method": "standard", "label": "string", "price": { "amount": "15.00", "currency": "SAR" }, "etaDays": "2-3" }
]
```

#### UI states
loading quotes → list of radio options → submit → route to S-4.7.

#### Bloc scaffold
- `CheckoutShippingBloc`. Events: started, selected, submitted. States: loading-quotes, loaded(options, selected?), submitting, submitted, failure.

#### Acceptance criteria
- [ ] Empty quotes ⇒ "No shipping methods available — change address" CTA.
- [ ] AR + EN.
- [ ] Tests.

---

### S-4.7 Payment step

**Route:** `/checkout/{sessionId}/payment` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-payment`](../../../docs/mobile-screens-wireframes.md#phase-4-payment--s-47-payment-step)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| PATCH | /v1/customer/checkout/sessions/{sessionId}/payment-method | submit | yes | |

#### Response data shape
Available methods come from the summary payload's `availableMethods` (server-driven per market — BR-5).

#### UI states

| State | Trigger | What renders |
|---|---|---|
| loading | mount | spinner |
| loaded | 2xx | list of available methods (card, Apple Pay, Mada, STC Pay, Tabby/Tamara KSA, Valu/Meeza EG, COD, bank transfer) |
| submitting | PATCH in-flight | button spinner |
| success | 2xx | route to S-4.8 |
| error-422 | 422 | per-field validation (card no, expiry) |
| error-409 | 409 | ConflictDialog |

#### Bloc scaffold
- `CheckoutPaymentBloc`. Method-specific sub-states for card (hosted-fields token entry) vs hosted (Apple Pay, BNPL handoff) vs offline (COD, bank transfer).

#### Acceptance criteria
- [ ] PCI scope **SAQ-A**: card details NEVER leave the hosted fields; client passes a provider-issued token only (ADR-007).
- [ ] Apple Pay / Mada flows hand off via SDK; on return, PATCH carries the provider token.
- [ ] Tabby / Tamara hand-off via WebView per provider SDK; return route resumes session.
- [ ] COD / bank transfer: simple radio select, no extra fields beyond optional reference.
- [ ] AR copy editorial.
- [ ] Tests.

---

### S-4.8 Order review / submit

**Route:** `/checkout/{sessionId}/review` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-review-submit`](../../../docs/mobile-screens-wireframes.md#phase-4-review-submit--s-48-order-review--submit)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/checkout/sessions/{sessionId}/submit | Place Order tap | **Idempotency-Key required** | terminal |

#### Response data shape
```json
{
  "orderId": "uuid",
  "orderNumber": "2026-05-000123",
  "paymentState": "captured | pending | requires_action",
  "fulfillmentState": "pending",
  "redirect": { "kind": "3ds | provider_webview | none", "url": "string?" },
  "bankTransfer": { "reference": "string", "iban": "string", "amount": "..." } 
}
```

#### UI states
review summary → submit → spinner → success routes to S-4.10 OR routes to a WebView for 3DS/redirect → return resumes.

#### Bloc scaffold
- `CheckoutReviewBloc`. Generates `Idempotency-Key` once on entry; reuses on retries.
- Events: `ReviewStarted`, `ReviewSubmitted`, `ReviewRedirectReturned(result)`.
- States: `ReviewLoaded(summary, idempotencyKey)`, `ReviewSubmitting`, `ReviewRedirecting(url)`, `ReviewSuccess(orderId, paymentState, bankTransfer?)`, `ReviewFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] Idempotency-Key generated once and stored in Bloc state; identical on retry.
- [ ] 3DS / WebView return resumes the same Bloc; success path routes to S-4.10.
- [ ] On 409 drift: ConflictDialog; on accept-drift, call S-4.9.
- [ ] Bank transfer success branches to a dedicated state showing reference + IBAN + copy buttons.
- [ ] AR copy editorial.
- [ ] Tests.

---

### S-4.9 Drift handling (409 ConflictDialog)

**Route:** modal over the active step
**OpenAPI source:** `openapi.checkout.json`
**Wireframe:** [`#phase-4-drift`](../../../docs/mobile-screens-wireframes.md#phase-4-drift--s-49-drift--409-conflict)

#### Endpoints used

| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/checkout/sessions/{sessionId}/accept-drift | accept tap | yes | |

#### Response data shape
Returns refreshed summary.

#### UI states
modal "Prices changed" + old/new totals + Review vs Accept-and-pay.

#### Bloc scaffold
Reuses the active step's Bloc; drift handling implemented as a method on `CheckoutBaseBloc` that emits `ConflictDriftDetected(delta)` then `ConflictResolved`.

#### Acceptance criteria
- [ ] Modal shows itemized delta where the server provides it; falls back to total-only delta otherwise.
- [ ] Accept-and-pay calls accept-drift then retries the original step (submit or PATCH).
- [ ] Review routes back to S-4.4 with the refreshed summary.
- [ ] Tests.

---

### S-4.10 Order confirmation

**Status:** **Done** — `features/checkout/screens/order_confirmation_screen.dart` exists; verify.
**Route:** `/checkout/{sessionId}/confirmation` · **Bottom nav:** hidden
**OpenAPI source:** none (consumes submit response + Phase 5 order detail link)
**Wireframe:** [`#phase-4-confirmation`](../../../docs/mobile-screens-wireframes.md#phase-4-confirmation--s-410-order-confirmation)

#### UI states
success illustration + order number + bank-transfer reference (if applicable) + View Order CTA (to Phase 5 S-5.2) + Continue Shopping (Home).

#### Acceptance criteria
- [ ] Cart is cleared on entry to this screen.
- [ ] Bank-transfer reference is copyable.
- [ ] AR copy editorial.
- [ ] Tests.

---

## 5. State machine — checkout session (client-side view)

Server is the source of truth (Principle 24). Client mirrors the relevant states:

```text
[Cart] --(POST sessions)--> [SessionCreated]
                                 |
                                 v
                         [AddressPending] --(PATCH address)--> [ShippingPending]
                                                                      |
                                                                      v
                                                              [PaymentPending] --(PATCH payment)--> [ReviewReady]
                                                                                                            |
                                                                                                            v
                                                                                                      [Submitting]
                                                                                                            |
                  ┌─────────────────────────────────────────────────────────────────┐                       v
                  │                                                                 │                  [3DS|WebView]
                  │                                                                 │                       │
                  ▼                                                                 ▼                       ▼
              [Conflict] <----- 409 on any PATCH/POST -----                  [Confirmed] -----> S-4.10
                  │                                                                 │
                  └---(accept-drift)---> resumes prior state                         └---(bank transfer)---> [Confirmed.BankTransferPending]
```

## 6. Acceptance criteria — phase-wide

- [ ] 10 screens above (including S-4.10 confirmation) pass per-screen DoD.
- [ ] Cart is client-state only; backend untouched by qty changes.
- [ ] Submit always sends Idempotency-Key; same key on retry.
- [ ] All totals come from price-cart preview or summary endpoints.
- [ ] Drift handled with reusable ConflictDialog.
- [ ] Payment methods are server-driven; no hardcoded enum in UI beyond an icon map.
- [ ] `flutter analyze` + `flutter test` green.
- [ ] §8 row → **Done**.

## 7. Dependencies

- Phase 1, Phase 2 (PricingGateway, InventoryGateway), Phase 5 (order detail entry from confirmation).
- Backend specs: 010 (checkout), 011 (orders for confirmation handoff), 007-a/b (pricing), 008 (inventory), 027 (payments — Phase 1E milestone 9).

## 8. Out of scope

- Saved card vaulting beyond provider tokenization (provider handles).
- Multi-address shipping (single delivery address only at launch).
- Gift orders / gifting flow.
- Partial fulfillment selection.

## 9. References

- Principles 5, 10, 11, 13, 24, 27, 28.
- ADR-002 (Bloc), ADR-007 (payments), ADR-008 (shipping), ADR-010 (data residency).
